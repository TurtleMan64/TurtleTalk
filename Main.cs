using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections;

using NAudio.Wave;

namespace CLC
{
public class CrewLinkClone
{
    public const int SW_HIDE = 0;

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    
    public static bool loop = true;
    public static bool gameReaderIsInError = true;
    
    public static void Main()
    {
        //var handle = GetConsoleWindow();
        //ShowWindow(handle, SW_HIDE);
        
        Config.init();
        UI.init();
        Menu.init();
        MicIn.init();
        AudioOut.init();
        Network.init();
        
        while (loop)
        {
            //Get new state of Among Us
            GameReader.GameState gameState = GameReader.getNewGameState();
            
            //Update the GUI to reflect new players
            UI.updateFromGameState(gameState);
            
            //Update network with new game state
            //Network.sendMessage(3, gameState);
            
            //Update MicIn with among us Id
            MicIn.sendMessage(3, gameState);
            
            //Update all of the players audio volumes
            AudioOut.sendMessage(4, gameState);
            
            //theDisplay.Refresh(); //i forgot why i needed this in past projects 
            System.Threading.Thread.Sleep(50);
        }
        
        Config.export();
        Console.WriteLine("Main thread closing.");
    }
}
}
