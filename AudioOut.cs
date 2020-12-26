using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections;
using System.Threading;

using NAudio.Wave;
using NAudio.CoreAudioApi;

using Concentus.Structs;

namespace CLC
{
public class AudioOut
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
    
    private static int deviceNumber = 0;
    
    private static Mutex playersMut = new Mutex();
    private static Dictionary<ulong, PlayerAudioOut> players = new Dictionary<ulong, PlayerAudioOut>();
    
    public static void init()
    {
        sendMessage(1, 0);
        
        //Thread to handle the window and not block
        new Thread(() =>
        {
            while (CrewLinkClone.loop)
            {
                step();
            }
            
            //kill all the players
            playersMut.WaitOne();
            foreach (KeyValuePair<ulong, PlayerAudioOut> entry in players)
            {
                entry.Value.sendMessage(2, null);
            }
            playersMut.ReleaseMutex();
            
            Console.WriteLine("Audio Out thread closed.");
            
        }).Start();
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
        if (messages.Count > 50)
        {
            messages.Clear();
            mut.ReleaseMutex();
            return new Message(0, null);
        }
        Message message = messages.Dequeue();
        mut.ReleaseMutex();
        return message;
    }
    
    public static void step()
    {
        Message msg = getMessage();
        //Console.WriteLine(msg);
        switch (msg.id)
        {
            case 0: //No messages
                System.Threading.Thread.Sleep(1);
                break;
            
            case 1: //Connect to speaker and start playing
                deviceNumber = (int)msg.data;
                Console.WriteLine("Switching to new speaker: " + Menu.outputAudioDevices[deviceNumber].FriendlyName);
                //Go through all the threads and update the new device
                playersMut.WaitOne();
                foreach (KeyValuePair<ulong, PlayerAudioOut> entry in players)
                {
                    entry.Value.sendMessage(1, deviceNumber);
                }
                playersMut.ReleaseMutex();
                break;
            
            case 2: //Closing
                break;
            
            case 3: //New data coming in
                AudioPacket packet = (AudioPacket)msg.data;
                //Console.WriteLine("New packet coming in");
                //Is this a disconnect?
                if (packet.samples == null && packet.encodedBytes == null)
                {
                    playersMut.WaitOne();
                    
                    byte[] dc = BitConverter.GetBytes(packet.senderId);
                    //Console.WriteLine("{0} {1} {2} {3} {4} {5} {6} {7}", dc[0], dc[1], dc[2], dc[3], dc[4], dc[5], dc[6], dc[7]);
                    if (!players.ContainsKey(packet.senderId))
                    {
                        //a players we dont know about disconnected, i guess we dont care.
                        Console.WriteLine("Player unknown " + packet.senderId.ToString() + " disconnected");
                    }
                    else
                    {
                        //Close him
                        players[packet.senderId].sendMessage(2, null);
                        players.Remove(packet.senderId);
                        Console.WriteLine("Player " + packet.senderId.ToString() + " disconnected");
                    }
                    playersMut.ReleaseMutex();
                    break;
                }
                
                //Create new thread if connection is new
                playersMut.WaitOne();
                if (!players.ContainsKey(packet.senderId))
                {
                    PlayerAudioOut player = new PlayerAudioOut();
                    players.Add(packet.senderId, player);
                    player.sendMessage(1, deviceNumber);
                    player.sendMessage(3, packet);
                    
                    byte[] dc = BitConverter.GetBytes(packet.senderId);
                    //Console.WriteLine("New connection: {0} {1} {2} {3} {4} {5} {6} {7}", dc[0], dc[1], dc[2], dc[3], dc[4], dc[5], dc[6], dc[7]);
                    
                    new Thread(() => handleThread(player)).Start();
                }
                else
                {
                    //Add our new packet
                    players[packet.senderId].sendMessage(3, packet);
                }
                playersMut.ReleaseMutex();
                break;
                
            case 4: //Updates the audio level of all the players
                setEveryonesAudio((GameReader.GameState)msg.data);
                break;
             
            default:
                break;
        }
    }
    
    private static void setEveryonesAudio(GameReader.GameState gameState)
    {
        switch (gameState.state)
        {
            case GameReader.GameState.State.NOGAME:
            case GameReader.GameState.State.UNKNOWN:
            case GameReader.GameState.State.MENU:
                //make everyone at 100%
                playersMut.WaitOne();
                foreach (KeyValuePair<ulong, PlayerAudioOut> entry in players)
                {
                    entry.Value.sendMessage(4, 1.0f);
                }
                playersMut.ReleaseMutex();
                break;
                
            case GameReader.GameState.State.LOBBY:
            {
                GameReader.Player me = gameState.me;
                playersMut.WaitOne();
                
                //Make everyone whos not in the game have 100% audio
                //Make a map of everyone else
                Dictionary<byte, PlayerAudioOut> amongUsIdToAudioStream = new Dictionary<byte, PlayerAudioOut>();
                foreach (KeyValuePair<ulong, PlayerAudioOut> entry in players)
                {
                    if (entry.Value.amongUsId >= 10)
                    {
                        entry.Value.sendMessage(4, 1.0f);
                    }
                    else if (!amongUsIdToAudioStream.ContainsKey(entry.Value.amongUsId))
                    {
                        amongUsIdToAudioStream.Add(entry.Value.amongUsId, entry.Value);
                    }
                    else //There multiple people with the same among us id. idk what to do
                    {
                        entry.Value.sendMessage(4, 1.0f);
                        Console.WriteLine("Multiple people with amongUsId = {0}", entry.Value.amongUsId);
                    }
                }
                
                //Quiet down everyone else
                foreach (GameReader.Player player in gameState.players)
                {
                    if (player != me && amongUsIdToAudioStream.ContainsKey(player.id))
                    {
                        //calculate volume for this player
                        float xDiff = me.x - player.x;
                        float yDiff = me.y - player.y;
                        float dist = (float)Math.Sqrt(xDiff*xDiff + yDiff*yDiff);
                        
                        const float fullVolumeRadius = 0.0f;
                        const float endRadius = 2.0f;
                        
                        float volume = 1.0f;
                        if (dist < fullVolumeRadius)
                        {
                            volume = 1.0f;
                        }
                        else if (dist > endRadius)
                        {
                            volume = 0.0f;   
                        }
                        else
                        {
                            const float unitRatio = 1.0f/(endRadius - fullVolumeRadius); //1 unit of position equals how much volume decrease?
                            float newDist = dist - fullVolumeRadius;
                            volume = 1 - newDist*unitRatio;
                        }
                        
                        //Console.WriteLine("Player {0} is {1} away, set volume to {2}", player.id, dist, volume);
                        amongUsIdToAudioStream[player.id].sendMessage(4, volume);
                    }
                }
                
                playersMut.ReleaseMutex();
                break;
            }
                
            case GameReader.GameState.State.DISCUSSION:
            {
                GameReader.Player me = gameState.me;
                playersMut.WaitOne();
                
                //Make everyone whos not in the game have 100% audio
                //Make a map of everyone else
                Dictionary<byte, PlayerAudioOut> amongUsIdToAudioStream = new Dictionary<byte, PlayerAudioOut>();
                foreach (KeyValuePair<ulong, PlayerAudioOut> entry in players)
                {
                    if (entry.Value.amongUsId >= 10)
                    {
                        entry.Value.sendMessage(4, 1.0f);
                    }
                    else if (!amongUsIdToAudioStream.ContainsKey(entry.Value.amongUsId))
                    {
                        amongUsIdToAudioStream.Add(entry.Value.amongUsId, entry.Value);
                    }
                    else //There multiple people with the same among us id. idk what to do
                    {
                        entry.Value.sendMessage(4, 1.0f);
                        Console.WriteLine("Multiple people with amongUsId = {0}", entry.Value.amongUsId);
                    }
                }
                
                //Quiet down everyone else
                foreach (GameReader.Player player in gameState.players)
                {
                    if (player != me && amongUsIdToAudioStream.ContainsKey(player.id))
                    {
                        if (!player.isDead) //only hear alive players during the discussion
                        {
                            amongUsIdToAudioStream[player.id].sendMessage(4, 1.0f);
                        }
                        else
                        {
                            amongUsIdToAudioStream[player.id].sendMessage(4, 0.0f);
                        }
                    }
                }
                
                playersMut.ReleaseMutex();
                break;
            }
                
            case GameReader.GameState.State.TASKS:
            {
                GameReader.Player me = gameState.me;
                playersMut.WaitOne();
                
                //Make everyone whos not in the game have 100% audio
                //Make a map of everyone else
                Dictionary<byte, PlayerAudioOut> amongUsIdToAudioStream = new Dictionary<byte, PlayerAudioOut>();
                foreach (KeyValuePair<ulong, PlayerAudioOut> entry in players)
                {
                    if (entry.Value.amongUsId >= 10)
                    {
                        entry.Value.sendMessage(4, 1.0f);
                    }
                    else if (!amongUsIdToAudioStream.ContainsKey(entry.Value.amongUsId))
                    {
                        amongUsIdToAudioStream.Add(entry.Value.amongUsId, entry.Value);
                    }
                    else //There multiple people with the same among us id. idk what to do
                    {
                        entry.Value.sendMessage(4, 1.0f);
                        Console.WriteLine("Multiple people with amongUsId = {0}", entry.Value.amongUsId);
                    }
                }
                
                //Quiet down everyone else
                foreach (GameReader.Player player in gameState.players)
                {
                    if (player != me && amongUsIdToAudioStream.ContainsKey(player.id))
                    {
                        //calculate volume for this player
                        float xDiff = me.x - player.x;
                        float yDiff = me.y - player.y;
                        float dist = (float)Math.Sqrt(xDiff*xDiff + yDiff*yDiff);
                        
                        const float fullVolumeRadius = 0.0f;
                        const float endRadius = 2.0f;
                        
                        float volume = 1.0f;
                        if (dist < fullVolumeRadius)
                        {
                            volume = 1.0f;
                        }
                        else if (dist > endRadius)
                        {
                            volume = 0.0f;   
                        }
                        else
                        {
                            const float unitRatio = 1.0f/(endRadius - fullVolumeRadius); //1 unit of position equals how much volume decrease?
                            float newDist = dist - fullVolumeRadius;
                            volume = 1 - newDist*unitRatio;
                        }
                        
                        //if you are alive, only hear other alive players
                        if (!me.isDead)
                        {
                            if (!player.isDead) //hear other alive players
                            {
                                amongUsIdToAudioStream[player.id].sendMessage(4, volume);
                            }
                            else //dont hear dead players if ur alive
                            {
                                amongUsIdToAudioStream[player.id].sendMessage(4, 0.0f);
                            }
                        }
                        else
                        {
                            //if you are dead, hear everyone
                            amongUsIdToAudioStream[player.id].sendMessage(4, volume);
                        }
                    }
                }
                
                playersMut.ReleaseMutex();
                break;
            }
                
            default:
                break;
        }
    }
    
    public static void handleThread(PlayerAudioOut player)
    {
        player.step();
    }
}
}
