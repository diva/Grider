/*
 * GriderApp.cs: The Main for Grider, the secure Hypergrid client
 *
 * Copyright (c) 2009 Contributors.
 * All rights reserved.
 *
 * - Redistribution and use in source and binary forms, with or without 
 *   modification, are permitted provided that the following conditions are met:
 *
 * - Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 * - Neither the name of the project Grider nor the names 
 *   of its contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 * 
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" 
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF 
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
 * POSSIBILITY OF SUCH DAMAGE.
 */
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using GridProxy;

namespace Grider
{
    public class GriderApp
    {
        static string viewerPath = "C:\\Program Files\\SecondLife\\SecondLife.exe";
        static bool preferences = false;

        static void Main(string[] args)
        {
            ReadPreferences();

            Console.WriteLine("-----------------------------------------------");
            Console.WriteLine("  Grider -- the client for the safe Hypergrid");
            Console.WriteLine("-----------------------------------------------");
            Console.WriteLine("Default viewer is " + viewerPath);
            if (!preferences)
            {
                Console.WriteLine("Enter a new viewer path or hit <enter>");
                Console.Write(">> ");
                string input = Console.ReadLine();
                if (input.Length > 10)
                    viewerPath = input;
            }

            if (!File.Exists(viewerPath))
            {
                Console.WriteLine("The provided path is not valid.");
                return;
            }

            WritePreferences();

            Console.WriteLine("Launching viewer...");
            Process viewer = Process.Start(viewerPath, "-loginuri http://localhost:8080 -multiple");

            GriderProxyFrame p = new GriderProxyFrame(args);
            GriderProxyPlugin gridSurfer = new Grider(p, viewer);
            gridSurfer.Init();
            p.proxy.Start();
        }

        static void ReadPreferences()
        {
            try
            {
                using (StreamReader sr = new StreamReader("Grider.ini"))
                {
                    preferences = true;
                    string line = string.Empty;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.StartsWith(";"))
                            continue;
                        string[] parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            string name = parts[0].Trim();
                            string value = parts[1].Trim();
                            switch (name)
                            {
                                case "viewer":
                                    viewerPath = value;
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        static void WritePreferences()
        {
            try
            {
                using (StreamWriter sw = new StreamWriter("Grider.ini"))
                {
                    sw.WriteLine("viewer=" + viewerPath);
                }
            }
            catch
            {
            }

        }

    }
}
