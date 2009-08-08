/*
 * Agent.cs: The Agents for Grider, the secure Hypergrid client
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
using System.Collections;
using System.Collections.Generic;
using System.Net;

using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Capabilities;
using OpenSimLibComms;

using Nwc.XmlRpc;

namespace Grider
{
    public class Agent
    {
        // These are the invariant parts of agents. All this persists for the duration
        // of the session, and is sent to all regions this client connects to.
        // There's only one of these (static) for all agents.
        public static AgentCircuitData agentCircuitData;
        public static UUID AuthToken;
        public static string UserServerURL;
        public static string InventoryServerURL;
        public static string AssetServerURL;
        public static string AssetServerSendKey;
        public static RegionInfo Home;
        public static string LocalEQCAP = "http://localEQG/";
        public static string NextEQID = string.Empty;
        public static EventQueue NextEQ;
        public static GriderProxy proxy;
        public static UserAuthClient UserAuth;

        public static Dictionary<string, Agent> Agents = new Dictionary<string, Agent>();

        // The rest here is the variant part of agents. It depends on the region to where the
        // client connects to.
        string agentAccess;
        public RegionInfo TheRegion;
        public IPEndPoint regionEndPoint;
        //ulong regionHandle;
        //uint simHttpPort;
        public string GridServerURL;
        public string GridServerSendKey;
        public string eqID;
        public EventQueue EQ;
        public string CapsSeed;
        public RegionClient RegClient;

        Vector3 position = new Vector3(128, 128, 70);
        Vector3 lookAt = new Vector3(0.99f, 0.042f, 0);

        public static void SetUserData(GriderProxy p, string userURL, string inventoryURL, string assetURL, string assetKey, UUID token)
        {
            proxy = p;
            UserServerURL = userURL;
            InventoryServerURL = inventoryURL;
            AssetServerURL = assetURL;
            AssetServerSendKey = assetKey;
            AuthToken = token;
        }

        public static void SetAgentCircuitData(Hashtable hash, Hashtable appearance, string seedcap)
        {
            agentCircuitData = new AgentCircuitData();
            AgentCircuitDataFromXml(hash);
            agentCircuitData.CapsPath = seedcap;
            agentCircuitData.ChildrenCapSeeds = new Dictionary<ulong, string>();
            agentCircuitData.child = true;
            if (appearance != null)
                agentCircuitData.Appearance = new AvatarAppearance(appearance);
            else
                agentCircuitData.Appearance = new AvatarAppearance(agentCircuitData.AgentID);

            ///
            Console.WriteLine("------- Attachments -------");
            Hashtable attachs = agentCircuitData.Appearance.GetAttachments();
            if (attachs == null)
                Console.WriteLine("   None");
            else
            {
                List<int> attPoints = agentCircuitData.Appearance.GetAttachedPoints();
                foreach (int i in attPoints)
                    Console.Write(i + ", ");
                Console.WriteLine("      -----------");
            }
            ///
        }

        public static void AgentCircuitDataFromXml(Hashtable hash)
        {
            //loginFlagsHash = new Hashtable();
            //loginFlagsHash["daylight_savings"] = DST;
            //loginFlagsHash["stipend_since_login"] = StipendSinceLogin;
            //loginFlagsHash["gendered"] = Gendered;
            //loginFlagsHash["ever_logged_in"] = EverLoggedIn;
            //loginFlags.Add(loginFlagsHash);
            //hash["login-flags"] = loginFlags;

            if (hash["first_name"] != null)
                agentCircuitData.firstname = (string)hash["first_name"];

            if (hash["last_name"] != null)
                agentCircuitData.lastname = (string)hash["last_name"];

            Console.WriteLine(" --- " + agentCircuitData.firstname + " " + agentCircuitData.lastname + " ---");
            //if (hash["agent_access"] != null)
            //    agentCircuitData.agentAccess = (string)hash["agent_access"];

            //globalTexturesHash = new Hashtable();
            //globalTexturesHash["sun_texture_id"] = SunTexture;
            //globalTexturesHash["cloud_texture_id"] = CloudTexture;
            //globalTexturesHash["moon_texture_id"] = MoonTexture;
            //globalTextures.Add(globalTexturesHash);
            //// this.eventCategories.Add(this.eventCategoriesHash);

            //AddToUIConfig("allow_first_life", allowFirstLife);
            //uiConfig.Add(uiConfigHash);


            if (hash["agent_id"] != null)
            {
                UUID uuid = UUID.Zero;
                UUID.TryParse((string)hash["agent_id"], out uuid);
                agentCircuitData.AgentID = uuid;
            }

            if (hash["session_id"] != null)
            {
                UUID uuid = UUID.Zero;
                UUID.TryParse((string)hash["session_id"], out uuid);
                agentCircuitData.SessionID = uuid;
            }
            if (hash["secure_session_id"] != null)
            {
                UUID uuid = UUID.Zero;
                UUID.TryParse((string)hash["secure_session_id"], out uuid);
                agentCircuitData.SecureSessionID = uuid;
            }

            if (hash["circuit_code"] != null)
            {
                int code = (int)hash["circuit_code"];
                agentCircuitData.circuitcode = (uint)code;
            }
            //hash["seconds_since_epoch"] = (Int32)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            //hash["global-textures"] = globalTextures;
            if (hash["seed_capability"] != null)
                agentCircuitData.CapsPath = GetRandomCapsObjectPath(); //(string)hash["seed_capability"];

            //hash["event_categories"] = eventCategories;
            //hash["event_notifications"] = new ArrayList(); // todo
            //hash["classified_categories"] = classifiedCategories;
            //hash["ui-config"] = uiConfig;

            // We may fetch these directly from the inventory server
            //hash["inventory-skeleton"] = agentInventory;
            //hash["inventory-skel-lib"] = inventoryLibrary;
            //hash["inventory-root"] = inventoryRoot;
            //hash["inventory-lib-root"] = inventoryLibRoot;
            //hash["gestures"] = activeGestures;
            //hash["inventory-lib-owner"] = inventoryLibraryOwner;

            // And these from the User server
            //hash["initial-outfit"] = initialOutfit;
            //hash["start_location"] = startLocation;
            //hash["home"] = home;
            //hash["look_at"] = lookAt;

            hash["message"] = "GridSurfer: " + (string)hash["message"];
            // And these from our own agent when we have it, perhaps
            agentCircuitData.startpos = new Vector3(128, 128, 70);
            //hash["region_y"] = (Int32)(RegionY * Constants.RegionSize);
            //if (m_buddyList != null)
            //{
            //    hash["buddy-list"] = m_buddyList.ToArray();
            //}

            //hash["login"] = "true";
            //xmlRpcResponse.Value = hash;

        }

        public static void SetUserAuth(UserAuthClient uclient)
        {
            UserAuth = uclient;
        }

        public static EventQueue GetEventQueue(string key)
        {
            lock (Agents)
            {
                if (Agents.ContainsKey(key))
                    return Agents[key].EQ;
            }
            return NextEQ;
        }

        public static void RemoveEventQueue(string key)
        {
            lock (Agents)
            {
                if (Agents.ContainsKey(key))
                    Agents.Remove(key);
            }

        }

        // This is the very first agent
        public Agent(string gridserver, string gridserverkey, ulong regionhandle, IPEndPoint endpoint, uint httpport)
        {
            GridServerURL = gridserver;
            GridServerSendKey = gridserverkey;
            TheRegion = new RegionInfo();
            uint x = 0, y = 0;
            Utils.LongToUInts(regionhandle, out x, out y);
            TheRegion.RegionLocX = x;
            TheRegion.RegionLocY = y;
            //region.RegionHandle = regionhandle;
            TheRegion.ExternalHostName = endpoint.Address.ToString();
            TheRegion.InternalEndPoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), (int)0); 
            TheRegion.HttpPort = httpport;
            //regionHandle = regionhandle;
            regionEndPoint = endpoint;
            //simHttpPort = httpport;

            CapsSeed = agentCircuitData.CapsPath;

            if (Home == null)
                Home = TheRegion;

            RegClient = new RegionClient(proxy, "http://" + TheRegion.ExternalHostName + ":" + TheRegion.HttpPort.ToString(), CapsSeed);

            SetupEQ();
            Console.WriteLine("[Grider]: New agent for region " + TheRegion.ExternalEndPoint.Address + ":" + TheRegion.HttpPort + " at " + TheRegion.RegionLocX + "-" + TheRegion.RegionLocY);
        }

        // All other agents
        public Agent(RegionInfo regInfo, Vector3 pos, Vector3 look)
        {
            TheRegion = regInfo;
            regionEndPoint = TheRegion.ExternalEndPoint;
            position = pos;
            lookAt = look;

            CapsSeed = CapsUtil.GetRandomCapsObjectPath();
            RegClient = new RegionClient(proxy, "http://" + TheRegion.ExternalHostName + ":" + TheRegion.HttpPort.ToString(), CapsSeed);

            SetupEQ();
            Console.WriteLine("[Grider]: New agent for region " + TheRegion.ExternalEndPoint.Address + ":" + TheRegion.HttpPort + " at " 
                + TheRegion.RegionLocX + "-" + TheRegion.RegionLocY + " with caps seed " + CapsSeed);
        }

        void SetupEQ()
        {
            eqID = UUID.Random().ToString();
            NextEQID = eqID;
            EQ = new EventQueue();
            NextEQ = EQ;
            lock (Agents)
            {
                if (!Agents.ContainsKey(eqID))
                {
                    Agents.Add(eqID, this);
                    Console.WriteLine("[Agent]: Added new agent with EQID " + eqID);
                }
                else
                    Console.WriteLine("[Agent]: key already exists???");
            }

        }


        public bool Go()
        {
            OpenSimComms.InformRegionOfUser(TheRegion, agentCircuitData, Home, UserServerURL, InventoryServerURL, AssetServerURL, string.Empty);
            string key = UserAuth.GetNewKey();
            return OpenSimComms.CreateChildAgent(TheRegion, Clone(agentCircuitData), key); 
        }

        public void Update()
        {
            OpenSimComms.UpdateChildAgent(TheRegion, Clone(agentCircuitData), position, lookAt);
        }

        public void Update(Agent oldAgent)
        {
            // Eventually we will copy the data from the oldAgent to this one, appearance, attachments, etc
            Update();
        }

        public void Retrieve()
        {
            AvatarAppearance appearance = null;
            OpenSimComms.RetrieveRootAgent(TheRegion, agentCircuitData.AgentID, out position, out appearance);
            Console.WriteLine("   >> Position of old agent was " + position);
        }

        private delegate void CloseDelegate();

        public void Stop()
        {
            CloseDelegate d = CloseAsync;
            try
            {
                d.BeginInvoke(CloseCompleted, d);
            }
            catch
            {
            }
        }

        private void CloseAsync()
        {
            OpenSimComms.CloseAgent(TheRegion, agentCircuitData.AgentID);
        }

        private void CloseCompleted(IAsyncResult iar)
        {
            Console.WriteLine("[GridSurfer]: Close agent completed.");
            CloseDelegate icon = (CloseDelegate)iar.AsyncState;
            icon.EndInvoke(iar);
        }


        public static string GetRandomCapsObjectPath()
        {
            UUID caps = UUID.Random();
            string capsPath = caps.ToString();
            capsPath = capsPath.Remove(capsPath.Length - 4, 4);
            return capsPath;
        }

        public AgentCircuitData Clone(AgentCircuitData adata)
        {
            AgentCircuitData aclone = new AgentCircuitData();
            aclone.AgentID = adata.AgentID;
            aclone.Appearance = adata.Appearance;
            aclone.BaseFolder = adata.BaseFolder;
            aclone.CapsPath = CapsSeed; // !!! This different
            aclone.child = adata.child;
            aclone.ChildrenCapSeeds = adata.ChildrenCapSeeds;
            aclone.circuitcode = adata.circuitcode;
            aclone.firstname = adata.firstname;
            aclone.InventoryFolder = adata.InventoryFolder;
            aclone.lastname = adata.lastname;
            aclone.SecureSessionID = adata.SecureSessionID;
            aclone.SessionID = adata.SessionID;
            aclone.startpos = adata.startpos;

            return aclone;
        }

    }
}
