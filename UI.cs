using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Collections.Generic;
using System.Collections;

namespace CLC
{
public class UI
{
    public static Form theDisplay;
    
    private class MyLabel : Label
    {
        public bool isPresent = false;
        public short currentVolume = -1;
        public short lastDrawnVolume = -1;
        
        public MyLabel()
        {
            
        }
        
        protected override void OnPaint(PaintEventArgs e)
        {
            if (isPresent)
            {
                if (currentVolume < 0) //Player is in among us, but we have no audio from them: show red outline
                {
                    ControlPaint.DrawBorder(e.Graphics, ClientRectangle,
                    Color.DarkRed, 3, ButtonBorderStyle.Solid,
                    Color.DarkRed, 3, ButtonBorderStyle.Solid,
                    Color.DarkRed, 3, ButtonBorderStyle.Solid,
                    Color.DarkRed, 3, ButtonBorderStyle.Solid);
                }
                else //Player is in among us and we have audio, set to green with intensity
                {
                    int start = 0;
                    if (currentVolume > 0)
                    {
                        start = 60;
                    }
                    int gVal = 30 + start + (int)(400*(currentVolume/32768.0));
                    gVal = Math.Min(gVal, 255);
                    Color borderCol = Color.FromArgb(255,  30, gVal,  30);
                    ControlPaint.DrawBorder(e.Graphics, ClientRectangle, 
                    borderCol, 3, ButtonBorderStyle.Solid,
                    borderCol, 3, ButtonBorderStyle.Solid,
                    borderCol, 3, ButtonBorderStyle.Solid,
                    borderCol, 3, ButtonBorderStyle.Solid);
                    currentVolume = -1;
                }
            }
            lastDrawnVolume = currentVolume;
            base.OnPaint(e);
        }
    }
    
    private static Color dark = Color.FromArgb(30, 30, 30);
    private static Color[] colors = new Color[12];
    
    private static MyLabel[] labels = new MyLabel[10];
    private static Mutex labelsMutex = new Mutex();
    
    public static void init()
    {
        theDisplay = new Form();
        theDisplay.ClientSize = new Size(208, 348);
        theDisplay.MinimizeBox = false;
        theDisplay.MaximizeBox = false;
        theDisplay.FormBorderStyle = FormBorderStyle.FixedSingle; 
        theDisplay.StartPosition = FormStartPosition.CenterScreen;
        theDisplay.BackColor = Color.FromArgb(20, 20, 20);
        theDisplay.Text = "TurtleTalk v0.1";
        
        try
        {
            theDisplay.Icon = new Icon(IconSource.getIconStream());
        }
        catch
        {
            
        }
        
        colors[ 0] = Color.FromArgb(255, 198,  17,  17);
        colors[ 1] = Color.FromArgb(255,  19,  46, 210);
        colors[ 2] = Color.FromArgb(255,  17, 128,  45);
        colors[ 3] = Color.FromArgb(255, 238,  84, 187);
        colors[ 4] = Color.FromArgb(255, 240, 125,  13);
        colors[ 5] = Color.FromArgb(255, 246, 246,  87);
        colors[ 6] = Color.FromArgb(255,  63,  71,  78);
        colors[ 7] = Color.FromArgb(255, 215, 225, 241);
        colors[ 8] = Color.FromArgb(255, 107,  47, 188);
        colors[ 9] = Color.FromArgb(255, 113,  73,  30);
        colors[10] = Color.FromArgb(255,  56, 255, 221);
        colors[11] = Color.FromArgb(255,  80, 240,  57);
        
        for (int i = 0; i < 10; i++)
        {
            MyLabel label = new MyLabel();
            label.Location = new Point(8, 32*i + 8);
            label.Size = new Size(192, 24);
            label.Font = new Font("Calibri", 18);
            label.BackColor = colors[i];
            //label.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Text = "Player " + (i+1).ToString();
            labels[i] = label;
            theDisplay.Controls.Add(label);
        }
        
        //Thread to handle the window and not block
        new Thread(() =>
        {
            Thread.CurrentThread.IsBackground = true;
            theDisplay.ShowDialog();
            
            MicIn.sendMessage(2, null);
            AudioOut.sendMessage(2, null);
            
            CrewLinkClone.loop = false;
            
            Console.WriteLine("UI thread closing.");
            
        }).Start();
    }
    
    public static void updateFromGameState(GameReader.GameState gameState)
    {
        labelsMutex.WaitOne();
        
        //theDisplay.Text = gameState.state.ToString();
        
        HashSet<int> presentIds = new HashSet<int>();
        for (int i = 0; i < gameState.players.Count; i++)
        {
            GameReader.Player pla = gameState.players[i];
            presentIds.Add(pla.id);
            labels[pla.id].isPresent = true;
            //if (pla == gameState.me)
            {
                //labels[pla.id].isPresent = false;
            }
            labels[pla.id].Text = pla.name;
            labels[pla.id].BackColor = colors[pla.color];
            //if (labels[pla.id].lastDrawnVolume != labels[pla.id].currentVolume)
            {
                labels[pla.id].Refresh();
                //labels[pla.id].currentVolume = -1;
            }
            //labels[pla.id].currentVolume = -1;
        }
        
        for (int id = 0; id < 10; id++)
        {
            if (!presentIds.Contains(id))
            {
                labels[id].isPresent = false;
                labels[id].Text = "";
                labels[id].BackColor = dark;
                //labels[id].currentVolume = -1;
            }
        }
        
        //for (int i = 0; i < 10; i++)
        {
            //if (volumes[i] < 0) //this means no data has come in for this player = color it red
            {
                //
            }
            //else
            {
                
                
                //volumes[i] = -1;
            }
        }
        labelsMutex.ReleaseMutex();
        
        //if (gameState.state == GameReader.GameState.State.NOGAME)
        {
            //theDisplay.Text = "Searching for game...";
        }
        //else
        {
            //theDisplay.Text = "Among Us";
        }
    }
    
    public static void updatePlayerAudio(byte amongUsId, short volume)
    {
        labelsMutex.WaitOne();
        if (amongUsId < 10)
        {
            labels[amongUsId].currentVolume = volume;
        }
        labelsMutex.ReleaseMutex();
    }
}
}