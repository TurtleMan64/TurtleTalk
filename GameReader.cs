using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.IO;

namespace CLC
{
public class GameReader
{
    private const int PROCESS_VM_READ = 0x0010;

    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern bool ReadProcessMemory(int hProcess, int lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);
    
    private static int processHandle = 0;
    
    private static bool expectReadToFail = false;
    
    private static int gameAssemblyBase   = 0x5F160000;
    private static int meetingHudBase     = 0x01C573A4;
    private static int gameStateBase      = 0x01C57F54;
    private static int allPlayersPtrBase  = 0x01C57BE8;
    private static int exiledPlayerIdBase = 0x01C573A4;
    
    private static GameState.State prevState = GameState.State.NOGAME;
    private static bool exileCausesEnd = false;
    
    public class GameState
    {
        public enum State
        {
            NOGAME,
            UNKNOWN,
            MENU,
            LOBBY,
            DISCUSSION,
            TASKS
        }
        public State state;
        public Player me;
        public List<Player> players;
        
        public GameState() 
        {
            state = State.NOGAME;
            me = null;
            players = new List<Player>();
        }

        public GameState(State state, Player me, List<Player> players)
        {
            this.state = state;
            this.me = me;
            this.players = players;
        }
    }

    public class Player
    {
        public byte id;
        public string name;
        public int color;
        public bool isDisconnected;
        public bool isImposter;
        public bool isDead;
        public float x;
        public float y;
        public bool inVent;
        public bool isLocal;
        
        public Player()
        {
            this.id = 0;
            this.name = "error";
            this.color = 0;
            this.isDisconnected = true;
            this.isImposter = false;
            this.isDead = false;
            this.x = 0.0f;
            this.y = 0.0f;
            this.inVent = false;
            this.isLocal = true;
        }
        
        public Player(byte id, string name, int color, bool isDisconnected, bool isImposter, bool isDead, float x, float y, bool inVent, bool isLocal)
        {
            this.id = Math.Max((byte)0, Math.Min(id, (byte)9));
            this.name = name;
            this.color = Math.Max(0, Math.Min(color, 11));
            this.isDisconnected = isDisconnected;
            this.isImposter = isImposter;
            this.isDead = isDead;
            this.x = x;
            this.y = y;
            this.inVent = inVent;
            this.isLocal = isLocal;
        }
    }
    
    public static GameState getNewGameState()
    {
        if (processHandle == 0)
        {
            attatchToGame();
        }
        
        if (processHandle != 0) //Still not attached to game
        {
            int meetingHud = readAddress(readAddress(readAddress(gameAssemblyBase + meetingHudBase) + 0x5C) + 0x0); if (processHandle == 0) { return new GameState(); }
            int meetingHudCachePtr = 0;
            if (meetingHud != 0)
            {
                meetingHudCachePtr = readAddress(meetingHud + 0x08); if (processHandle == 0) { return new GameState(); }
            }
            
            int meetingHudState = 4;
            if (meetingHudCachePtr != 0)
            {
                meetingHudState = readAddress(meetingHud + 0x84, 4); if (processHandle == 0) { return new GameState(); }
            }
            
            GameState.State state = GameState.State.UNKNOWN;
            int gameState = readAddress(readAddress(readAddress(readAddress(gameAssemblyBase + gameStateBase) + 0x5C) + 0x0) + 0x64); if (processHandle == 0) { return new GameState(); }
            
            switch (gameState)
            {
                case 0:
                    state = GameState.State.MENU;
                    exileCausesEnd = false;
                    break;
                    
                case 1:
                case 3:
                    state = GameState.State.LOBBY;
                    exileCausesEnd = false;
                    break;
                    
                default:
                    if (exileCausesEnd)
                    {
                        state = GameState.State.LOBBY;
                    }
                    else if (meetingHudState < 4)
                    {
                        state = GameState.State.DISCUSSION;
                    }
                    else
                    {
                        state = GameState.State.TASKS;
                    }
                    break;
            }
            
            if (state == GameState.State.UNKNOWN ||
                state == GameState.State.MENU)
            {
                return new GameState(state, null, new List<Player>());
            }
            
            int allPlayersPtr = readAddress(readAddress(readAddress(readAddress(gameAssemblyBase + allPlayersPtrBase) + 0x5C) + 0x00) + 0x24);
            int allPlayers = readAddress(allPlayersPtr + 0x08);
            int playerCount = readAddress(allPlayersPtr + 0x0C);
            int playerAddrPtr = allPlayers + 0x10;
            
            if (processHandle == 0) { return new GameState(); }
            
            Player me = null;
            List<Player> players = new List<Player>();
            int numImposters = 0;
            int numCrewmates = 0;
            expectReadToFail = true;
            //this isnt working currently
            int exiledPlayerId = readByte(readAddress(readAddress(readAddress(readAddress(readAddress(gameAssemblyBase + 0xFF) + exiledPlayerIdBase) + 0x5C) + 0x00) + 0x94) + 0x08);
            expectReadToFail = false;
            //Console.WriteLine("exiledPlayerId = {0}", exiledPlayerId);
            
            for (int i = 0; i < Math.Max(0, Math.Min(playerCount, 10)); i++)
            {
                int address = readAddress(playerAddrPtr);
                Player pla = readPlayer(address);
                
                if (processHandle == 0) { return new GameState(); }

                playerAddrPtr += 4;
                players.Add(pla);
                if (pla.isLocal)
                {
                    me = pla;
                }
                
                if (pla.id == exiledPlayerId || pla.isDead || pla.isDisconnected)
                {
                    continue;
                }
                
                if (pla.isImposter)
                {
                    numImposters++;
                }
                else
                {
                    numCrewmates++;
                }
            }
            
            if (prevState == GameState.State.DISCUSSION && state == GameState.State.TASKS)
            {
                if (numImposters == 0 || numImposters >= numCrewmates)
                {
					exileCausesEnd = true;
					state = GameState.State.LOBBY;
				}
            }
            
            prevState = state;
            
            return new GameState(state, me, players);
        }
        
        return new GameState();
    }
    
    private static void attatchToGame()
    {
        //CrewLinkClone.theDisplay.Text = "Searching for game...";
        processHandle = 0;
        
        try
        {
            Process[] processes = Process.GetProcessesByName("Among Us");
            
            processHandle = (int)OpenProcess(PROCESS_VM_READ, false, processes[0].Id);
            
            if (processHandle == 0)
            {
                CrewLinkClone.gameReaderIsInError = true;
                System.Threading.Thread.Sleep(500);
                return;
            }
            
            //CrewLinkClone.theDisplay.Text = "Among Us";
            
            List<MyModule> modules = CollectModules(processes[0]);
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i].ModuleName == "GameAssembly.dll")
                {
                    gameAssemblyBase = (int)modules[i].BaseAddress;
                    Console.WriteLine(modules[i].ModuleName);
                    Console.WriteLine(modules[i].BaseAddress);
                }
            }
            
            CrewLinkClone.gameReaderIsInError = false;
        }
        catch
        {
            processHandle = 0;
            CrewLinkClone.gameReaderIsInError = true;
            System.Threading.Thread.Sleep(500);
        }
    }
    
    private static int readAddress(int addressToRead)
    {
        int bytesRead = 0;
        byte[] buffer = new byte[4];
        if (ReadProcessMemory((int)processHandle, addressToRead, buffer, 4, ref bytesRead) == false || bytesRead != 4)
        {
            if (expectReadToFail)
            {
                return -1;
            }
            
            processHandle = 0;
            CrewLinkClone.gameReaderIsInError = true;
            return 0;
        }

        int result = 0;
        result+=buffer[0];
        result+=buffer[1]<<8;
        result+=buffer[2]<<16;
        result+=buffer[3]<<24;

        return result;
    }
    
    //Use this if it's expected to fail sometimes.
    private static int readAddress(int addressToRead, int defaultValue)
    {
        int bytesRead = 0;
        byte[] buffer = new byte[4];
        if (ReadProcessMemory((int)processHandle, addressToRead, buffer, 4, ref bytesRead) == false || bytesRead != 4)
        {
            //processHandle = 0;
            Console.WriteLine("failed");
            return defaultValue;
        }

        int result = 0;
        result+=buffer[0];
        result+=buffer[1]<<8;
        result+=buffer[2]<<16;
        result+=buffer[3]<<24;

        return result;
    }
    
    private static byte readByte(int addressToRead)
    {
        int bytesRead = 0;
        byte[] buffer = new byte[1];
        if (ReadProcessMemory((int)processHandle, addressToRead, buffer, 1, ref bytesRead) == false || bytesRead != 1)
        {
            if (expectReadToFail)
            {
                return (byte)255;
            }

            processHandle = 0;
            CrewLinkClone.gameReaderIsInError = true;
            return 0;
        }

        return buffer[0];
    }
    
    private static Player readPlayer(int addressToRead)
    {
        int bytesRead = 0;
        byte[] b = new byte[56];
        if (ReadProcessMemory((int)processHandle, addressToRead, b, 56, ref bytesRead) == false || bytesRead != 56)
        {
            processHandle = 0;
            CrewLinkClone.gameReaderIsInError = true;
            return new Player();
        }
        
        int namePtr = readAddress(addressToRead + 12);
        string name = readString(namePtr);
        
        int objPtr = readAddress(addressToRead + 44);
        int posPtr = readAddress(objPtr + 0x60);
        
        bool isDisconnected = (BitConverter.ToInt32(b, 32) != 0);
        bool isImposter     = (b[40] != 0);
        bool isDead         = (b[41] != 0); 
        bool inVent         = (readByte(objPtr + 0x31) != 0);
        bool isLocal        = (readAddress(objPtr + 0x54) != 0);
        
        float x;
        float y;
        
        if (!isLocal)
        {
            x = readFloat(posPtr + 0x3C);
            y = readFloat(posPtr + 0x40);
        }
        else
        {
            x = readFloat(posPtr + 0x50);
            y = readFloat(posPtr + 0x54);
        }
        
        return new Player(b[8], name, b[16], isDisconnected, isImposter, isDead, x, y, inVent, isLocal);
    }
    
    private static string readString(int addressToRead)
    {
        //every string has blanks inbetween every character
        int length = 2*readAddress(addressToRead + 0x8); //length of string at 0x8
        
        int bytesRead = 0;
        byte[] buffer = new byte[length];
        if (ReadProcessMemory((int)processHandle, addressToRead + 0xC, buffer, length, ref bytesRead) == false || bytesRead != length)
        {
            if (expectReadToFail)
            {
                return "error";
            }
            
            processHandle = 0;
            CrewLinkClone.gameReaderIsInError = true;
            return "";
        }
        
        byte[] b2 = new byte[length/2];
        for (int i = 0; i < length/2; i++)
        {
            b2[i] = buffer[i*2];
        }

        return System.Text.Encoding.UTF8.GetString(b2);
    }

	private static float readFloat(int addressToRead)
    {
		int bytesRead = 0;
        byte[] buffer = new byte[4];
        if (ReadProcessMemory((int)processHandle, addressToRead, buffer, 4, ref bytesRead) == false || bytesRead != 4)
        {
            if (expectReadToFail)
            {
                return 0.0f;
            }
            
            processHandle = 0;
            CrewLinkClone.gameReaderIsInError = true;
            return 0.0f;
        }
		
		return BitConverter.ToSingle(buffer, 0);
	}
    
    //https://stackoverflow.com/questions/36431220/getting-a-list-of-dlls-currently-loaded-in-a-process-c-sharp
    private static List<MyModule> CollectModules(Process process)
    {
        List<MyModule> collectedModules = new List<MyModule>();

        IntPtr[] modulePointers = new IntPtr[0];
        int bytesNeeded = 0;

        // Determine number of modules
        if (!Native.EnumProcessModulesEx(process.Handle, modulePointers, 0, out bytesNeeded, (uint)Native.ModuleFilter.ListModulesAll))
        {
            Console.WriteLine("Native.EnumProcessModulesEx failed");
            return collectedModules;
        }

        int totalNumberofModules = bytesNeeded / IntPtr.Size;
        modulePointers = new IntPtr[totalNumberofModules];

        // Collect modules from the process
        if (Native.EnumProcessModulesEx(process.Handle, modulePointers, bytesNeeded, out bytesNeeded, (uint)Native.ModuleFilter.ListModulesAll))
        {
            for (int index = 0; index < totalNumberofModules; index++)
            {
                StringBuilder moduleFilePath = new StringBuilder(1024);
                Native.GetModuleFileNameEx(process.Handle, modulePointers[index], moduleFilePath, (uint)(moduleFilePath.Capacity));

                string moduleName = Path.GetFileName(moduleFilePath.ToString());
                Native.ModuleInformation moduleInformation = new Native.ModuleInformation();
                Native.GetModuleInformation(process.Handle, modulePointers[index], out moduleInformation, (uint)(IntPtr.Size * (modulePointers.Length)));

                // Convert to a normalized module and add it to our list
                MyModule module = new MyModule(moduleName, moduleInformation.lpBaseOfDll, moduleInformation.SizeOfImage);
                collectedModules.Add(module);
            }
        }

        return collectedModules;
    }

    private class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ModuleInformation
        {
            public IntPtr lpBaseOfDll;
            public uint SizeOfImage;
            public IntPtr EntryPoint;
        }

        internal enum ModuleFilter
        {
            ListModulesDefault = 0x0,
            ListModules32Bit = 0x01,
            ListModules64Bit = 0x02,
            ListModulesAll = 0x03,
        }

        [DllImport("psapi.dll")]
        public static extern bool EnumProcessModulesEx(IntPtr hProcess, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U4)] [In][Out] IntPtr[] lphModule, int cb, [MarshalAs(UnmanagedType.U4)] out int lpcbNeeded, uint dwFilterFlag);

        [DllImport("psapi.dll")]
        public static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpBaseName, [In] [MarshalAs(UnmanagedType.U4)] uint nSize);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule, out ModuleInformation lpmodinfo, uint cb);
    }

    private class MyModule
    {
        public MyModule(string moduleName, IntPtr baseAddress, uint size)
        {
            this.ModuleName = moduleName;
            this.BaseAddress = baseAddress;
            this.Size = size;
        }

        public string ModuleName { get; set; }
        public IntPtr BaseAddress { get; set; }
        public uint Size { get; set; }
    }
}
}