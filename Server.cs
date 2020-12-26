using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;

namespace CLC
{
public class Server
{
    public class Connection
    {
        public TcpClient client = null;
        public NetworkStream stream = null;
        
        public string ip = "";
        
        public byte[] idBytes = null;
        
        public Mutex myMut = new Mutex();
        
        public int errorCount = 0;
        
        public Connection(TcpClient client, NetworkStream stream)
        {
            this.client = client;
            this.stream = stream;
            
            ip = (((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString() + ":" + ((IPEndPoint)client.Client.RemoteEndPoint).Port.ToString());
            
            //string[] words = (((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()).Split('.');
            //byte b0 = (byte)Int32.Parse(words[0]);
            //byte b1 = (byte)Int32.Parse(words[1]);
            //byte b2 = (byte)Int32.Parse(words[2]);
            //byte b3 = (byte)Int32.Parse(words[3]);
            //
            //byte[] prt  = BitConverter.GetBytes((int)(((IPEndPoint)client.Client.RemoteEndPoint).Port));
            //idBytes = new byte[8];
            //idBytes[0] = b0;
            //idBytes[1] = b1;
            //idBytes[2] = b2;
            //idBytes[3] = b3;
            //idBytes[4] = prt[0];
            //idBytes[5] = prt[1];
            //idBytes[6] = prt[2];
            //idBytes[7] = prt[3];
        }
        
        public void sendData(byte[] data, int numToWrite)
        {
            if (errorCount > 0)
            {
                return;
            }
            myMut.WaitOne();
            try
            {
                stream.Write(data, 0, numToWrite);
            }
            catch (Exception e)
            {
                Console.WriteLine(ip + " exception while writing:" + e.ToString());
                errorCount++;
            }
            myMut.ReleaseMutex();
        }
    }
    
    public static Mutex mut = new Mutex();
    public static List<Connection> connections = null;
    
    public static bool loop = true;
    
    public static void Main()
    {
        var listener = new TcpListener(IPAddress.Any, 25566);
        listener.Start();
        Console.WriteLine("waiting...");
        
        connections = new List<Connection>();
        
        while (loop)
        {
            TcpClient client = listener.AcceptTcpClient();
            
            Connection conn = new Connection(client, client.GetStream());
            
            Console.WriteLine("Connected to " + conn.ip);
            
            mut.WaitOne();
            connections.Add(conn);
            Thread thread = new Thread(() => handleThread(conn));
            mut.ReleaseMutex();
            
            thread.Start();
        }
    }
    
    public static void handleThread(Connection conn)
    {
        byte[] tempBuf = new byte[480000];
        
        long lastMessageTimestampMillis = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        
        while (loop)
        {
            long currentTimeMillis = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if ((currentTimeMillis - lastMessageTimestampMillis) > 3000) //we havent got a message in 3 second, assume we lost connection and close it.
            {
                Console.WriteLine("Disconnected from " + conn.ip + " because it was unresponsive");
                closeMe(conn);
                return;
            }
            
            if (conn.errorCount > 0)
            {
                Console.WriteLine("Disconnected from " + conn.ip + " while writing from it.");
                closeMe(conn);
                return;
            }
            
            //Read new audio data from us
            if (conn.stream.DataAvailable)
            {
                int numRead = 0;
                try
                {
                    numRead = conn.stream.Read(tempBuf, 0, tempBuf.Length);
                    //Console.WriteLine("Read {0} bytes", numRead);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Disconnected from " + conn.ip + " while reading from it: " + e.ToString());
                    closeMe(conn);
                    return;
                }
                
                if (conn.idBytes == null) //Very first data coming from this connection
                {
                    if (numRead >= 6)
                    {
                        conn.idBytes = new byte[6];
                        conn.idBytes[0] = tempBuf[0];
                        conn.idBytes[1] = tempBuf[1];
                        conn.idBytes[2] = tempBuf[2];
                        conn.idBytes[3] = tempBuf[3];
                        conn.idBytes[4] = tempBuf[4];
                        conn.idBytes[5] = tempBuf[5];
                    }
                    else
                    {
                        Console.WriteLine("Epic lol");
                        closeMe(conn);
                        return;
                    }
                }
                
                //Go through all the other connections and send our audio data
                mut.WaitOne();
                for (int i = 0; i < connections.Count; i++)
                {
                    if (connections[i] != conn)
                    {
                        //Console.WriteLine("sending data out");
                        connections[i].sendData(tempBuf, numRead);
                    }
                }
                mut.ReleaseMutex();
                
                lastMessageTimestampMillis = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }
            else
            {
                System.Threading.Thread.Sleep(1);
            }
        }
    }
    
    //Call from the thread that you want to close
    public static void closeMe(Connection conn)
    {
        //Console.WriteLine("closeMe start " + conn.ip);
        mut.WaitOne();
        try
        {
            conn.myMut.WaitOne();
            try
            {
                conn.client.Close();
                conn.stream.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception when trying to close client and stream for {0}: {1}", conn.ip, e.ToString());
            }
            conn.myMut.ReleaseMutex();
            
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i] != conn)
                {
                    try
                    {
                        byte[] dc = new byte[12];
                        dc[ 0] = conn.idBytes[0];
                        dc[ 1] = conn.idBytes[1];
                        dc[ 2] = conn.idBytes[2];
                        dc[ 3] = conn.idBytes[3];
                        dc[ 4] = conn.idBytes[4];
                        dc[ 5] = conn.idBytes[5];
                        dc[ 6] = 69;
                        dc[ 7] = 0;
                        dc[ 8] = 0;
                        dc[ 9] = 0;
                        dc[10] = 0;
                        dc[11] = 0;
                        connections[i].sendData(dc, 12);
                        //Console.WriteLine("Sending DC to "  + connections[i].ip);
                        //Console.WriteLine("{0} {1} {2} {3} {4} {5} {6} {7}", dc[0], dc[1], dc[2], dc[3], dc[4], dc[5], dc[6], dc[7]);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Exception when trying to send disconnect message to {0}: {1}", connections[i].ip, e.ToString());
                    }
                }
            }
            
            connections.Remove(conn);
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception when trying to send disconnect messages from {0}: {1}", conn.ip, e.ToString());
        }
        mut.ReleaseMutex();
        //Console.WriteLine("closeMe end " + conn.ip);
    }
}
}