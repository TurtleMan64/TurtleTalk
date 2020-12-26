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
public class PlayerAudioOut
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
    
    public byte amongUsId = 69;
    
    private float currentAmongUsVolume = 1.0f;
    
    private int audioOutBufferMin = 20; //num packets to prevent audio flickering
    private int audioOutBufferMax = 80; //num packets to prevent really big delay
    
    private OpusDecoder opusDecoder = new OpusDecoder(48000, 1);
    private List<AudioPacket> packets = new List<AudioPacket>();
    
    private WasapiOut wasapiOut = null;
    private BufferedWaveProvider waveProvider = null;
    
    private Mutex mut = new Mutex();
    private Queue<Message> messages = new Queue<Message>();
    
    public PlayerAudioOut()
    {
        audioOutBufferMin = Int32.Parse(Config.config["audioOutBufferMin"]);
        audioOutBufferMax = Int32.Parse(Config.config["audioOutBufferMax"]);
    }
    
    public void sendMessage(int message, Object data)
    {
        mut.WaitOne();
        messages.Enqueue(new Message(message, data));
        mut.ReleaseMutex();
    }
    
    private Message getMessage()
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
    
    public void step()
    {
        while (CrewLinkClone.loop)
        {
            updateBuffers();
            Message msg = getMessage();
            switch (msg.id)
            {
                case 0: //No messages
                    System.Threading.Thread.Sleep(1);
                    break;
                
                case 1: //Connect to speaker and start playing
                    if (wasapiOut != null)
                    {
                        wasapiOut.Stop();
                        wasapiOut.Dispose();
                        wasapiOut = null;
                    }
                    int deviceNum = (int)msg.data;
                    wasapiOut = new WasapiOut(Menu.outputAudioDevices[deviceNum], AudioClientShareMode.Shared, false, 40);
                    
                    waveProvider = new BufferedWaveProvider(new WaveFormat(48000, 16, 1));
                    waveProvider.DiscardOnBufferOverflow = true;

                    wasapiOut.Init(waveProvider);
                    Console.WriteLine("Switching to new speaker: " + Menu.outputAudioDevices[deviceNum].FriendlyName);
                    wasapiOut.Play();
                    break;
                
                case 2: //Shut me down
                    if (wasapiOut != null)
                    {
                        wasapiOut.Stop();
                        wasapiOut.Dispose();
                        wasapiOut = null;
                    }
                    Console.WriteLine("Player audio thread closing.");
                    return;
                
                case 3: //New data coming in
                    if (wasapiOut != null && waveProvider != null)
                    {
                        AudioPacket packet = (AudioPacket)msg.data;
                        packets.Add(packet);

                        amongUsId = packet.amongUsId;
                    }
                    break;
                    
                case 4: //Set volume
                    currentAmongUsVolume = (float)msg.data;
                    break;
                 
                default:
                    break;
            }
        }
        
        if (wasapiOut != null)
        {
            wasapiOut.Stop();
            wasapiOut.Dispose();
            wasapiOut = null;
        }
        
        Console.WriteLine("Player audio thread closing.");
    }
    
    private void updateBuffers()
    {
        if (wasapiOut != null && waveProvider != null)
        {
            //Trim old packets
            if (packets.Count > audioOutBufferMax)
            {
                Console.WriteLine("Erasing " + (packets.Count - audioOutBufferMax).ToString() + " old packets."); 
                packets.RemoveRange(0, packets.Count - audioOutBufferMax);
            }
            //Console.WriteLine("waveProvider.BufferedBytes = " + waveProvider.BufferedBytes.ToString()); 
            while (waveProvider.BufferedBytes <= 480*audioOutBufferMin && packets.Count > 0)
            {
                Console.WriteLine("waveProvider.BufferedBytes = " + waveProvider.BufferedBytes.ToString()); 
                
                byte[] bytes = packets[0].generateRawPcmBytes(opusDecoder, currentAmongUsVolume);
                waveProvider.AddSamples(bytes, 0, bytes.Length);
                packets.RemoveRange(0, 1);
            }
        }
    }
}
}
