using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections;
using System.Threading;

using NAudio.Wave;

namespace CLC
{
public class MicIn
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
    
    private static WaveInEvent audioInput = null;
    
    private static byte currentAmongUsId = 69;
    
    private static short noiseGate = 327;
    private static float micAmplify = 1.0f;
    
    private static long lastLoudAudioSampleTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
    
    public static void init()
    {
        sendMessage(1, 0);
        
        //Thread to handle the window and not block
        new Thread(() =>
        {
            while (CrewLinkClone.loop)
            {
                step();
                System.Threading.Thread.Sleep(1);
            }
            
            if (audioInput != null)
            {
                audioInput.StopRecording();
                audioInput.Dispose();
                audioInput = null;
            }
            
            Console.WriteLine("Microphone input thread closing.");
            
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
        Message message = messages.Dequeue();
        mut.ReleaseMutex();
        return message;
    }
    
    public static void step()
    {
        Message msg = getMessage();
        switch (msg.id)
        {
            case 0: //No messages
                break;
            
            case 1: //Connect to mic and start recording
                if (audioInput != null)
                {
                    audioInput.StopRecording();
                    audioInput.Dispose();
                    audioInput = null;
                }
                audioInput = new WaveInEvent();
                audioInput.BufferMilliseconds  = 10; //This is extremely important to keep at 10, everything is built around there being 960 bytes per callback
                audioInput.WaveFormat = new WaveFormat(48000, 16, 1); //hz, bits per sample, channels
                audioInput.DataAvailable += new EventHandler<WaveInEventArgs>(audioInputNewDataCallback);
                audioInput.RecordingStopped += new EventHandler<StoppedEventArgs>(audioInputRcordingStoppedCallback);
                audioInput.DeviceNumber = (int)msg.data;
                Console.WriteLine("Switching to new mic: " + WaveIn.GetCapabilities(audioInput.DeviceNumber).ProductName);
                audioInput.StartRecording();
                break;
            
            case 2:
                //audioInput.StopRecording();
                break;
            
            case 3: //Get new local among us id
                GameReader.GameState gameState = (GameReader.GameState)msg.data;
                if (gameState.me != null)
                {
                    currentAmongUsId = gameState.me.id;
                }
                else
                {
                    currentAmongUsId = 69;
                }
                break;
                
            case 4: //Get new noise gate threshold
                noiseGate = (short)msg.data;
                break;
                
            case 5: //Get new amplify
                micAmplify = (float)msg.data;
                break;
             
            default:
                break;
        }
    }
    
    private static void audioInputRcordingStoppedCallback(object sender, StoppedEventArgs e)
    {
        Exception exc = e.Exception;
        Console.WriteLine("Audio Input device stopped recording!");
        if (exc != null)
        {
            Console.WriteLine("Reason: " + exc.ToString());
        }
    }
    
    private static void audioInputNewDataCallback(object sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded != 960)
        {
            Console.WriteLine("Warning: Microphone callback supplied different amount of bytes than expected: {0}", e.BytesRecorded.ToString());
            return;
        }
        
        short[] inputAudioSamples = new short[e.BytesRecorded];
        int maxVol = 0;
        long currentTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        
        for (int i = 0; i < e.BytesRecorded; i+=2)
        {
            short sample = BitConverter.ToInt16(e.Buffer, i);
            sample = (short)(sample*micAmplify);
            int absSample = Math.Abs((int)sample);
            
            if (absSample > (int)noiseGate)
            {
                lastLoudAudioSampleTimestamp = currentTimestamp;
            }
            
            if ((currentTimestamp - lastLoudAudioSampleTimestamp) > 500) //if its been 0.5 second since it was last loud, silence it
            {
                sample = 0;
                absSample = 0;
            }
            
            inputAudioSamples[i/2] = sample;
            
            if (absSample > maxVol)
            {
                maxVol = absSample;
            }
        }

        AudioPacket packet = new AudioPacket(0, currentAmongUsId, inputAudioSamples, true);
        //Console.WriteLine("Sending new mic packet out");
        Network.sendMessage(2, packet);
        UI.updatePlayerAudio(currentAmongUsId, (short)maxVol);
    }
}
}