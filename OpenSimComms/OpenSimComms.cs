/*
 * OpenSimComms.cs: Client comms for OpenSim servers 
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
using System.IO;
using System.Net;

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenMetaverse.Packets;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Communications.Clients;
//using OpenMetaverse.StructuredData;
using Nwc.XmlRpc;

namespace OpenSimLibComms
{
    public class OpenSimComms
    {
        private static RegionClient region_comms = new RegionClient();

        public static RegionInfo GetRegionInfo(string serverURL, string sendKey, ulong regionHandle)
        {
            RegionInfo regionInfo = null;

            try
            {
                Hashtable requestData = new Hashtable();
                requestData["region_handle"] = regionHandle.ToString();
                requestData["authkey"] = sendKey;
                ArrayList SendParams = new ArrayList();
                SendParams.Add(requestData);
                XmlRpcRequest GridReq = new XmlRpcRequest("simulator_data_request", SendParams);
                XmlRpcResponse GridResp = GridReq.Send(serverURL, 3000);

                Hashtable responseData = (Hashtable) GridResp.Value;

                if (responseData.ContainsKey("error"))
                {
                    Console.WriteLine("[OGS1 GRID SERVICES]: Error received from grid server: " + responseData["error"]);
                    return null;
                }

                uint regX = Convert.ToUInt32((string) responseData["region_locx"]);
                uint regY = Convert.ToUInt32((string) responseData["region_locy"]);
                string externalHostName = (string) responseData["sim_ip"];
                uint simPort = Convert.ToUInt32(responseData["sim_port"]);
                string regionName = (string)responseData["region_name"];
                UUID regionID = new UUID((string)responseData["region_UUID"]);
                uint remotingPort = Convert.ToUInt32((string)responseData["remoting_port"]);
                
                uint httpPort = 9000;
                if (responseData.ContainsKey("http_port"))
                {
                    httpPort = Convert.ToUInt32((string)responseData["http_port"]);
                }

                // Ok, so this is definitively the wrong place to do this, way too hard coded, but it doesn't seem we GET this info?

                string simURI = "http://" + externalHostName + ":" + httpPort;

                // string externalUri = (string) responseData["sim_uri"];

                //IPEndPoint neighbourInternalEndPoint = new IPEndPoint(IPAddress.Parse(internalIpStr), (int) port);
                regionInfo = RegionInfo.Create(regionID, regionName, regX, regY, externalHostName, httpPort, simPort, remotingPort, simURI );
                if (responseData["region_secret"] != null)
                    regionInfo.regionSecret = (string)responseData["region_secret"];

            }
            catch (Exception e)
            {
                Console.WriteLine("[HGrid]: " +
                            "Region lookup failed for: " + regionHandle.ToString() +
                            " - Is the GridServer down?" + e.ToString());
                return null;
            }

            AdjustRegionHandle(regionInfo);

            return regionInfo;
        }

        public static void AdjustRegionHandle(RegionInfo regInfo)
        {
            ulong realHandle = 0;
            if (!UInt64.TryParse(regInfo.regionSecret, out realHandle))
                // Nope
                return;

            uint x = 0, y = 0;
            Utils.LongToUInts(realHandle, out x, out y);
            x = x / Constants.RegionSize;
            y = y / Constants.RegionSize;
            regInfo.RegionLocX = x;
            regInfo.RegionLocY = y;
        }

        public static bool InformRegionOfUser(RegionInfo theRegion, AgentCircuitData agentCircuitData, RegionInfo home, 
                                                string userServer, string inventoryServer, string assetServer, string imServer)
        {
            Hashtable loginParams = new Hashtable();
            loginParams["session_id"] = agentCircuitData.SessionID.ToString();
            loginParams["secure_session_id"] = agentCircuitData.SecureSessionID.ToString();

            loginParams["firstname"] = agentCircuitData.firstname;
            loginParams["lastname"] = agentCircuitData.lastname;

            loginParams["agent_id"] = agentCircuitData.AgentID.ToString();
            loginParams["circuit_code"] = agentCircuitData.circuitcode.ToString();
            loginParams["startpos_x"] = agentCircuitData.startpos.X.ToString();
            loginParams["startpos_y"] = agentCircuitData.startpos.Y.ToString();
            loginParams["startpos_z"] = agentCircuitData.startpos.Z.ToString();
            //loginParams["caps_path"] = agentCircuitData.CapsPath;

            loginParams["userserver_id"] = HGNetworkServersInfo.ServerURI(userServer);
            loginParams["inventoryserver_id"] = HGNetworkServersInfo.ServerURI(inventoryServer);
            loginParams["assetserver_id"] = HGNetworkServersInfo.ServerURI(assetServer);
            loginParams["imserver_id"] = HGNetworkServersInfo.ServerURI(imServer);

            loginParams["root_folder_id"] = UUID.Zero;

            // Let's make a home in the first login region
            loginParams["regionhandle"] = home.RegionHandle.ToString();
            loginParams["home_address"] = home.ExternalHostName;
            loginParams["home_port"] = home.HttpPort.ToString();
            loginParams["home_internal_port"] = loginParams["home_port"]; // irrelevant, but cannot be null
            loginParams["home_remoting"] = loginParams["home_port"]; // irrelevant, but cannot be null

            ArrayList SendParams = new ArrayList();
            SendParams.Add(loginParams);

            // Send
            string uri = "http://" + theRegion.ExternalHostName + ":" + theRegion.HttpPort + "/";
            Console.WriteLine("[HGrid]: Go contacting uri: " + uri);
            XmlRpcRequest request = new XmlRpcRequest("expect_hg_user", SendParams);
            XmlRpcResponse reply;
            try
            {
                reply = request.Send(uri, 6000);
            }
            catch (Exception e)
            {
                Console.WriteLine("[HGrid]: Failed to notify region about user. Reason: " + e.Message);
                return false;
            }

            if (!reply.IsFault)
            {
                bool responseSuccess = true;
                if (reply.Value != null)
                {
                    Hashtable resp = (Hashtable)reply.Value;
                    if (resp.ContainsKey("success"))
                    {
                        if ((string)resp["success"] == "FALSE")
                        {
                            responseSuccess = false;
                        }
                    }
                }
                if (responseSuccess)
                {
                    Console.WriteLine("[HGrid]: Successfully informed remote region about user " + agentCircuitData.AgentID);
                    return true;
                }
                else
                {
                    Console.WriteLine("[HGrid]: Region responded that it is not available to receive clients");
                    return false;
                }
            }
            else
            {
                Console.WriteLine("[HGrid]: XmlRpc request to region failed with message {0}" + reply.FaultString + ", code " + reply.FaultCode);
                return false;
            }
        }

        public static string GetNewKey(string authurl, UUID userID, UUID authKey)
        {
            //List<string> SendParams = new List<string>();
            //SendParams.Add(userID.ToString());
            //SendParams.Add(authKey.ToString());

            //XmlRpcRequest request = new XmlRpcRequest("hg_new_auth_key", SendParams);
            //XmlRpcResponse reply;
            //try
            //{
            //    reply = request.Send(authurl, 6000);
            //}
            //catch (Exception e)
            //{
            //    Console.WriteLine("[HGrid]: Failed to get new key. Reason: " + e.Message);
            //    return string.Empty;
            //}

            //if (!reply.IsFault)
            //{
            //    string newKey = string.Empty;
            //    if (reply.Value != null)
            //        newKey = (string)reply.Value;

            //    return newKey;
            //}
            //else
            //{
            //    Console.WriteLine("[HGrid]: XmlRpc request to get auth key failed with message {0}" + reply.FaultString + ", code " + reply.FaultCode);
            //    return string.Empty;
            //}

            return AuthClient.GetNewKey(authurl, userID, authKey);

        }

        #region Agents
        public static bool CreateChildAgent(RegionInfo region, AgentCircuitData aAgent, string key)
        {
            string reason = String.Empty;
            bool success = region_comms.DoCreateChildAgentCall(region, aAgent, key, out reason);
            Console.WriteLine("[GridSurfer]: posted agent circuit data to " + region.ExternalHostName + ":" + region.HttpPort + " -- " + success);
            return success;
        }

        public static bool UpdateChildAgent(RegionInfo region, AgentCircuitData aAgent, Vector3 pos, Vector3 look)
        {
            AgentData adata = CreateAgentData(region, aAgent, pos, look);
            bool success = region_comms.DoChildAgentUpdateCall(region, adata);
            Console.WriteLine("[GridSurfer]: updated agent in " + region.ExternalHostName + ":" + region.HttpPort + " -- " + success);
            return success;
        }

        public static bool RetrieveRootAgent(RegionInfo region, UUID id, out Vector3 position, out AvatarAppearance appearance)
        {
            position = Vector3.Zero;
            appearance = null;
            IAgentData agent = null;
            if (region_comms.DoRetrieveRootAgentCall(region, id, out agent))
            {
                position = ((CompleteAgentData)agent).Position;
                // Need to create a new appearance from VPs and wearables
                //appearance = ((CompleteAgentData)agent).
            }
            return false;
        }

        public static bool CloseAgent(RegionInfo region, UUID agentID)
        {
            bool success = region_comms.DoCloseAgentCall(region, agentID);
            Console.WriteLine("[GridSurfer]: closed agent in " + region.ExternalHostName + ":" + region.HttpPort + " -- " + success);
            return success;
        }

        private static AgentData CreateAgentData(RegionInfo region, AgentCircuitData agentCircuitData, Vector3 pos, Vector3 look)
        {
            AgentData adata = new AgentData();
            adata.AgentID = agentCircuitData.AgentID;
            adata.RegionHandle = region.RegionHandle;
            adata.Position = pos;
            adata.BodyRotation = Quaternion.Identity;

            if (agentCircuitData.Appearance != null) 
            {
                if (agentCircuitData.Appearance.Texture != null) 
                    adata.AgentTextures = agentCircuitData.Appearance.Texture.GetBytes();
                adata.VisualParams = agentCircuitData.Appearance.VisualParams;
                adata.Wearables = WearablesToUUIDs(agentCircuitData.Appearance.Wearables);
            }
            adata.AlwaysRun = false;
            adata.CircuitCode = agentCircuitData.circuitcode;
            adata.SessionID = agentCircuitData.SessionID;
            return adata;
        }

        private static UUID[] WearablesToUUIDs(AvatarWearable[] aws)
        {
            // We might not pass the Wearables in all cases...
            // They're only needed so that persistent changes to the appearance
            // are preserved in the new region where the user is moving to.
            // But in Hypergrid we might not let this happen.
            int i = 0;
            UUID[] wears = null;
            if (aws != null)
            {
                wears = new UUID[aws.Length * 2];
                foreach (AvatarWearable aw in aws)
                {
                    if (aw != null)
                    {
                        wears[i++] = aw.ItemID;
                        wears[i++] = aw.AssetID;
                    }
                    else
                    {
                        wears[i++] = UUID.Zero;
                        wears[i++] = UUID.Zero;
                    }
                }
            }
            return wears;
        }

        #endregion Agents

        #region Objects
        public static bool CreateObject(RegionInfo region, ISceneObject sog)
        {
            string reason = string.Empty;
            bool success = region_comms.DoCreateObjectCall(region, sog, sog.ToXml2(), true);
            Console.WriteLine("[GridSurfer]: posted object to " + region.ExternalHostName + ":" + region.HttpPort + " -- " + success);
            return success;
        }

        public static bool CreateObject(RegionInfo region, UUID userID, UUID itemID)
        {
            bool success = region_comms.DoCreateObjectCall(region, userID, itemID);
            Console.WriteLine("[GridSurfer]: posted inv item " + itemID + " to " + region.ExternalHostName + ":" + region.HttpPort + " -- " + success);
            return success;
        }
        #endregion

        #region Inventory

        public static bool CreateInventoryHandlers(string serverURL, UUID userID, string authKey, out Hashtable capsHandlers)
        {
            capsHandlers = new Hashtable();

            if (!serverURL.EndsWith("/"))
                serverURL += "/";

            string uri = serverURL + "InvCap/" + userID.ToString() + "/";
            Console.WriteLine("[HGrid] CreateInventoryHandlers in " + uri);

            HttpWebRequest InvRequest = (HttpWebRequest)WebRequest.Create(uri);
            InvRequest.Method = "GET";
            InvRequest.ContentType = "text/plain";
            InvRequest.Timeout = 15000;
            InvRequest.Headers.Add("Authorization", authKey);

            HttpWebResponse webResponse = null;
            try
            {
                webResponse = (HttpWebResponse)InvRequest.GetResponse();
                if (webResponse == null)
                {
                    Console.WriteLine("[HGrid]: Null reply on CreateInventoryHandlers post");
                }

                StreamReader sr = new StreamReader(webResponse.GetResponseStream());
                //reply = sr.ReadToEnd().Trim();
                string reply = sr.ReadToEnd().Trim();
                sr.Close();
                //m_log.InfoFormat("[REST COMMS]: DoCreateChildAgentCall reply was {0} ", reply);
                capsHandlers = DeserializeHashtable(reply);
            }
            catch (WebException ex)
            {
                Console.WriteLine("[HGrid]: exception on reply of CreateInventoryHandlers: " + ex.Message);
                return false;
            }

            Console.WriteLine("[HGrid]: CreateInventoryHanders returned " + webResponse.StatusCode);
            if (webResponse.StatusCode != HttpStatusCode.OK)
            {
                return false;
            }
            return true;

        }

        public delegate void InvCollectionCallBack(InventoryCollection inv);

        public static void GetInventory(string uri, UUID userID, ReturnResponse<InventoryCollection> callBack)
        {
            Console.WriteLine("[HGrid] GetInventory from " + uri);
            try
            {
                RestSessionObjectPosterResponse<Guid, InventoryCollection> requester
                        = new RestSessionObjectPosterResponse<Guid, InventoryCollection>();
                requester.ResponseCallback = callBack;

                requester.BeginPostObject(uri, userID.Guid, string.Empty, string.Empty);
            }
            catch (Exception e)
            {
                Console.WriteLine("[HGrid]: Exception posting to inventory: " + e);
            }
        }



        public static void FetchInventoryDescendants(string uri, InventoryFolderBase fb, ReturnResponse<InventoryCollection> callBack)
        {
            Console.WriteLine("[HGrid] FetchInventoryDescendants from " + uri);
            try
            {
                RestSessionObjectPosterResponse<InventoryFolderBase, InventoryCollection> requester
                        = new RestSessionObjectPosterResponse<InventoryFolderBase, InventoryCollection>();
                requester.ResponseCallback = callBack;

                requester.BeginPostObject(uri, fb, string.Empty, string.Empty);
            }
            catch (Exception e)
            {
                Console.WriteLine("[HGrid]: Exception posting to inventory: " + e);
            }
        }


        public static void GetInventoryItem(string uri, InventoryItemBase item, ReturnResponse<InventoryItemBase> callBack)
        {
            Console.WriteLine("[HGrid] GetInventory from " + uri);
            try
            {
                RestSessionObjectPosterResponse<InventoryItemBase, InventoryItemBase> requester
                        = new RestSessionObjectPosterResponse<InventoryItemBase, InventoryItemBase>();
                requester.ResponseCallback = callBack;

                requester.BeginPostObject(uri, item, string.Empty, string.Empty);
            }
            catch (Exception e)
            {
                Console.WriteLine("[HGrid]: Exception posting to inventory: " + e);
            }
        }

        public static void InventoryFolderOperation(string uri, InventoryFolderBase folder, ReturnResponse<bool> callBack)
        {
            Console.WriteLine("[HGrid] Inventory folder operation " + uri);
            try
            {
                RestSessionObjectPosterResponse<InventoryFolderBase, bool> requester
                        = new RestSessionObjectPosterResponse<InventoryFolderBase, bool>();
                requester.ResponseCallback = callBack;

                requester.BeginPostObject(uri, folder, string.Empty, string.Empty);
            }
            catch (Exception e)
            {
                Console.WriteLine("[HGrid]: Exception posting to inventory: " + e);
            }
        }

        public static void InventoryItemOperation(string uri, InventoryItemBase item, ReturnResponse<InventoryItemBase> callBack)
        {
            Console.WriteLine("[HGrid] Inventory item operation " + uri);
            try
            {
                RestSessionObjectPosterResponse<InventoryItemBase, InventoryItemBase> requester
                        = new RestSessionObjectPosterResponse<InventoryItemBase, InventoryItemBase>();
                requester.ResponseCallback = callBack;

                requester.BeginPostObject(uri, item, string.Empty, string.Empty);
            }
            catch (Exception e)
            {
                Console.WriteLine("[HGrid]: Exception posting to inventory: " + e);
            }
        }

        public static bool RequestAsset(string uri, InventoryItemBase item, ReturnResponse<AssetBase> callBack)
        {
            Console.WriteLine("[HGrid] GetAsset from " + uri);
            try
            {
                RestSessionObjectPosterResponse<InventoryItemBase, AssetBase> requester
                        = new RestSessionObjectPosterResponse<InventoryItemBase, AssetBase>();
                requester.ResponseCallback = callBack;

                requester.BeginPostObject(uri, item, string.Empty, string.Empty);
            }
            catch (Exception e)
            {
                Console.WriteLine("[HGrid]: Exception posting to inventory: " + e);
                return false;
            }
            return true;
        }

        public static bool CreateAsset(string uri, AssetBase asset, ReturnResponse<bool> callBack)
        {
            Console.WriteLine("[HGrid] CreateAsset to " + uri);
            try
            {
                RestSessionObjectPosterResponse<AssetBase, bool> requester
                        = new RestSessionObjectPosterResponse<AssetBase, bool>();
                requester.ResponseCallback = callBack;

                requester.BeginPostObject(uri, asset, string.Empty, string.Empty);
            }
            catch (Exception e)
            {
                Console.WriteLine("[HGrid]: Exception posting to inventory: " + e);
                return false;
            }
            return true;
        }

        #endregion Inventory

        #region misc

        static Hashtable DeserializeHashtable(string str)
        {
            Console.WriteLine("----- Deserialize hashtable ----");
            Hashtable result = new Hashtable();
            string[] pairs = str.Split(';');
            if (pairs.Length > 0)
            {
                foreach (string pair in pairs)
                {
                    string[] parts = pair.Split(',');
                    if (parts.Length == 2)
                    {
                        Console.WriteLine("  >> key " + parts[0] + " value " + parts[1]);
                        result.Add(parts[0], parts[1]);
                    }
                }
            }
            return result;
        }
        #endregion
    }
}
