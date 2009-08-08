/*
 * Copyright (c) 2009 Contributors.
 * All rights reserved.
 * This file is largely based on OMV's GridProxyLoader, with a few modifications 
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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Reflection;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenMetaverse.Packets;
using GridProxy;


namespace Grider
{
    public class GriderProxyFrame
    {
        public GriderProxy proxy;
        private Dictionary<string, CommandDelegate> commandDelegates = new Dictionary<string, CommandDelegate>();
        private UUID agentID;
        private UUID sessionID;
        private UUID inventoryRoot;
        private bool logLogin = false;
        private string[] args;

        public delegate void CommandDelegate(string[] words);

        public string[] Args
        {
            get { return args; }
        }

        public UUID AgentID
        {
            get { return agentID; }
        }

        public UUID SessionID
        {
            get { return sessionID; }
        }

        public UUID InventoryRoot
        {
            get { return inventoryRoot; }
        }

        public void AddCommand(string cmd, CommandDelegate deleg)
        {
            commandDelegates[cmd] = deleg;
        }

        public GriderProxyFrame(string[] args)
        {
            //bool externalPlugin = false;
            this.args = args;

            GriderProxyConfig proxyConfig = new GriderProxyConfig("GridSurferProxy", "Crista Lopes / Diva", args);
            proxy = new GriderProxy(proxyConfig);

            // add delegates for login
            proxy.SetLoginRequestDelegate(new XmlRpcRequestDelegate(LoginRequest));
            proxy.SetLoginResponseDelegate(new XmlRpcResponseDelegate(LoginResponse));

            // add a delegate for outgoing chat
            proxy.AddDelegate(PacketType.ChatFromViewer, Direction.Outgoing, new PacketDelegate(ChatFromViewerOut));

            //  handle command line arguments
            foreach (string arg in args)
                if (arg == "--log-login")
                    logLogin = true;
                else if (arg.Substring(0, 2) == "--")
                {
                    int ipos = arg.IndexOf("=");
                    if (ipos != -1)
                    {
                        string sw = arg.Substring(0, ipos);
                        string val = arg.Substring(ipos + 1);
                        Console.WriteLine("arg '" + sw + "' val '" + val + "'");
                        if (sw == "--load")
                        {
                            //externalPlugin = true;
                            LoadPlugin(val);
                        }
                    }
                }

            commandDelegates["/load"] = new CommandDelegate(CmdLoad);
       }

        private void CmdLoad(string[] words)
        {
            if (words.Length != 2)
                SayToUser("Usage: /load <plugin name>");
            else
            {
                try
                {
                    LoadPlugin(words[1]);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
        }

        public void LoadPlugin(string name)
        {

            Assembly assembly = Assembly.LoadFile(Path.GetFullPath(name));
            foreach (Type t in assembly.GetTypes())
            {
                try
                {
                    if (t.IsSubclassOf(typeof(GriderProxyPlugin)))
                    {
                        ConstructorInfo info = t.GetConstructor(new Type[] { typeof(GriderProxyFrame) });
                        GriderProxyPlugin plugin = (GriderProxyPlugin)info.Invoke(new object[] { this });
                        plugin.Init();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }

        }

        // LoginRequest: dump a login request to the console
        private void LoginRequest(XmlRpcRequest request)
        {
            if (logLogin)
            {
                Console.WriteLine("==> Login Request");
                Console.WriteLine(request);
            }
        }

        // Loginresponse: dump a login response to the console
        private void LoginResponse(XmlRpcResponse response)
        {
            System.Collections.Hashtable values = (System.Collections.Hashtable)response.Value;
            if (values.Contains("agent_id"))
                agentID = new UUID((string)values["agent_id"]);
            if (values.Contains("session_id"))
                sessionID = new UUID((string)values["session_id"]);
            if (values.Contains("inventory-root")) 
            {
                inventoryRoot = new UUID(
                    (string)((System.Collections.Hashtable)(((System.Collections.ArrayList)values["inventory-root"])[0]))["folder_id"]
                    );
                Console.WriteLine("inventory root: " + inventoryRoot);
            }

            if (logLogin)
            {
                Console.WriteLine("<== Login Response");
                Console.WriteLine(response);
            }
        }

        // ChatFromViewerOut: outgoing ChatFromViewer delegate; check for Analyst commands
        private Packet ChatFromViewerOut(Packet packet, IPEndPoint sim)
        {
            // deconstruct the packet
            ChatFromViewerPacket cpacket = (ChatFromViewerPacket)packet;
            string message = System.Text.Encoding.UTF8.GetString(cpacket.ChatData.Message).Replace("\0", "");

            if (message.Length > 1 && message[0] == '/')
            {
                string[] words = message.Split(' ');
                if (commandDelegates.ContainsKey(words[0]))
                {
                    // this is an Analyst command; act on it and drop the chat packet
                    ((CommandDelegate)commandDelegates[words[0]])(words);
                    return null;
                }
            }

            return packet;
        }

        // SayToUser: send a message to the user as in-world chat
        public void SayToUser(string message)
        {
            ChatFromSimulatorPacket packet = new ChatFromSimulatorPacket();
            packet.ChatData.FromName = Utils.StringToBytes("GridProxy");
            packet.ChatData.SourceID = UUID.Random();
            packet.ChatData.OwnerID = agentID;
            packet.ChatData.SourceType = (byte)2;
            packet.ChatData.ChatType = (byte)1;
            packet.ChatData.Audible = (byte)1;
            packet.ChatData.Position = new Vector3(0, 0, 0);
            packet.ChatData.Message = Utils.StringToBytes(message);
            proxy.InjectPacket(packet, Direction.Incoming);
        }

    }


    public abstract class GriderProxyPlugin : MarshalByRefObject
    {
        // public abstract ProxyPlugin(ProxyFrame main);
        public abstract void Init();
    }

}
