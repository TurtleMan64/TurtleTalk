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
public class Network
{
    private class Message
    {
        public int id = 0;
        public Object data = null;
        
        public Message(int id, Object data)
        {
            this.id = id;
            this.data = data;
        }
    }
    private static Mutex mut = new Mutex();
    private static Queue<Message> messages = new Queue<Message>();
    
    private static TcpClient client = null;
    private static NetworkStream stream = null;
    
    private static List<byte> netIn = new List<byte>(); //Current input from the network (TODO = change this to circular buffer. Removing is expensive from List)
    private static byte[] tempBuf = new byte[480000]; //temporary input from network before getting added to netIn enough for 5 seconds of samples at 48000 hz
    
    private static GameReader.GameState gameState = null;
    
    public static ulong networkId = 0;
    
    public static void init()
    {
        //Thread to handle the network and not block
        new Thread(() =>
        {
            getPublicIPv4();
            
            while (CrewLinkClone.loop)
            {
                step();
            }
            
            if (stream != null)
            {
                stream.Close();
                stream = null;
            }
            if (client != null)
            {
                client.Close();
                client = null;
            }
            
            Console.WriteLine("Network thread closing.");
            
        }).Start();
    }
    
    private static void getPublicIPv4()
    {
        try
        {
            using (var client = new WebClient())
            {
                client.Headers["User-Agent"] =
                "Mozilla/4.0 (Compatible; Windows NT 5.1; MSIE 6.0) " +
                "(compatible; MSIE 6.0; Windows NT 5.1; " +
                ".NET CLR 1.1.4322; .NET CLR 2.0.50727)";

                try
                {
                    byte[] arr = client.DownloadData("http://checkip.amazonaws.com/");
                    string response = System.Text.Encoding.UTF8.GetString(arr);
                    string result = response.Trim();
                    Console.WriteLine("Public IPv4: " + result);
                    
                    string[] words = response.Split('.');
                    byte b0 = (byte)Int32.Parse(words[0]);
                    byte b1 = (byte)Int32.Parse(words[1]);
                    byte b2 = (byte)Int32.Parse(words[2]);
                    byte b3 = (byte)Int32.Parse(words[3]);
                    
                    byte[] buf = new byte[8];
                    buf[0] = b0;
                    buf[1] = b1;
                    buf[2] = b2;
                    buf[3] = b3;
                    
                    networkId = BitConverter.ToUInt64(buf, 0);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error when fetching public ipv4: " + e.ToString());
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Error when fetching public ipv4: " + e.ToString());
        }
    }
    
    public static void sendMessage(int message, Object data)
    {
        mut.WaitOne();
        messages.Enqueue(new Message(message, data));
        mut.ReleaseMutex();
    }
    
    private static Message getMessage()
    {
        mut.WaitOne();
        if (messages.Count == 0)
        {
            mut.ReleaseMutex();
            return new Message(0, null);
        }
        Message message = messages.Dequeue();
        mut.ReleaseMutex();
        return message;
    }
    
    private static bool hasMessages()
    {
        mut.WaitOne();
        if (messages.Count == 0)
        {
            mut.ReleaseMutex();
            return false;
        }
        mut.ReleaseMutex();
        return true;
    }
    
    //public static Random rnd = new Random();
    
    public static void step()
    {
        readFromServer();
        
        Message msg = getMessage();

        switch (msg.id)
        {
            case 0: //No messages = sleep
                System.Threading.Thread.Sleep(1);
                break;
            
            case 1: //Connect to a server
                try
                {
                    if (stream != null)
                    {
                        stream.Close();
                        stream = null;
                    }
                    if (client != null)
                    {
                        client.Close();
                        client = null;
                    }

                    string serverIp = (string)msg.data;
                    Int32 port = 25566;
                    client = new TcpClient(serverIp, port);
                    
                    //Updating the network Id to havee the locally bound port
                    byte[] localPort = BitConverter.GetBytes(((IPEndPoint)client.Client.LocalEndPoint).Port);
                    byte[] netId = BitConverter.GetBytes(networkId);
                    netId[4] = localPort[0];
                    netId[5] = localPort[1];
                    netId[6] = localPort[2];
                    netId[7] = localPort[3];
                    networkId = BitConverter.ToUInt64(netId, 0);
                    
                    // Get a client stream for reading and writing.
                    stream = client.GetStream();
                    stream.ReadTimeout  = 2500; //2.5 sec
                    stream.WriteTimeout = 2500; //2.5 sec
                }
                catch (Exception e)
                {
                    MessageBox.Show("Could not connect to server. Try again.");
                    Console.WriteLine("Exception when trying to establish connection to server: {0}", e);
                }
                break;
            
            case 2: //Send new audio data to server
                try
                {
                    if (stream != null && client != null)
                    {
                        //byte[] audioBuf = (byte[])msg.data;
                        //if (rnd.Next(100) > 80)
                        //{
                        //    System.Threading.Thread.Sleep(rnd.Next(80));
                        //}
                        //stream.Write(audioBuf, 0, audioBuf.Length);
                        //Console.WriteLine("Net out");
                        AudioPacket packet = (AudioPacket)msg.data;
                        byte[] buf = packet.generateNetworkStreamBytes();
                        stream.Write(buf, 0, buf.Length);
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show("Lost connection to the server. Try reconnecting.");
                    Console.WriteLine("Lost connection to server: {0}", e);
                    stream.Close();
                    client.Close();
                    stream = null;
                    client = null;
                }
                break;
            
            case 3: //Get local game state
                gameState = (GameReader.GameState)msg.data;
                break;
            
            default:
                break;
        }
    }
    
    private static void readFromServer()
    {
        try
        {
            if (stream != null && client != null)
            {
                if (stream.DataAvailable)
                {
                    //if (rnd.Next(100) > 80)
                    //{
                    //    System.Threading.Thread.Sleep(rnd.Next(80));
                    //}

                    int numRead = stream.Read(tempBuf, 0, tempBuf.Length);
                    for (int i = 0; i < numRead; i++)
                    {
                        netIn.Add(tempBuf[i]);
                    }
                    
                    AudioPacket packet = null;
                    do
                    {
                        //Console.WriteLine("Net in 1");
                        packet = AudioPacket.constructFromNetworkStreamBytes(netIn);
                        if (packet != null)
                        {
                            //Console.WriteLine("Net in 2");
                            AudioOut.sendMessage(3, packet);
                        }
                    }
                    while (packet != null);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception while reading: {0}", e);
            MessageBox.Show("Lost connection to the server. Try reconnecting.");
            stream.Close();
            client.Close();
            stream = null;
            client = null;
        }
    }
}
}