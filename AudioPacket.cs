using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections;
using System.Threading;

using NAudio.Wave;

using Concentus.Enums;
using Concentus.Structs;

namespace CLC
{
    public class AudioPacket
    {
        private static OpusEncoder opusEncoder = new OpusEncoder(48000, 1, OpusApplication.OPUS_APPLICATION_AUDIO); //OPUS_APPLICATION_RESTRICTED_LOWDELAY maybe for less delay
        
        public ulong senderId; //id is ip + port
        public byte amongUsId = 69; //69 meaning not connected to among us, or unknown. normal range is [0, 9]
        public short[] samples; //480 samples, at 48khz
        public byte[] encodedBytes; //only god knows
        
        public AudioPacket(ulong senderId, byte amongUsId, byte[] encodedBytes)
        {
            this.senderId = senderId;
            this.amongUsId = amongUsId;
            this.encodedBytes = encodedBytes;
            
            opusEncoder.Bitrate = 24000;
        }
        
        public AudioPacket(ulong senderId, byte amongUsId, short[] samples, bool idk)
        {
            this.senderId = senderId;
            this.amongUsId = amongUsId;
            this.samples = samples;
            
            opusEncoder.Bitrate = 24000;
        }
        
        public byte[] generateRawPcmBytes(OpusDecoder opusDecoder, float volume)
        {
            if (volume <= 0)
            {
                UI.updatePlayerAudio(amongUsId, 0);
                return new byte[960];
            }
            
            short[] decodedSamples = new short[480];
            int numBytesDecoded = opusDecoder.Decode(encodedBytes, 0, encodedBytes.Length, decodedSamples, 0, 480, false);
            samples = decodedSamples;
            
            int maxVol = 0;
            byte[] pcm = new byte[960];
            for (int i = 0; i < 480; i++)
            {
                int sample = (int)decodedSamples[i];
                sample = (int)(sample*volume);
                sample = Math.Max(-32767, Math.Min(sample, 32767));
                
                int volAbs = Math.Abs(sample);
                if (volAbs > maxVol)
                {
                    maxVol = volAbs;
                }
                
                byte[] b = BitConverter.GetBytes((short)sample);
                pcm[i*2 + 0] = b[0];
                pcm[i*2 + 1] = b[1];
            }
            
            UI.updatePlayerAudio(amongUsId, (short)maxVol);
            
            return pcm;
        }
        
        public byte[] generateNetworkStreamBytes()
        {
            byte[] buf = new byte[960];
            int packetSize = opusEncoder.Encode(samples, 0, 480, buf, 0, 960);
            byte[] encodedAudio = new byte[packetSize + 12];
            for (int i = 0; i < packetSize; i++)
            {
                encodedAudio[i + 12] = buf[i];
            }
            
            //Console.WriteLine("Encoded to " + packetSize.ToString() + " bytes.");
            
            byte[] netId = BitConverter.GetBytes(Network.networkId);
            encodedAudio[0] = netId[0];
            encodedAudio[1] = netId[1];
            encodedAudio[2] = netId[2];
            encodedAudio[3] = netId[3];
            encodedAudio[4] = netId[4];
            encodedAudio[5] = netId[5];
            
            encodedAudio[6] = amongUsId;
            encodedAudio[7] = 0;
            
            byte[] lenBytes = BitConverter.GetBytes(packetSize);
            encodedAudio[ 8] = lenBytes[0];
            encodedAudio[ 9] = lenBytes[1];
            encodedAudio[10] = lenBytes[2];
            encodedAudio[11] = lenBytes[3];
            
            return encodedAudio;
        }
        
        //Tries its best. Fails if its feeling a bit sad. Removes bytes from networkBytes that it consumes.
        public static AudioPacket constructFromNetworkStreamBytes(List<byte> networkBytes)
        {
            //Do we have enough for the metadata?
            if (networkBytes.Count < 12)
            {
                return null;
            }
            
            byte[] meta = new byte[12];
            
            //First 6 bytes = senderId
            for (int i = 0; i < 6; i++)
            {
                meta[i] = networkBytes[i];
            }
            ulong senderId = BitConverter.ToUInt64(meta, 0);
            
            
            //Rest of the bytes = amongUsId and encoded bytes length
            for (int i = 6; i < 12; i++)
            {
                meta[i] = networkBytes[i];
            }
            byte amongUsId = meta[6];
            
            //Do we have enough for the encoded samples?
            int encodedLen = BitConverter.ToInt32(meta, 8);
            if (networkBytes.Count < encodedLen + 12)
            {
                return null;
            }
            
            if (encodedLen == 0) //this means that it was disconnected
            {
                networkBytes.RemoveRange(0, 12);
                return new AudioPacket(senderId, amongUsId, null);
            }
            
            //Console.WriteLine("About to decode " + encodedLen.ToString() + " bytes from network stream of size " + (networkBytes.Count - 12).ToString());
            
            byte[] endodedBytes = new byte[encodedLen];
            for (int i = 0; i < encodedLen; i++)
            {
                endodedBytes[i] = networkBytes[12 + i];
            }
            
            //Remove what weve used from the stream.
            networkBytes.RemoveRange(0, encodedLen + 12);
            
            return new AudioPacket(senderId, amongUsId, endodedBytes);
        }
    }
}
