/*
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading;

using OpenSimLibComms;
using OpenSim.Framework;

using Nwc.XmlRpc;
using OpenMetaverse;
using OpenMetaverse.Packets;
using OpenMetaverse.StructuredData;
using OpenMetaverse.Http;
using GridProxy;


namespace Grider
{
    public class Grider : GriderProxyPlugin
    {
        private GriderProxyFrame frame;
        private GriderProxy proxy;
        private Hashtable loggedPackets = new Hashtable();
        //private Dictionary<PacketType, Dictionary<BlockField, object>> modifiedPackets = new Dictionary<PacketType, Dictionary<BlockField, object>>();
        private Assembly openmvAssembly;
        //private StreamWriter output;

        private Process Viewer;

        private Agent MainAgent;
        private UserAuthClient UserAuth;
        private InventoryClient UserInventory;

        public Grider(GriderProxyFrame frame, Process viewer)
        {
            this.frame = frame;
            this.proxy = frame.proxy;
            this.Viewer = viewer;
        }

        ~Grider()
        {
            //if (output != null)
            //    output.Close();
        }

        public override void Init()
        {
            openmvAssembly = Assembly.Load("OpenMetaverse");
            if (openmvAssembly == null) throw new Exception("Assembly load exception");

            // build the table of /command delegates
            InitializeDelegates();

            ////  handle command line arguments
            //foreach (string arg in frame.Args)
            //    if (arg == "--log-all")
            //        LogAll();
            //    else if (arg.Contains("--log-whitelist="))
            //        LogWhitelist(arg.Substring(arg.IndexOf('=') + 1));
            //    else if (arg.Contains("--output="))
            //        SetOutput(arg.Substring(arg.IndexOf('=') + 1));

            Console.WriteLine("Grider loaded");
        }

        private void InitializeDelegates()
        {
            proxy.SetLoginRequestDelegate(new XmlRpcRequestDelegate(LoginRequestHandle));
            proxy.SetLoginResponseDelegate(new XmlRpcResponseDelegate(LoginResponseHandle));
            proxy.AddDelegate(PacketType.LogoutRequest, Direction.Outgoing, new PacketDelegate(LogoutHandler));

            proxy.AddCapsDelegate("SeedCapability", new CapsDelegate(SeedCapHandler));
            proxy.AddCapsDelegate("EventQueueGet", new CapsDelegate(EventQueueGetHandler));
            //proxy.AddCapsDelegate("UpdateNotecardAgentInventory", new CapsDelegate(UpdateNotecardHandler));

            proxy.AddDelegate(PacketType.TeleportLocationRequest, Direction.Outgoing, new PacketDelegate(TeleportHandler));
            proxy.AddDelegate(PacketType.CompleteAgentMovement, Direction.Outgoing, new PacketDelegate(CompleteMovementHandler));
            proxy.AddDelegate(PacketType.AgentMovementComplete, Direction.Incoming, new PacketDelegate(MovementCompleteHandler));

            proxy.AddDelegate(PacketType.FetchInventoryDescendents, Direction.Outgoing, new PacketDelegate(FetchInventoryFolderHandler));
            proxy.AddDelegate(PacketType.FetchInventory, Direction.Outgoing, new PacketDelegate(GetInventoryHandler));
            proxy.AddDelegate(PacketType.InventoryDescendents, Direction.Incoming, new PacketDelegate(InventoryDescendantsReply));
            proxy.AddDelegate(PacketType.CreateInventoryFolder, Direction.Outgoing, new PacketDelegate(CreateInventoryFolderHandler));
            proxy.AddDelegate(PacketType.UpdateInventoryFolder, Direction.Outgoing, new PacketDelegate(UpdateInventoryFolderHandler));
            proxy.AddDelegate(PacketType.MoveInventoryFolder, Direction.Outgoing, new PacketDelegate(MoveInventoryFolderHandler));
            proxy.AddDelegate(PacketType.CreateInventoryItem, Direction.Outgoing, new PacketDelegate(CreateInventoryItemHandler));
            proxy.AddDelegate(PacketType.PurgeInventoryDescendents, Direction.Outgoing, new PacketDelegate(PurgeFolderHandler));
            proxy.AddDelegate(PacketType.UpdateInventoryItem, Direction.Outgoing, new PacketDelegate(UpdateInventoryItemHandler));
            proxy.AddDelegate(PacketType.CopyInventoryItem, Direction.Outgoing, new PacketDelegate(CopyInventoryItemHandler));
            proxy.AddDelegate(PacketType.MoveInventoryItem, Direction.Outgoing, new PacketDelegate(MoveInventoryItemHandler));
            proxy.AddDelegate(PacketType.RemoveInventoryItem, Direction.Outgoing, new PacketDelegate(RemoveInventoryItemHandler));
            proxy.AddDelegate(PacketType.RemoveInventoryFolder, Direction.Outgoing, new PacketDelegate(RemoveInventoryFolderHandler));

            proxy.AddDelegate(PacketType.AgentWearablesRequest, Direction.Outgoing, new PacketDelegate(WearablesRequestHandler));

            proxy.AddDelegate(PacketType.RequestImage, Direction.Outgoing, new PacketDelegate(ImageRequestHandler));
            proxy.AddDelegate(PacketType.TransferRequest, Direction.Outgoing, new PacketDelegate(AssetTransferRequestHandler));
            proxy.AddDelegate(PacketType.AssetUploadRequest, Direction.Outgoing, new PacketDelegate(AssetUploadRequestHandler));
            proxy.AddDelegate(PacketType.SendXferPacket, Direction.Outgoing, new PacketDelegate(SendXferHandler));
        }


        #region Login and Logout

        string firstName, lastName;

        void LoginRequestHandle(XmlRpcRequest request)
        {
            Console.WriteLine(">> LoginRequest to " + request.MethodName);
            request.MethodName = "hg_login";
            Hashtable hash = (Hashtable)request.Params[0];
            string first = firstName = (string)hash["first"];
            string last = lastName = (string)hash["last"];
            if (string.Empty.Equals(first) || string.Empty.Equals(last))
                return;

            if (first.Contains("."))
            {
                TransformName(ref first, ref last, ref proxy.remoteLoginURI);
                hash["first"] = first;
                hash["last"] = last;

                Console.WriteLine("[Grider]: Login request for " + first + " " + last + ". Login URI: " + proxy.remoteLoginURI);
            }
        }

        void LoginResponseHandle(XmlRpcResponse response)
        {
            Console.WriteLine(">> LoginResponse ");
            Hashtable hash = (Hashtable)response.Value;
            //Console.WriteLine(" reply length: " + hash.Count + "; IsError? " + response.IsFault);
            if (hash.Count <= 5)
            {
                Console.WriteLine("[Grider]: Login failed");
                return;
            }

            // successful login

            string key = string.Empty;
            if (hash.Contains("seed_capability"))
            {
                key = (string)hash["seed_capability"];
                key = key.Replace(proxy.loginURI, "");
                CapInfo info = null;
                proxy.KnownCaps.TryGetValue(key, out info);
                if (info == null)
                {
                    Console.WriteLine("SeedCap info not found");
                    info = new CapInfo(key, proxy.activeCircuit, "SeedCapability");
                    proxy.KnownCaps[key] = info;
                }
                else
                    Console.WriteLine("SeedCap info found!");
                info.AddDelegate(new CapsDelegate(SeedCapHandler));
            }

            RestoreName(hash);
            CreateUser(hash);
            CreateAgentCircuitData(hash, key);

            CreateAgentForRegion(hash);

            PostAgentToRegion(hash);

            CleanUpResponse(hash);
        }

        private void TransformName(ref  string first, ref  string last, ref string remoteLoginURL)
        {
            if (!first.Contains("."))
                return;

            remoteLoginURL = "http://" + last;
            if (!last.Contains(":"))
                remoteLoginURL += ":8002";
            //remoteLoginURL += "/";

            string[] fparts = first.Split(new char[] { '.' });
            first = fparts[0];
            last = fparts[1];
        }

        private void RestoreName(Hashtable hash)
        {
            hash["first_name"] = firstName;
            hash["last_name"] = lastName;
        }

        void CleanUpResponse(Hashtable hash)
        {
            hash.Remove("grid_service");
            hash.Remove("grid_service_send_key");
            hash.Remove("inventory_service");
            hash.Remove("asset_service");
            hash.Remove("asset_service_send_key");
            hash.Remove("region_handle");
            hash.Remove("http_port");
            hash.Remove("appearance");
            hash.Remove("auth_token");
            hash.Remove("real_sim_ip");
            hash.Remove("real_sim_port");
        }

        Packet LogoutHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("----------------------");
            Console.WriteLine("         Logout");
            Timer exitTimer = new Timer(DoExit, null, 0, 500);
            return packet;
        }

        private void DoExit(object o)
        {
            Console.Write(".");
            if (Viewer.HasExited)
                Environment.Exit(0);
                
        }

        #endregion Login

        #region Seed and EventQueueGet Caps

        bool SeedCapHandler(CapsRequest req, CapsStage stage)
        {
            Console.WriteLine(">> SeedCapability " + stage.ToString() + " to " + req.Info.URI);

            if (stage != CapsStage.Response) return false;

            if (req.Response.Type == OSDType.Map)
            {
                OSDMap nm = (OSDMap)req.Response;

                // First, let's fix the EventQueue Cap
                if (nm["EventQueueGet"] != null)
                    Console.WriteLine("[GRIDER]: Original EQGet Cap " + nm["EventQueueGet"].AsString());
                else
                    Console.WriteLine("[GRIDER]: SeedCap response did not have EQGet Cap");

                //Agent.NextEQID = UUID.Random().ToString();
                string eqkey = Agent.LocalEQCAP + Agent.NextEQID + "/";

                nm["EventQueueGet"] = OSD.FromString(proxy.loginURI + eqkey);
                Console.WriteLine("[GRIDFER]: New EQGet Cap " + nm["EventQueueGet"].AsString());

                if (!proxy.KnownCaps.ContainsKey(eqkey))
                {
                    CapInfo newCap = new CapInfo(eqkey, req.Info.Sim, eqkey);
                    newCap.AddDelegate(new CapsDelegate(LocalEQHandler));
                    lock (proxy)
                        proxy.KnownCaps[eqkey] = newCap;
                }

                // Then, let's fix the UpdateScriptAgent Cap
                if (nm["UpdateScriptAgent"] != null)
                    Console.WriteLine("[GRIDER]: Original UpdateScriptAgent Cap " + nm["UpdateScriptAgent"].AsString());
                else
                    Console.WriteLine("[GRIDER]: SeedCap response did not have UpdateScriptAgent Cap");
                if (UserInventory.CapsHandlers.ContainsKey("UpdateScriptAgent"))
                {
                    string newcap = (string)UserInventory.CapsHandlers["UpdateScriptAgent"];
                    nm["UpdateScriptAgent"] = OSD.FromString(proxy.loginURI + newcap);
                    nm["UpdateNotecardAgentInventory"] = OSD.FromString(proxy.loginURI + newcap);
                    nm["UpdateScriptAgentInventory"] = OSD.FromString(proxy.loginURI + newcap);
                    if (!proxy.KnownCaps.ContainsKey(newcap))
                    {
                        CapInfo newCap = new CapInfo(newcap, req.Info.Sim, "UpdateScriptAgent");
                        lock (proxy)
                            proxy.KnownCaps[newcap] = newCap;
                    }
                    nm["UpdateScriptAgent"] = OSD.FromString(newcap);
                    nm["UpdateNotecardAgentInventory"] = OSD.FromString(newcap);
                    nm["UpdateScriptAgentInventory"] = OSD.FromString(newcap);

                    Console.WriteLine("[GRIDER]: New UpdateScriptAgent Cap " + nm["UpdateScriptAgent"].AsString());
                }
                else
                    Console.WriteLine("[GRIDER]: UserInventory does not contain UpdateScriptAgent Cap");
                if (UserInventory.CapsHandlers.ContainsKey("NewFileAgentInventory"))
                {
                    string newcap = (string)UserInventory.CapsHandlers["NewFileAgentInventory"];
                    if (!proxy.KnownCaps.ContainsKey(newcap))
                    {
                        CapInfo newCap = new CapInfo(newcap, req.Info.Sim, "NewFileAgentInventory");
                        lock (proxy)
                            proxy.KnownCaps[newcap] = newCap;
                    }
                    nm["NewFileAgentInventory"] = OSD.FromString(newcap);

                    Console.WriteLine("[GRIDER]: New NewFileAgentInventory Cap " + nm["NewFileAgentInventory"].AsString());
                }
                else
                    Console.WriteLine("[GRIDER]: UserInventory does not contain NewFileAgentInventory Cap");

            }

            //Console.WriteLine("---------------");
            //lock (this)
            //{
            //    foreach (KeyValuePair<string, CapInfo> kvp in KnownCaps)
            //    {
            //        Console.WriteLine(" >> Key: " + kvp.Key + "; Value: " + kvp.Value.CapType);
            //    }
            //}
            //Console.WriteLine("---------------");

            return false;
        }

        bool EventQueueGetHandler(CapsRequest req, CapsStage stage)
        {
            Console.WriteLine(">> EventQueuGet ");
            return false;
        }

        bool LocalEQHandler(CapsRequest req, CapsStage stage)
        {
            if (stage != CapsStage.Response) return true; // shortcircuit, so don't foward to sim
            Console.WriteLine(">> LocalEQHandler " + stage.ToString() + " to " + req.Info.URI);

            int length = req.Info.URI.Length; 
            string key = req.Info.URI.Substring(length - 37, 36);
            //Console.WriteLine("       key " + key);
            // it comes back on the Response phase
            EventQueue _eq = Agent.GetEventQueue(key);
            if (_eq == null)
            {
                Console.WriteLine("[GRIDER]: Agent has no EQ??? Creating new one");
                _eq = new EventQueue();
            }
            else
            {
                Console.WriteLine("[GRIDER]: Found Event Queue for agent " + key);
            }

            if (_eq.Run(req) == null)
                Agent.RemoveEventQueue(key);
        
            //    req.Response = new OSD();

            return false;
        }


        bool UpdateNotecardHandler(CapsRequest req, CapsStage stage)
        {
            Console.WriteLine(">> UpdateNotecard " + stage.ToString() + " to " + req.Info.URI);

            if (stage != CapsStage.Response) return true; // shortcircuit, so don't foward to sim

            string uri = (string)UserInventory.CapsHandlers["UpdateNotecardAgentInventory"]; //req.Info.URI;
            Console.WriteLine("[GRIDER]: Forwarding caps request to " + uri);
            Console.WriteLine("[GRIDER]:  request is " + req.Request);
            proxy.ForwardCaps(uri, req);
            return false;
        }


        #endregion Seed and EventQueueGet caps

        #region User and Agents

        void CreateUser(Hashtable hash)
        {
            string inventoryServerURL = string.Empty;
            string assetServerURL = string.Empty;
            string assetServerSendKey = string.Empty;
            UUID authToken = UUID.Zero;

            if (hash["inventory_service"] != null)
                inventoryServerURL = (string)hash["inventory_service"];
            if (hash["asset_service"] != null)
                assetServerURL = (string)hash["asset_service"];
            if (hash["asset_service_send_key"] != null)
                assetServerSendKey = (string)hash["asset_service_send_key"];
            if (hash["auth_token"] != null)
                UUID.TryParse((string)hash["auth_token"], out authToken);

            try
            {

                Agent.SetUserData(proxy, proxy.remoteLoginURI, inventoryServerURL, assetServerURL, assetServerSendKey, authToken);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception " + e);
            }

        }

        void CreateAgentCircuitData(Hashtable hash, string seedcap)
        {
            Hashtable appearance = null;

            Console.WriteLine(" Seed Cap = " + seedcap);
            // http://127.0.0.1:9000/CAPS/b8455b97-411d-49cb-af6b-f03435f46e9a0000/
            // ==> b8455b97-411d-49cb-af6b-f03435f46e9a
            int length = seedcap.Length;
            if (seedcap.EndsWith("/"))
                seedcap = seedcap.Remove(length - 1);
            seedcap = seedcap.Substring(length - 41, 36);
            Console.WriteLine(" Seed Cap = " + seedcap);
            if (hash["appearance"] != null)
            {
                Console.WriteLine("[GRID]: Got appearance from user server");
                appearance = (Hashtable)hash["appearance"];
            }
            else
            {
                Console.WriteLine("[GRID]: Did not get appearance from user server");
            }

            Agent.SetAgentCircuitData(hash, appearance, seedcap);

            UserAuth = new UserAuthClient(Agent.agentCircuitData.AgentID, proxy.remoteLoginURI, Agent.AuthToken);

            UserInventory = new InventoryClient(proxy, Agent.agentCircuitData.AgentID, UserAuth.GetNewKey(), Agent.InventoryServerURL, Agent.AssetServerURL, Agent.AssetServerSendKey);

            Agent.SetUserAuth(UserAuth);

            ProcessUserInventory(hash);
        }

        protected void ProcessUserInventory(Hashtable hash)
        {
            // Let's wait for the inventory to come
            int nsecs = 15;
            while ((UserInventory.InventoryRoot == null) && (nsecs-- > 0))
                Thread.Sleep(1000);

            if (UserInventory.InventoryRoot == null)
            {
                Console.WriteLine("[GRIDER]: No inventory");
                return; // We should probably say something to the viewer
            }

            Console.WriteLine("[GRIDER]: Inventory successfully retrieved");

            ArrayList AgentInventoryArray = UserInventory.InventoryRoot.InventoryArray;

            Hashtable InventoryRootHash = new Hashtable();
            InventoryRootHash["folder_id"] = UserInventory.InventoryRoot.RootFolderID.ToString();
            ArrayList InventoryRoot = new ArrayList();
            InventoryRoot.Add(InventoryRootHash);

            Console.WriteLine("[GRIDER]: Adjusting inventory root");
            hash["inventory-skeleton"] = AgentInventoryArray;
            hash["inventory-root"] = InventoryRoot;

            // We can get rid of the user's inventory now
            UserInventory.InventoryRoot = null;

            // Let's not touch the Library folder
        }


        void CreateAgentForRegion(Hashtable hash)
        {
            string gridServerURL = string.Empty, gridserverkey = string.Empty;
            if (hash["grid_service"] != null)
                gridServerURL = (string)hash["grid_service"];
            if (hash["grid_service_send_key"] != null)
                gridserverkey = (string)hash["grid_service_send_key"];

            int port = 9000;
            string ip = "127.0.0.1";
            IPAddress ipaddr = IPAddress.Parse(ip);
            if (hash["real_sim_port"] != null)
                port = (Int32)hash["real_sim_port"];
            else
                Console.WriteLine("[Grider]: region port not present");
            if (hash["real_sim_ip"] != null)
            {
                ip = (string)hash["real_sim_ip"];
                Console.WriteLine("  >> " + ip);
            }
            else
                Console.WriteLine("[Grider]: region IP not present");
            IPAddress.TryParse(ip, out ipaddr);
            IPEndPoint regionEndPoint = new IPEndPoint(ipaddr, port);
            Console.WriteLine("[Grider]: IPEndPoint of region is " + regionEndPoint.ToString());

            ulong regionHandle = 0;
            if (hash["region_handle"] != null)
            {
                if (!UInt64.TryParse((string)hash["region_handle"], out regionHandle))
                    Console.WriteLine("[Grider]: Unable to parse region handle from " + (string)hash["region_handle"]);
            }
            if (regionHandle == 0)
            {
                Console.WriteLine("[Grider]: Unable to detect Region Handle in login message");
                return ;
            }

            uint httpPort = 9000;
            if (hash["http_port"] != null)
            {
                //Console.WriteLine("  >> type of http_port: " + hash["http_port"].GetType().ToString());
                httpPort = (uint)((Int32)hash["http_port"]);
            }

            MainAgent = new Agent(gridServerURL, gridserverkey, regionHandle, regionEndPoint, httpPort);
        }

        bool PostAgentToRegion(Hashtable response)
        {
            return MainAgent.Go();
        }

        #endregion User and Agents

        #region Teleports

        Packet TeleportHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("\n>> TeleportRequest for agent at " + MainAgent.regionEndPoint.ToString());

            TeleportLocationRequestPacket tppack = (TeleportLocationRequestPacket)packet;
            ulong handle = tppack.Info.RegionHandle;
            if (MainAgent.TheRegion.RegionHandle == handle)
                // Let it pass. Let the region deal with it, it's safe.
                return packet;

            // else we deal with it
            string url = "http://" + MainAgent.TheRegion.ExternalHostName + ":" + MainAgent.TheRegion.HttpPort + "/";
            RegionInfo regInfo = OpenSimComms.GetRegionInfo(url, string.Empty, handle);
            Console.WriteLine("[Grider]: Got region info " + regInfo.RegionName + " in " + regInfo.RegionLocX + "-" + regInfo.RegionLocY);

            Vector3 pos = tppack.Info.Position;
            Vector3 lookat = tppack.Info.LookAt;
            Agent newAgent = new Agent(regInfo, pos, lookat);
            newAgent.Go();

            string capsPathOrig = "http://" + regInfo.ExternalHostName + ":" + regInfo.HttpPort
                + "/CAPS/" + /*Agent.agentCircuitData.CapsPath*/ newAgent.CapsSeed + "0000/";
            string capsPath = proxy.loginURI + capsPathOrig;

            IPEndPoint fakeSim = proxy.ProxySim(newAgent.regionEndPoint);

            CapInfo info = new CapInfo(capsPathOrig, newAgent.regionEndPoint, "SeedCapability");
            info.AddDelegate(new CapsDelegate(SeedCapHandler));
            lock (this)
            {
                proxy.KnownCaps[capsPathOrig] = info;
            }

            EventQueueEvent mapES = LLEvents.EnableSimulator(regInfo.RegionHandle, fakeSim);
            EventQueueEvent mapEAC = LLEvents.EstablishAgentCommunication(Agent.agentCircuitData.AgentID, fakeSim.ToString(), capsPath);
            List<EventQueueEvent> l = new List<EventQueueEvent>();
            l.Add(mapES);
            //l.Add(mapEAC);
            EventQueue eq = MainAgent.EQ;
            Console.WriteLine("    >>> Posting events to queue " + eq.id + " related to agent at " + MainAgent.regionEndPoint.ToString());
            eq.SendEvents(l);

            //Thread.Sleep(5);
            EventQueueEvent mapTF = LLEvents.TeleportFinishEvent(regInfo.RegionHandle, 13, fakeSim, 4, 16, capsPath, Agent.agentCircuitData.AgentID);
            eq.SendEvent(mapTF);

            //Thread.Sleep(7000);
            //DisableSimulatorPacket ds = new DisableSimulatorPacket();
            //proxy.InjectPacket(ds, Direction.Incoming);
            //CloseCircuitPacket cc = new CloseCircuitPacket();
            //proxy.InjectPacket(cc, Direction.Incoming);


            oldAgent = MainAgent;
            MainAgent = newAgent;
            proxy.activeCircuit = MainAgent.regionEndPoint;

            Console.WriteLine("[Grider]: MainAgent now at " + MainAgent.regionEndPoint.ToString());
            return null;
        }

        Packet CompleteMovementHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("\n>> CompleteMovement for agent at " + MainAgent.regionEndPoint.ToString());
            CompleteAgentMovementPacket cm = (CompleteAgentMovementPacket)packet;
            if (oldAgent != null)
                oldAgent.Retrieve();
            MainAgent.Update(oldAgent);
            return packet;
        }

        Agent oldAgent;

        Packet MovementCompleteHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("\n>> MovementComplete for agent at " + MainAgent.regionEndPoint.ToString());
            AgentMovementCompletePacket mc = (AgentMovementCompletePacket)packet;

            //proxy.UnproxySim(MainAgent.regionEndPoint);

            // Send the attachments
            List<int> attPts = Agent.agentCircuitData.Appearance.GetAttachedPoints();
            foreach (int attPt in attPts)
            {
                UUID itemId = Agent.agentCircuitData.Appearance.GetAttachedItem(attPt);
                OpenSimComms.CreateObject(MainAgent.TheRegion, Agent.agentCircuitData.AgentID, itemId);
            }

            EventQueueEvent ds = LLEvents.DisableSimulator(0);
            if (oldAgent != null)
            {
                oldAgent.Stop(); //Console.WriteLine("   ----- Posting DS to " + oldAgent.EQ.id);
                oldAgent.EQ.SendEvent(ds);
            }

            return packet;
        }

        #endregion Teleports

        #region Inventory


        Packet FetchInventoryFolderHandler(Packet packet, IPEndPoint endPoint)
        {
            FetchInventoryDescendentsPacket folderReq = (FetchInventoryDescendentsPacket)packet;

            Console.WriteLine("[Grider]: FetchInventoryDescendants ");
            //OpenSimComms.FetchInventoryDescendants(folderReq.InventoryData.FolderID, InvDescendants);
            if (!UserInventory.FetchInventoryDescendants(folderReq))
                return packet;

            return null;
        }

        Packet InventoryDescendantsReply(Packet packet, IPEndPoint endPoint)
        {
            InventoryDescendentsPacket folderReq = (InventoryDescendentsPacket)packet;

            Console.WriteLine("[Grider]: InventoryDescendantsReply ");
            //OpenSimComms.FetchInventoryDescendants(folderReq.InventoryData.FolderID, InvDescendants);
            return packet;
        }

        Packet GetInventoryHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: FetchInventory");
            //return packet;

            FetchInventoryPacket invPacket = (FetchInventoryPacket)packet;

            if (!UserInventory.GetInventoryItem(invPacket))
                return packet;

            return null;
        }

        Packet CreateInventoryFolderHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: CreateInventoryFolder");
            //return packet;

            CreateInventoryFolderPacket invPacket = (CreateInventoryFolderPacket)packet;

            if (!UserInventory.CreateInventoryFolder(invPacket))
                return packet;

            return null;
        }

        Packet UpdateInventoryFolderHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: UpdateInventoryFolder");
            //return packet;

            UpdateInventoryFolderPacket invPacket = (UpdateInventoryFolderPacket)packet;

            if (!UserInventory.UpdateInventoryFolder(invPacket))
                return packet;

            return null;
        }

        Packet MoveInventoryFolderHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: MoveInventoryFolder");
            //return packet;

            MoveInventoryFolderPacket invPacket = (MoveInventoryFolderPacket)packet;

            if (!UserInventory.MoveInventoryFolder(invPacket))
                return packet;

            return null;
        }

        Packet PurgeFolderHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: PurgeFolder");
            //return packet;

            PurgeInventoryDescendentsPacket invPacket = (PurgeInventoryDescendentsPacket)packet;

            if (!UserInventory.PurgeFolder(invPacket))
                return packet;

            return null;
        }

        Packet RemoveInventoryFolderHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: RemoveInventoryFolder");
            //return packet;

            RemoveInventoryFolderPacket invPacket = (RemoveInventoryFolderPacket)packet;

            if (!UserInventory.RemoveInventoryFolder(invPacket))
                return packet;

            return null;
        }

        Packet CreateInventoryItemHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: CreateInventoryItem");
            //return packet;

            CreateInventoryItemPacket invPacket = (CreateInventoryItemPacket)packet;

            if (!UserInventory.CreateInventoryItem(invPacket))
                return packet;

            return null;
        }

        Packet UpdateInventoryItemHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: UpdateInventoryItem");
            //return packet;

            UpdateInventoryItemPacket invPacket = (UpdateInventoryItemPacket)packet;

            if (!UserInventory.UpdateInventoryItem(invPacket))
                return packet;

            return null;
        }

        Packet RemoveInventoryItemHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: RemoveInventoryItem");
            //return packet;

            RemoveInventoryItemPacket invPacket = (RemoveInventoryItemPacket)packet;

            if (!UserInventory.RemoveInventoryItem(invPacket))
                return packet;

            return null;
        }

        Packet MoveInventoryItemHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: MoveInventoryItem");
            //return packet;

            MoveInventoryItemPacket invPacket = (MoveInventoryItemPacket)packet;

            if (!UserInventory.MoveInventoryItem(invPacket))
                return packet;

            return null;
        }

        Packet CopyInventoryItemHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: CopyInventoryItem");
            //return packet;

            CopyInventoryItemPacket invPacket = (CopyInventoryItemPacket)packet;

            if (!UserInventory.CopyInventoryItem(invPacket))
                return packet;

            return null;
        }


        #endregion Inventory

        #region Assets

        Packet ImageRequestHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: ImageRequestHandler");

            RequestImagePacket imgPacket = (RequestImagePacket)packet;

            if (!UserInventory.GetImage(imgPacket))
                //return packet;
                MainAgent.RegClient.GetImage(imgPacket);

            return null;
        }

        Packet AssetTransferRequestHandler(Packet packet, IPEndPoint endPoint)
        {

            TransferRequestPacket transfer = (TransferRequestPacket)packet;

            Console.WriteLine("[Grider]: AssetTransferRequestHandler, type " + transfer.TransferInfo.SourceType);
            if (!UserInventory.GetAsset(transfer))
                return packet;
            else
                return null;
        }

        Packet AssetUploadRequestHandler(Packet packet, IPEndPoint endPoint)
        {

            AssetUploadRequestPacket upload = (AssetUploadRequestPacket)packet;

            Console.WriteLine("[Grider]: AssetUploadRequestHandler");
            if (!UserInventory.PostAsset(upload, Agent.agentCircuitData.SecureSessionID))
                return packet;
            else
                return null;
        }

        Packet SendXferHandler(Packet packet, IPEndPoint endPoint)
        {
            SendXferPacketPacket xfer = (SendXferPacketPacket)packet;

            //Console.WriteLine("[Grider]: SendXferHandler");
            if (!UserInventory.XferAsset(xfer))
                return packet;
            else
                return null;

        }

        #endregion Assets

        #region Wearables and appearance

        Packet WearablesRequestHandler(Packet packet, IPEndPoint endPoint)
        {
            Console.WriteLine("[Grider]: WearablesRequestHandler");
            AgentWearablesRequestPacket wpack = (AgentWearablesRequestPacket) packet;

            AgentWearablesUpdatePacket aw = (AgentWearablesUpdatePacket)PacketPool.Instance.GetPacket(PacketType.AgentWearablesUpdate);
            aw.AgentData.AgentID = wpack.AgentData.AgentID;
            aw.AgentData.SerialNum = (uint)Agent.agentCircuitData.Appearance.Serial++;
            aw.AgentData.SessionID = wpack.AgentData.SessionID;

            // TODO: don't create new blocks if recycling an old packet
            aw.WearableData = new AgentWearablesUpdatePacket.WearableDataBlock[13];
            AgentWearablesUpdatePacket.WearableDataBlock awb;
            AvatarWearable[] wearables = Agent.agentCircuitData.Appearance.Wearables;
            Console.WriteLine("    > Size of wearables: " + wearables.Length);
            for (int i = 0; i < wearables.Length; i++)
            {
                awb = new AgentWearablesUpdatePacket.WearableDataBlock();
                awb.WearableType = (byte)i;
                awb.AssetID = wearables[i].AssetID;
                awb.ItemID = wearables[i].ItemID;
                aw.WearableData[i] = awb;

                //                m_log.DebugFormat(
                //                    "[APPEARANCE]: Sending wearable item/asset {0} {1} (index {2}) for {3}",
                //                    awb.ItemID, awb.AssetID, i, Name);
            }

            //OutPacket(aw, ThrottleOutPacketType.Task);
            proxy.InjectPacket(aw, Direction.Incoming);
            return null;
        }

        #endregion Wearables
    }
}
