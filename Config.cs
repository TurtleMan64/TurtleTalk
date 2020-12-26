using System;
using System.Collections.Generic;
using System.Collections;

namespace CLC
{
public class Config
{
    public static Dictionary<string, string> config = new Dictionary<string, string>();
    
    public static void init()
    {
        //put in default values
        config.Add("server", "127.0.0.1");
        config.Add("micAmplification", "1.0");
        config.Add("micNoiseGate", "0.01");
        config.Add("audioOutBufferMin", "20");
        config.Add("audioOutBufferMax", "80");
        
        //read in saved values
        string line;
        System.IO.StreamReader file = new System.IO.StreamReader("Config.ini");
        while ((line = file.ReadLine()) != null)
        {
            try
            {
                string[] data = line.Split(' ');
                config[data[0]] = data[1];
            }
            catch (Exception e)
            {
                Console.WriteLine("Warning: Trouble parsing line in config file: " + e.ToString());
            }
        }
        
        file.Close();
    }
    
    public static void export()
    {
        try
        {
            using (System.IO.StreamWriter file = new System.IO.StreamWriter("Config.ini"))
            {
                foreach (var entry in config)
                {
                    file.WriteLine("{0} {1}", entry.Key, entry.Value);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Warning: Trouble writing out config file: " + e.ToString());
        }
    }
}
}
