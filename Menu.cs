using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Collections.Generic;
using System.Collections;

using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace CLC
{
public class Menu
{
    public static List<MMDevice> outputAudioDevices = new List<MMDevice>();
    
    public static void init()
    {
        // Create an empty MainMenu.
        MainMenu mainMenu = new MainMenu();
        
        MenuItem[] subMenuMic = new MenuItem[WaveIn.DeviceCount];
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            
            subMenuMic[i] = new MenuItem((i+1).ToString() + ": " + caps.ProductName);
            subMenuMic[i].Click += eventMicChange;
        }
        
        MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
        foreach (MMDevice wasapiOutDevice in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            outputAudioDevices.Add(wasapiOutDevice);
        }
        
        MenuItem[] subMenuSpeaker = new MenuItem[outputAudioDevices.Count];
        for (int i = 0; i < outputAudioDevices.Count; i++)
        {
            subMenuSpeaker[i] = new MenuItem((i+1).ToString() + ": " + outputAudioDevices[i].FriendlyName);
            subMenuSpeaker[i].Click += eventSpeakerChange;
        }
        
        MenuItem menuItemMic     = new MenuItem("&Mic", subMenuMic);
        MenuItem menuItemSpeaker = new MenuItem("&Speaker", subMenuSpeaker);
        MenuItem menuItemConnect = new MenuItem("&Connect");
        MenuItem menuItemConfig  = new MenuItem("&Config");
        
        menuItemConnect.Click += eventConnectToServer;
        menuItemConfig.Click  += eventConfig;
        
        // Add four MenuItem objects to the MainMenu.
        mainMenu.MenuItems.Add(menuItemMic);
        mainMenu.MenuItems.Add(menuItemSpeaker);
        mainMenu.MenuItems.Add(menuItemConnect);
        mainMenu.MenuItems.Add(menuItemConfig);
        
        UI.theDisplay.Menu = mainMenu;
    }
    
    public static void eventMicChange(object sender, EventArgs e)
    {
        var clickedMenuItem = sender as MenuItem; 
        var menuText = clickedMenuItem.Text;
        
        int num = Int32.Parse(menuText.Substring(0, 1)) - 1;
        
        MicIn.sendMessage(1, num);
    }
    
    public static void eventSpeakerChange(object sender, EventArgs e)
    {
        var clickedMenuItem = sender as MenuItem; 
        var menuText = clickedMenuItem.Text;
        
        int num = Int32.Parse(menuText.Substring(0, 1)) - 1;
        
        AudioOut.sendMessage(1, num);
    }
    
    public static void eventConnectToServer(object sender, EventArgs e)
    {
        Form prompt = new Form()
        {
            Width = 250,
            Height = 150,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = "Enter Server IP",
            StartPosition = FormStartPosition.CenterScreen
        };
        Label textLabel = new Label() { Left = 20, Top=20, Text="Enter Server IP" };
        TextBox textBox = new TextBox() { Left = 20, Top=50, Width=200, Text=Config.config["server"] };
        Button confirmation = new Button() { Text = "Ok", Left=150, Width=70, Top=70, DialogResult = DialogResult.OK };
        confirmation.Click += (sender2, e2) => { prompt.Close(); };
        prompt.Controls.Add(textBox);
        prompt.Controls.Add(confirmation);
        prompt.Controls.Add(textLabel);
        prompt.AcceptButton = confirmation;
        
        if (prompt.ShowDialog() == DialogResult.OK)
        {
            Config.config["server"] = textBox.Text;
            Network.sendMessage(1, Config.config["server"]);
        }
    }
    
    public static void eventConfig(object sender, EventArgs e)
    {
        Form prompt = new Form()
        {
            Width = 250,
            Height = 350,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = "Mic Config",
            StartPosition = FormStartPosition.CenterScreen
        };
        
        Label textLabel1 = new Label() { Left = 20, Top=20, Text="Microphone noise gate (default = 0.01)", AutoSize=true};
        TextBox textBox1 = new TextBox() { Left = 20, Top=40, Width=200, Text=Config.config["micNoiseGate"] };
        prompt.Controls.Add(textLabel1);
        prompt.Controls.Add(textBox1);
        
        Label textLabel2 = new Label() { Left = 20, Top=80, Text="Microphone amplification (default = 1.0)", AutoSize=true};
        TextBox textBox2 = new TextBox() { Left = 20, Top=100, Width=200, Text=Config.config["micAmplification"] };
        prompt.Controls.Add(textLabel2);
        prompt.Controls.Add(textBox2);
        
        Button confirmation = new Button() { Text = "Ok", Left=150, Width=70, Top=130, DialogResult = DialogResult.OK };
        confirmation.Click += (sender2, e2) => { prompt.Close(); };
        
        prompt.Controls.Add(confirmation);
        prompt.AcceptButton = confirmation;
        
        if (prompt.ShowDialog() == DialogResult.OK)
        {
            try
            {
                Config.config["micNoiseGate"] = textBox1.Text;
                float val = float.Parse(Config.config["micNoiseGate"]);
                val = Math.Max(0.0f, Math.Min(val, 1.0f));
                short gate = (short)(32767*val);
                MicIn.sendMessage(4, gate);
            }
            catch (Exception exc)
            {
                Console.WriteLine("Can't parse new noise gate {0}", exc.ToString());
            }
            
            try
            {
                Config.config["micAmplification"] = textBox2.Text;
                float val = float.Parse(Config.config["micAmplification"]);
                val = Math.Max(0.0f, Math.Min(val, 10.0f));
                MicIn.sendMessage(5, val);
            }
            catch (Exception exc)
            {
                Console.WriteLine("Can't parse new amplification {0}", exc.ToString());
            }
        }
    }
}
}