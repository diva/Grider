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
using System.Text;

using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Communications.Services;
using OpenSim.Framework.Communications.Cache;
using OpenMetaverse;
using OpenMetaverse.Packets;
using OpenSimLibComms;

namespace Grider
{
    public class InventoryClient : IAssetReceiver
    {
        // Items in the library are owned by this UUID
        private static UUID libOwner = new UUID("11111111-1111-0000-0000-000100bba000");

        public LoginService.InventoryData InventoryRoot = null;

        UUID UserID;
        string AuthKey;
        string AuthToken;
        string InventoryServerURL;
        string AssetServerURL;
        string AssetServerKey;
        GriderProxy proxy;
        bool connected = false;
        public Hashtable CapsHandlers = null;
        //GridAssetClient assClient;
        AssetDownloader assDownloader;

        Dictionary<char, List<InventoryAsset>> inventoryItems = new Dictionary<char, List<InventoryAsset>>();

        Dictionary<UUID, TextureSender> textureSenders = new Dictionary<UUID, TextureSender>();
        Dictionary<UUID, AssetSender> assetSenders = new Dictionary<UUID, AssetSender>();
        Dictionary<UUID, AssetReceiver> assetReceivers = new Dictionary<UUID, AssetReceiver>();

        public InventoryClient(GriderProxy sp, UUID id, string key, string i, string a, string ak)
        {
            proxy = sp;
            UserID = id;
            AuthKey = key;
            InventoryServerURL = i.Trim('/');
            AssetServerURL = a;
            AssetServerKey = ak;

            // Let's connect to inventory, so that it establishes a cap url for us
            Console.WriteLine("[InventoryClient]: Calling inventory for caps with key " + AuthKey);
            connected = OpenSimComms.CreateInventoryHandlers(InventoryServerURL, UserID, AuthKey, out CapsHandlers);
            if (!connected)
                Console.WriteLine("[InventoryClient]: Unable to connect to inventory service");

            Uri uri = null;
            if (Uri.TryCreate(AuthKey, UriKind.Absolute, out uri))
            {
                try
                {
                    AuthToken = uri.PathAndQuery.Trim('/');
                }
                catch { }
            }

            // Get the entire inventory
            GetInventory();

            assDownloader = new AssetDownloader(InventoryServerURL + "/" + AuthToken, this);
        }

        List<InventoryFolderBase> pendingRequests = new List<InventoryFolderBase>();
        List<bool> pendingRequestsFetchFoldersFlag = new List<bool>();
        List<bool> pendingRequestsFetchItemsFlag = new List<bool>();

        #region GetInventory

        void GetInventory()
        {
            if (!connected)
                return;

            string uri = InventoryServerURL + "/" + AuthToken + "/GetInventory/";
            OpenSimComms.GetInventory(uri, UserID, GetInventoryCallBack);
        }

        private void GetInventoryCallBack(InventoryCollection inv)
        {
            List<InventoryItemBase> items = inv.Items;
            List<InventoryFolderBase> folders = inv.Folders;

            // Add them to our local cache
            foreach (InventoryItemBase item in items)
            {
                AddToCache(item);
            }

            UUID rootID = UUID.Zero;
            ArrayList AgentInventoryArray = new ArrayList();
            Hashtable TempHash;
            foreach (InventoryFolderBase InvFolder in folders)
            {
                if (InvFolder.ParentID == UUID.Zero)
                {
                    rootID = InvFolder.ID;
                }
                TempHash = new Hashtable();
                TempHash["name"] = InvFolder.Name;
                TempHash["parent_id"] = InvFolder.ParentID.ToString();
                TempHash["version"] = (Int32)InvFolder.Version;
                TempHash["type_default"] = (Int32)InvFolder.Type;
                TempHash["folder_id"] = InvFolder.ID.ToString();
                AgentInventoryArray.Add(TempHash);
            }

            InventoryRoot = new LoginService.InventoryData(AgentInventoryArray, rootID);

        }

        #endregion GetInventory

        #region FetchInventoryDescendants

        public bool FetchInventoryDescendants(FetchInventoryDescendentsPacket folderRequest)
        {
            if (folderRequest.InventoryData.OwnerID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                InventoryFolderBase f = new InventoryFolderBase();
                f.ID = folderRequest.InventoryData.FolderID;
                f.Owner = folderRequest.InventoryData.OwnerID;

                string uri = InventoryServerURL + "/" + AuthToken + "/FetchDescendants/";
                lock (pendingRequests)
                {
                    pendingRequestsFetchItemsFlag.Add(folderRequest.InventoryData.FetchItems);
                    pendingRequestsFetchFoldersFlag.Add(folderRequest.InventoryData.FetchFolders);
                    pendingRequests.Add(f);
                }
                OpenSimComms.FetchInventoryDescendants(uri, f, DescendantsCallBack);
            }
            // Tell grid surfer not to forward
            return true;
        }

        // Shamelessly copied from LLClientView
        private void DescendantsCallBack(InventoryCollection inv)
        {
            // Send InventoryDescendentsPacket(s)

            Console.WriteLine("[InventoryClient]: Received InventoryCollection");
            InventoryFolderBase fb = null;
            bool fetchFolders = false, fetchItems = false;
            lock (pendingRequests)
            {
                fb = pendingRequests[0];
                pendingRequests.RemoveAt(0);
                fetchFolders = pendingRequestsFetchFoldersFlag[0];
                pendingRequestsFetchFoldersFlag.RemoveAt(0);
                fetchItems = pendingRequestsFetchItemsFlag[0];
                pendingRequestsFetchItemsFlag.RemoveAt(0);
            }
            if (fb == null)
            {
                Console.WriteLine("[InventoryClient]: Request not found ??? ");
                return;
            }

            List<InventoryItemBase> items = inv.Items;
            List<InventoryFolderBase> folders = inv.Folders;

            //// Add them to our local cache -- DON'T, they're already there
            //foreach (InventoryItemBase item in items)
            //{
            //    AddToCache(item);
            //}

            // An inventory descendents packet consists of a single agent section and an inventory details
            // section for each inventory item.  The size of each inventory item is approximately 550 bytes.
            // In theory, UDP has a maximum packet size of 64k, so it should be possible to send descendent
            // packets containing metadata for in excess of 100 items.  But in practice, there may be other
            // factors (e.g. firewalls) restraining the maximum UDP packet size.  See,
            //
            // http://opensimulator.org/mantis/view.php?id=226
            //
            // for one example of this kind of thing.  In fact, the Linden servers appear to only send about
            // 6 to 7 items at a time, so let's stick with 6
            int MAX_ITEMS_PER_PACKET = 6;

            //Ckrinke This variable is not used, so comment out to remove the warning from the compiler (3-21-08)
            //Ckrinke            uint FULL_MASK_PERMISSIONS = 2147483647;

            int itemsSent = 0;
            InventoryDescendentsPacket descend = new InventoryDescendentsPacket();
            if (fetchItems)
            {
                descend.Header.Zerocoded = true;
                descend.AgentData.AgentID = inv.UserID;
                descend.AgentData.OwnerID = inv.UserID;
                descend.AgentData.FolderID = fb.ID;
                descend.AgentData.Version = 1;

                if (items.Count < MAX_ITEMS_PER_PACKET)
                {
                    descend.ItemData = new InventoryDescendentsPacket.ItemDataBlock[items.Count];
                }
                else
                {
                    descend.ItemData = new InventoryDescendentsPacket.ItemDataBlock[MAX_ITEMS_PER_PACKET];
                }

                // Descendents must contain the *total* number of descendents (plus folders, whether we
                // fetch them or not), not the number of entries we send in this packet. For consistency,
                // I'll use it for folder-requests, too, although I wasn't able to get one with
                // FetchFolders = true.
                // TODO this should be checked with FetchFolders = true
                descend.AgentData.Descendents = items.Count + folders.Count;

                int count = 0;
                int i = 0;
                foreach (InventoryItemBase item in items)
                {
                    descend.ItemData[i] = new InventoryDescendentsPacket.ItemDataBlock();
                    descend.ItemData[i].ItemID = item.ID;
                    descend.ItemData[i].AssetID = item.AssetID;
                    descend.ItemData[i].CreatorID = item.CreatorIdAsUuid;
                    descend.ItemData[i].BaseMask = item.BasePermissions;
                    descend.ItemData[i].Description = StringToPacketBytes(item.Description);
                    descend.ItemData[i].EveryoneMask = item.EveryOnePermissions;
                    descend.ItemData[i].OwnerMask = item.CurrentPermissions;
                    descend.ItemData[i].FolderID = item.Folder;
                    descend.ItemData[i].InvType = (sbyte)item.InvType;
                    descend.ItemData[i].Name = StringToPacketBytes(item.Name);
                    descend.ItemData[i].NextOwnerMask = item.NextPermissions;
                    descend.ItemData[i].OwnerID = item.Owner;
                    descend.ItemData[i].Type = (sbyte)item.AssetType;

                    descend.ItemData[i].GroupID = item.GroupID;
                    descend.ItemData[i].GroupOwned = item.GroupOwned;
                    descend.ItemData[i].GroupMask = item.GroupPermissions;
                    descend.ItemData[i].CreationDate = item.CreationDate;
                    descend.ItemData[i].SalePrice = item.SalePrice;
                    descend.ItemData[i].SaleType = item.SaleType;
                    descend.ItemData[i].Flags = item.Flags;

                    descend.ItemData[i].CRC =
                        Helpers.InventoryCRC(descend.ItemData[i].CreationDate, descend.ItemData[i].SaleType,
                                             descend.ItemData[i].InvType, descend.ItemData[i].Type,
                                             descend.ItemData[i].AssetID, descend.ItemData[i].GroupID,
                                             descend.ItemData[i].SalePrice,
                                             descend.ItemData[i].OwnerID, descend.ItemData[i].CreatorID,
                                             descend.ItemData[i].ItemID, descend.ItemData[i].FolderID,
                                             descend.ItemData[i].EveryoneMask,
                                             descend.ItemData[i].Flags, descend.ItemData[i].OwnerMask,
                                             descend.ItemData[i].GroupMask, item.CurrentPermissions);

                    i++;
                    count++;
                    itemsSent++;
                    if (i == MAX_ITEMS_PER_PACKET)
                    {
                        descend.Header.Zerocoded = true;
                        AddNullFolderBlockToDecendentsPacket(ref descend);
                        proxy.InjectPacket(descend, Direction.Incoming);
                        //OutPacket(descend, ThrottleOutPacketType.Asset);

                        if ((items.Count - count) > 0)
                        {
                            descend = new InventoryDescendentsPacket();
                            descend.Header.Zerocoded = true;
                            descend.AgentData.AgentID = inv.UserID;
                            descend.AgentData.OwnerID = inv.UserID;
                            descend.AgentData.FolderID = fb.ID;
                            descend.AgentData.Version = 1;

                            if ((items.Count - count) < MAX_ITEMS_PER_PACKET)
                            {
                                descend.ItemData = new InventoryDescendentsPacket.ItemDataBlock[items.Count - count];
                            }
                            else
                            {
                                descend.ItemData = new InventoryDescendentsPacket.ItemDataBlock[MAX_ITEMS_PER_PACKET];
                            }
                            descend.AgentData.Descendents = items.Count + folders.Count;
                            i = 0;
                        }
                    }
                }

                if (0 < i && i < MAX_ITEMS_PER_PACKET)
                {
                    AddNullFolderBlockToDecendentsPacket(ref descend);
                    proxy.InjectPacket(descend, Direction.Incoming);
                    ///OutPacket(descend, ThrottleOutPacketType.Asset);
                }
            }

            //send subfolders
            if (fetchFolders)
            {
                descend = new InventoryDescendentsPacket();
                descend.Header.Zerocoded = true;
                descend.AgentData.AgentID = inv.UserID;
                descend.AgentData.OwnerID = inv.UserID;
                descend.AgentData.FolderID = fb.ID;
                descend.AgentData.Version = 1;


                if (folders.Count < MAX_ITEMS_PER_PACKET)
                {
                    descend.FolderData = new InventoryDescendentsPacket.FolderDataBlock[folders.Count];
                }
                else
                {
                    descend.FolderData = new InventoryDescendentsPacket.FolderDataBlock[MAX_ITEMS_PER_PACKET];
                }

                // Not sure if this scenario ever actually occurs, but nonetheless we include the items
                // count even if we're not sending item data for the same reasons as above.
                descend.AgentData.Descendents = items.Count + folders.Count;

                int i = 0;
                int count = 0;
                foreach (InventoryFolderBase folder in folders)
                {
                    descend.FolderData[i] = new InventoryDescendentsPacket.FolderDataBlock();
                    descend.FolderData[i].FolderID = folder.ID;
                    descend.FolderData[i].Name = StringToPacketBytes(folder.Name);
                    descend.FolderData[i].ParentID = folder.ParentID;
                    descend.FolderData[i].Type = (sbyte)folder.Type;

                    i++;
                    count++;
                    itemsSent++;
                    if (i == MAX_ITEMS_PER_PACKET)
                    {
                        AddNullItemBlockToDescendentsPacket(ref descend);
                        proxy.InjectPacket(descend, Direction.Incoming);
                        //OutPacket(descend, ThrottleOutPacketType.Asset);

                        if ((folders.Count - count) > 0)
                        {
                            descend = new InventoryDescendentsPacket();
                            if ((folders.Count - count) < MAX_ITEMS_PER_PACKET)
                            {
                                descend.FolderData =
                                    new InventoryDescendentsPacket.FolderDataBlock[folders.Count - count];
                            }
                            else
                            {
                                descend.FolderData =
                                    new InventoryDescendentsPacket.FolderDataBlock[MAX_ITEMS_PER_PACKET];
                            }
                            descend.AgentData.Descendents = items.Count + folders.Count;
                            i = 0;
                        }
                    }
                }

                if (0 < i && i < MAX_ITEMS_PER_PACKET)
                {
                    AddNullItemBlockToDescendentsPacket(ref descend);
                    proxy.InjectPacket(descend, Direction.Incoming);
                    //OutPacket(descend, ThrottleOutPacketType.Asset);
                }
            }

            if (itemsSent == 0)
            {
                // no items found.
                descend = new InventoryDescendentsPacket();
                descend.Header.Zerocoded = true;
                descend.AgentData.AgentID = inv.UserID;
                descend.AgentData.OwnerID = inv.UserID;
                descend.AgentData.FolderID = fb.ID;
                descend.AgentData.Version = 1;
                descend.AgentData.Descendents = 0;
                AddNullItemBlockToDescendentsPacket(ref descend);
                AddNullFolderBlockToDecendentsPacket(ref descend);
                proxy.InjectPacket(descend, Direction.Incoming);
                //OutPacket(descend, ThrottleOutPacketType.Asset);
            }

        }

        #endregion

        #region GetInventoryItem

        public bool GetInventoryItem(FetchInventoryPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/GetItem/";
                for (int i = 0; i < invPacket.InventoryData.Length; i++)
                {

                    InventoryItemBase item = new InventoryItemBase();
                    item.ID = invPacket.InventoryData[i].ItemID;
                    item.Owner = invPacket.InventoryData[i].OwnerID;
                    OpenSimComms.GetInventoryItem(uri, item, InventoryCallBack);
                }
            }
            // Tell grid surfer not to forward
            return true;

        }

        private void ConfirmInventoryItem(InventoryItemBase item)
        {
            string uri = InventoryServerURL + "/" + AuthToken + "/GetItem/";
            OpenSimComms.GetInventoryItem(uri, item, InventoryItemCallBack);
        }


        private void InventoryCallBack(InventoryItemBase item)
        {
            Console.WriteLine("[InventoryClient]: InventoryCallBack");
            const uint FULL_MASK_PERMISSIONS = (uint)PermissionMask.All;

            FetchInventoryReplyPacket inventoryReply = new FetchInventoryReplyPacket(); //(FetchInventoryReplyPacket)PacketPool.Instance.GetPacket(PacketType.FetchInventoryReply);
            // TODO: don't create new blocks if recycling an old packet
            inventoryReply.AgentData.AgentID = item.Owner;
            inventoryReply.InventoryData = new FetchInventoryReplyPacket.InventoryDataBlock[1];
            inventoryReply.InventoryData[0] = new FetchInventoryReplyPacket.InventoryDataBlock();
            inventoryReply.InventoryData[0].ItemID = item.ID;
            inventoryReply.InventoryData[0].AssetID = item.AssetID;
            inventoryReply.InventoryData[0].CreatorID = item.CreatorIdAsUuid;
            inventoryReply.InventoryData[0].BaseMask = item.BasePermissions;
            inventoryReply.InventoryData[0].CreationDate = item.CreationDate;

            inventoryReply.InventoryData[0].Description = StringToPacketBytes(item.Description);
            inventoryReply.InventoryData[0].EveryoneMask = item.EveryOnePermissions;
            inventoryReply.InventoryData[0].FolderID = item.Folder;
            inventoryReply.InventoryData[0].InvType = (sbyte)item.InvType;
            inventoryReply.InventoryData[0].Name = StringToPacketBytes(item.Name);
            inventoryReply.InventoryData[0].NextOwnerMask = item.NextPermissions;
            inventoryReply.InventoryData[0].OwnerID = item.Owner;
            inventoryReply.InventoryData[0].OwnerMask = item.CurrentPermissions;
            inventoryReply.InventoryData[0].Type = (sbyte)item.AssetType;

            inventoryReply.InventoryData[0].GroupID = item.GroupID;
            inventoryReply.InventoryData[0].GroupOwned = item.GroupOwned;
            inventoryReply.InventoryData[0].GroupMask = item.GroupPermissions;
            inventoryReply.InventoryData[0].Flags = item.Flags;
            inventoryReply.InventoryData[0].SalePrice = item.SalePrice;
            inventoryReply.InventoryData[0].SaleType = item.SaleType;

            inventoryReply.InventoryData[0].CRC =
                Helpers.InventoryCRC(1000, 0, inventoryReply.InventoryData[0].InvType,
                                     inventoryReply.InventoryData[0].Type, inventoryReply.InventoryData[0].AssetID,
                                     inventoryReply.InventoryData[0].GroupID, 100,
                                     inventoryReply.InventoryData[0].OwnerID, inventoryReply.InventoryData[0].CreatorID,
                                     inventoryReply.InventoryData[0].ItemID, inventoryReply.InventoryData[0].FolderID,
                                     FULL_MASK_PERMISSIONS, 1, FULL_MASK_PERMISSIONS, FULL_MASK_PERMISSIONS,
                                     FULL_MASK_PERMISSIONS);
            inventoryReply.Header.Zerocoded = true;
            //OutPacket(inventoryReply, ThrottleOutPacketType.Asset);
            proxy.InjectPacket(inventoryReply, Direction.Incoming);
        }

        #endregion

        #region InventoryFolder operations

        public bool CreateInventoryFolder(CreateInventoryFolderPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/NewFolder/";
                InventoryFolderBase folder = new InventoryFolderBase();
                folder.ID = invPacket.FolderData.FolderID;
                folder.Owner = invPacket.AgentData.AgentID;
                folder.Name = Util.FieldToString(invPacket.FolderData.Name);
                folder.ParentID = invPacket.FolderData.ParentID;
                folder.Type = (short)invPacket.Type;
                OpenSimComms.InventoryFolderOperation(uri, folder, NullCallBack);
            }
            // Tell grid surfer not to forward
            return true;
        }

        public bool UpdateInventoryFolder(UpdateInventoryFolderPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/UpdateFolder/";
                foreach (UpdateInventoryFolderPacket.FolderDataBlock block in invPacket.FolderData) 
                {
                    InventoryFolderBase folder = new InventoryFolderBase();
                    folder.ID = block.FolderID;
                    folder.Owner = invPacket.AgentData.AgentID;
                    folder.Name = Util.FieldToString(block.Name);
                    folder.ParentID = block.ParentID;
                    folder.Type = (short)block.Type;
                    OpenSimComms.InventoryFolderOperation(uri, folder, NullCallBack);
                }
            }
            // Tell grid surfer not to forward
            return true;
        }

        public bool MoveInventoryFolder(MoveInventoryFolderPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/MoveFolder/";
                foreach (MoveInventoryFolderPacket.InventoryDataBlock block in invPacket.InventoryData)
                {
                    InventoryFolderBase folder = new InventoryFolderBase();
                    folder.ID = block.FolderID;
                    folder.Owner = invPacket.AgentData.AgentID;
                    folder.ParentID = block.ParentID;
                    OpenSimComms.InventoryFolderOperation(uri, folder, NullCallBack);
                }
            }
            // Tell grid surfer not to forward
            return true;
        }

        public bool PurgeFolder(PurgeInventoryDescendentsPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/PurgeFolder/";
                InventoryFolderBase folder = new InventoryFolderBase();
                folder.ID = invPacket.InventoryData.FolderID;
                folder.Owner = invPacket.AgentData.AgentID;
                OpenSimComms.InventoryFolderOperation(uri, folder, NullCallBack);
            }
            // Tell grid surfer not to forward
            return true;
        }

        public bool RemoveInventoryFolder(RemoveInventoryFolderPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/RemoveFolder/";
                foreach (RemoveInventoryFolderPacket.FolderDataBlock block in invPacket.FolderData)
                {
                    InventoryFolderBase folder = new InventoryFolderBase();
                    folder.ID = block.FolderID;
                    folder.Owner = invPacket.AgentData.AgentID;
                    OpenSimComms.InventoryFolderOperation(uri, folder, NullCallBack);
                }
            }
            // Tell grid surfer not to forward
            return true;
        }


        #endregion

        #region InventoryItem operations

        Dictionary<UUID, uint> invCallbackNumbers = new Dictionary<UUID, uint>();

        private InventoryItemBase NewInventoryItem(UUID creator, UUID folderID, string name, uint flags, AssetBase asset, sbyte invType, 
            uint baseMask, uint currentMask, uint everyoneMask, uint nextOwnerMask, uint groupMask, int creationDate)
        {
            InventoryItemBase item = new InventoryItemBase();
            item.Owner = creator;
            item.CreatorIdAsUuid = creator;
            item.ID = UUID.Random();
            item.AssetID = asset.FullID;
            item.Description = asset.Description;
            item.Name = name;
            item.Flags = flags;
            item.AssetType = asset.Type;
            item.InvType = invType;
            item.Folder = folderID;
            item.CurrentPermissions = currentMask;
            item.NextPermissions = nextOwnerMask;
            item.EveryOnePermissions = everyoneMask;
            item.GroupPermissions = groupMask;
            item.BasePermissions = baseMask;
            item.CreationDate = creationDate;

            return item;
        }

        private InventoryItemBase NewInventoryItem(UpdateInventoryItemPacket.InventoryDataBlock block)
        {
            UUID transactionID = block.TransactionID;
            sbyte invType = block.InvType;
            sbyte assetType = block.Type;
            string name = Util.FieldToString(block.Name);
            UUID folderID = block.FolderID;
            string description = Util.FieldToString(block.Description);

            InventoryItemBase item = new InventoryItemBase();
            item.ID = block.ItemID;
            item.Name = name;
            item.Description = description;
            item.AssetType = assetType;
            item.CreationDate = (block.CreationDate == 0) ? Util.UnixTimeSinceEpoch() : block.CreationDate;
            item.CurrentPermissions = (uint)PermissionMask.All; //|= 8; // See Scene.Inventory
            item.EveryOnePermissions = block.EveryoneMask;
            item.Flags = block.Flags;
            item.Folder = folderID;
            item.GroupID = block.GroupID;
            item.GroupOwned = block.GroupOwned;
            item.GroupPermissions = block.GroupMask;
            item.InvType = invType;
            item.NextPermissions = block.NextOwnerMask;
            item.SalePrice = block.SalePrice;
            item.SaleType = block.SaleType;
            item.Owner = UserID;

            return item;
        }

        private InventoryItemBase NewInventoryItem(UpdateInventoryItemPacket.InventoryDataBlock block, UUID assetID)
        {
            UUID transactionID = block.TransactionID;
            sbyte invType = block.InvType;
            sbyte assetType = block.Type;
            string name = Util.FieldToString(block.Name);
            UUID folderID = block.FolderID;
            string description = Util.FieldToString(block.Description);

            InventoryItemBase item = new InventoryItemBase();
            item.ID = block.ItemID;
            item.Name = name;
            item.Description = description;
            item.AssetType = assetType;
            item.CreationDate = (block.CreationDate == 0) ? Util.UnixTimeSinceEpoch() : block.CreationDate;
            item.CurrentPermissions = (uint)PermissionMask.All; //|= 8; // See Scene.Inventory
            item.EveryOnePermissions = block.EveryoneMask;
            item.Flags = block.Flags;
            item.Folder = folderID;
            item.GroupID = block.GroupID;
            item.GroupOwned = block.GroupOwned;
            item.GroupPermissions = block.GroupMask;
            item.InvType = invType;
            item.NextPermissions = block.NextOwnerMask;
            item.SalePrice = block.SalePrice;
            item.SaleType = block.SaleType;
            item.Owner = UserID;
            item.AssetID = assetID;

            return item;
        }


        public bool CreateInventoryItem(CreateInventoryItemPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/NewItem/";
                UUID transactionID = invPacket.InventoryBlock.TransactionID;
                sbyte invType = invPacket.InventoryBlock.InvType;
                sbyte assetType = invPacket.InventoryBlock.Type;
                string name = Util.FieldToString(invPacket.InventoryBlock.Name);
                UUID folderID = invPacket.InventoryBlock.FolderID;
                string description = Util.FieldToString(invPacket.InventoryBlock.Description);

                // This seems to be zero for notecards and scripts
                if (transactionID == UUID.Zero)
                {
                    byte[] data = null;

                    if (invType == 3) // Landmark
                    {
                        // WARNING! This is not right. We need to ask the region about the position of the agent
                        //Vector3 pos = new Vector3(128, 128, 70); 
                        //string strdata = String.Format(
                        //    "Landmark version 2\nregion_id {0}\nlocal_pos {1} {2} {3}\nregion_handle {4}\n",
                        //    region.RegionID,
                        //    pos.X, pos.Y, pos.Z,
                        //    region.RegionHandle);
                        //data = Encoding.ASCII.GetBytes(strdata);

                        // We still don't handle these
                        return true;
                    }

                    AssetBase asset = NewAsset(name, description, assetType, data);
                    // Post the asset
                    string asseturi = InventoryServerURL + "/" + AuthToken + "/PostAsset/";
                    OpenSimComms.CreateAsset(asseturi, asset, NullCallBack);

                    InventoryItemBase item = NewInventoryItem(UserID, folderID, name, 0, asset, invType, 
                        (uint)PermissionMask.All, (uint)PermissionMask.All, 0, invPacket.InventoryBlock.NextOwnerMask, 0, Util.UnixTimeSinceEpoch());
                    OpenSimComms.InventoryItemOperation(uri, item, InventoryItemCreateCallBack);
                    lock (invCallbackNumbers)
                    {
                        if (!invCallbackNumbers.ContainsKey(item.ID))
                            invCallbackNumbers.Add(item.ID, invPacket.InventoryBlock.CallbackID);
                    }
                    AddToCache(item);

                }
                // Not zero for clothes and body parts
                else
                {
                    Console.WriteLine("[UserInventory]: CreateItem with non-zero transactionID " + transactionID);
                    if (assetReceivers.ContainsKey(transactionID))
                    {
                        AssetReceiver assReceiver = assetReceivers[transactionID];
                        lock (assetReceivers)
                            assetReceivers.Remove(transactionID);

                        InventoryItemBase item = NewInventoryItem(UserID, folderID, name, 0, assReceiver.m_asset, invType,
                                 (uint)PermissionMask.All, (uint)PermissionMask.All, 0, invPacket.InventoryBlock.NextOwnerMask, 0, Util.UnixTimeSinceEpoch());
                        OpenSimComms.InventoryItemOperation(uri, item, InventoryItemCreateCallBack);
                        lock (invCallbackNumbers)
                        {
                            if (!invCallbackNumbers.ContainsKey(item.ID))
                                invCallbackNumbers.Add(item.ID, invPacket.InventoryBlock.CallbackID);
                        }
                        AddToCache(item);

                    }
                }
            }
            // Tell grid surfer not to forward
            return true;
        }

        public bool UpdateInventoryItem(UpdateInventoryItemPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/UpdateItem/";
                for (int i = 0; i < invPacket.InventoryData.Length; i++)
                {
                    UUID transactionID = invPacket.InventoryData[i].TransactionID;
                    sbyte invType = invPacket.InventoryData[i].InvType;
                    sbyte assetType = invPacket.InventoryData[i].Type;
                    string name = Util.FieldToString(invPacket.InventoryData[i].Name);
                    UUID folderID = invPacket.InventoryData[i].FolderID;
                    string description = Util.FieldToString(invPacket.InventoryData[i].Description);

                    if (transactionID == UUID.Zero)
                    {
                        InventoryItemBase item = NewInventoryItem(invPacket.InventoryData[i]);

                        OpenSimComms.InventoryItemOperation(uri, item, InventoryItemCallBack);
                        // Let's confirm, so we get the asset id of the new asset that that the server created
                        // so we can add it to the local cache
                    }
                    else
                    {
                        Console.WriteLine("[UserInventory]: UpdateItem with non-zero transactionID " + transactionID);
                        if (assetReceivers.ContainsKey(transactionID))
                        {
                            AssetReceiver assReceiver = assetReceivers[transactionID];
                            lock (assetReceivers)
                                assetReceivers.Remove(transactionID);

                            InventoryItemBase item = NewInventoryItem(invPacket.InventoryData[i], assReceiver.m_asset.FullID);

                            OpenSimComms.InventoryItemOperation(uri, item, InventoryItemCreateCallBack);
                            lock (invCallbackNumbers)
                            {
                                if (!invCallbackNumbers.ContainsKey(item.ID))
                                    invCallbackNumbers.Add(item.ID, invPacket.InventoryData[i].CallbackID);
                            }

                        }
                    }
                }
            }
            return true;
        }

        public bool RemoveInventoryItem(RemoveInventoryItemPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/DeleteItem/";
                foreach (RemoveInventoryItemPacket.InventoryDataBlock datablock in invPacket.InventoryData)
                {
                    InventoryItemBase item = new InventoryItemBase();
                    item.ID = datablock.ItemID;
                    item.Owner = UserID;
                    OpenSimComms.InventoryItemOperation(uri, item, InventoryItemCallBack);
                    RemoveFromCache(item);
                }
            }
            return true;
        }

        public bool MoveInventoryItem(MoveInventoryItemPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/MoveItem/";
                foreach (MoveInventoryItemPacket.InventoryDataBlock datablock in invPacket.InventoryData)
                {
                    InventoryItemBase item = new InventoryItemBase();
                    item.ID = datablock.ItemID;
                    item.Folder = datablock.FolderID;
                    item.Name = Util.FieldToString(datablock.NewName);
                    item.Owner = UserID;
                    OpenSimComms.InventoryItemOperation(uri, item, InventoryItemCreateCallBack);
                }
            }
            return true;
        }

        public bool CopyInventoryItem(CopyInventoryItemPacket invPacket)
        {
            if (invPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            if (connected)
            {
                string uri = InventoryServerURL + "/" + AuthToken + "/CopyItem/";
                Console.WriteLine(" >> CopyItem length " + invPacket.InventoryData.Length);
                foreach (CopyInventoryItemPacket.InventoryDataBlock datablock in invPacket.InventoryData)
                {
                    InventoryItemBase item = new InventoryItemBase();
                    item.Owner = datablock.OldAgentID;
                    item.ID = datablock.OldItemID;
                    // Warning: BIG HACK HERE, so that we can get this UUID back to fetch the callbakID
                    item.AssetID = UUID.Random();
                    item.Folder = datablock.NewFolderID;
                    item.Name = Util.FieldToString(datablock.NewName);
                    lock (invCallbackNumbers)
                    {
                        if (!invCallbackNumbers.ContainsKey(item.ID))
                            invCallbackNumbers.Add(item.AssetID, datablock.CallbackID); 
                    }
                    OpenSimComms.InventoryItemOperation(uri, item, InventoryItemCreateCallBack);
                }
            }
            return true;
        }

        void InventoryItemCallBack(InventoryItemBase item)
        {
            if (item.ID.Equals(UUID.Zero))
                Console.WriteLine("[UserInventory]: Inventory item operation was unsuccessful");
            else
            {
                Console.WriteLine("[UserInventory]: Inventory item operation succeeded");
                AddToCache(item);
            }
        }

        void InventoryItemCreateCallBack(InventoryItemBase item)
        {
            uint callbackID = 0;
            lock (invCallbackNumbers)
            {
                if (invCallbackNumbers.TryGetValue(item.ID, out callbackID))
                    invCallbackNumbers.Remove(item.ID);
            }
            AddToCache(item);

            const uint FULL_MASK_PERMISSIONS = (uint)PermissionMask.All;

            UpdateCreateInventoryItemPacket InventoryReply
                = (UpdateCreateInventoryItemPacket)PacketPool.Instance.GetPacket(
                                                       PacketType.UpdateCreateInventoryItem);

            // TODO: don't create new blocks if recycling an old packet
            InventoryReply.AgentData.AgentID = UserID;
            InventoryReply.AgentData.SimApproved = true;
            InventoryReply.InventoryData = new UpdateCreateInventoryItemPacket.InventoryDataBlock[1];
            InventoryReply.InventoryData[0] = new UpdateCreateInventoryItemPacket.InventoryDataBlock();
            InventoryReply.InventoryData[0].ItemID = item.ID;
            InventoryReply.InventoryData[0].AssetID = item.AssetID;
            InventoryReply.InventoryData[0].CreatorID = item.CreatorIdAsUuid;
            InventoryReply.InventoryData[0].BaseMask = item.BasePermissions;
            InventoryReply.InventoryData[0].Description = StringToPacketBytes(item.Description);
            InventoryReply.InventoryData[0].EveryoneMask = item.EveryOnePermissions;
            InventoryReply.InventoryData[0].FolderID = item.Folder;
            InventoryReply.InventoryData[0].InvType = (sbyte)item.InvType;
            InventoryReply.InventoryData[0].Name = StringToPacketBytes(item.Name);
            InventoryReply.InventoryData[0].NextOwnerMask = item.NextPermissions;
            InventoryReply.InventoryData[0].OwnerID = item.Owner;
            InventoryReply.InventoryData[0].OwnerMask = item.CurrentPermissions;
            InventoryReply.InventoryData[0].Type = (sbyte)item.AssetType;
            InventoryReply.InventoryData[0].CallbackID = callbackID;

            InventoryReply.InventoryData[0].GroupID = item.GroupID;
            InventoryReply.InventoryData[0].GroupOwned = item.GroupOwned;
            InventoryReply.InventoryData[0].GroupMask = item.GroupPermissions;
            InventoryReply.InventoryData[0].Flags = item.Flags;
            InventoryReply.InventoryData[0].SalePrice = item.SalePrice;
            InventoryReply.InventoryData[0].SaleType = item.SaleType;
            InventoryReply.InventoryData[0].CreationDate = item.CreationDate;

            InventoryReply.InventoryData[0].CRC =
                Helpers.InventoryCRC(1000, 0, InventoryReply.InventoryData[0].InvType,
                                     InventoryReply.InventoryData[0].Type, InventoryReply.InventoryData[0].AssetID,
                                     InventoryReply.InventoryData[0].GroupID, 100,
                                     InventoryReply.InventoryData[0].OwnerID, InventoryReply.InventoryData[0].CreatorID,
                                     InventoryReply.InventoryData[0].ItemID, InventoryReply.InventoryData[0].FolderID,
                                     FULL_MASK_PERMISSIONS, 1, FULL_MASK_PERMISSIONS, FULL_MASK_PERMISSIONS,
                                     FULL_MASK_PERMISSIONS);
            InventoryReply.Header.Zerocoded = true;
            //OutPacket(InventoryReply, ThrottleOutPacketType.Asset);
            proxy.InjectPacket(InventoryReply, Direction.Incoming);
        }

        #endregion


        #region Textures

        public bool GetImage(RequestImagePacket imgPacket)
        {
            if (imgPacket.AgentData.AgentID.Equals(libOwner))
                // Tell grid surfer to forward request to region, because the Library is served from there (!)
                return false;

            RequestImagePacket.RequestImageBlock[] invImgBlocks = new RequestImagePacket.RequestImageBlock[imgPacket.RequestImage.Length];
            int count = 0;
            for (int i = 0; i < imgPacket.RequestImage.Length; i++)
            {
                //Console.WriteLine("  >> Requested image " + imgPacket.RequestImage[i].Image);
                if (IsInventoryAsset(imgPacket.RequestImage[i].Image))
                {
                    if (connected)
                    {
                        invImgBlocks[i] = imgPacket.RequestImage[i];
                        InventoryItemBase item = GetInventoryItem(imgPacket.RequestImage[i].Image);

                        if (item != null)
                        {
                            lock (textureSenders)
                            {
                                if (!textureSenders.ContainsKey(imgPacket.RequestImage[i].Image))
                                {
                                    lock (textureSenders)
                                        textureSenders.Add(imgPacket.RequestImage[i].Image, new TextureSender(proxy, imgPacket.RequestImage[i].DiscardLevel, imgPacket.RequestImage[i].Packet));
                                    assDownloader.RequestAsset(item, true);
                                }
                            }
                        }
                        count++;
                        Console.WriteLine("  >> Image is inventory item");
                    }
                }
                else
                    invImgBlocks[i] = null;
            }

            if (count == imgPacket.RequestImage.Length)
            {
                // They were all inventory images
                Console.WriteLine("  >> All images were inventory items");
                return true;
            }
            else if (count == 0)
            {
                // None of them were inventory items
                //Console.WriteLine("  >> No images were inventory items");
                return false;
            }
            else
            {
                RequestImagePacket.RequestImageBlock[] iblocks = new RequestImagePacket.RequestImageBlock[imgPacket.RequestImage.Length - count];
                int j = 0;
                for (int i = 0; i < invImgBlocks.Length; i++)
                {
                    if (invImgBlocks[i] != null)
                        iblocks[j++] = imgPacket.RequestImage[i];
                }

                imgPacket.RequestImage = iblocks;
                Console.WriteLine("  >> Some images were inventory items " + (imgPacket.RequestImage.Length - count));

                // Forward this altered packet
                return false;
            }
        }

        #endregion

        #region Assets


        public bool GetAsset(TransferRequestPacket assetReqPacket)
        {
            UUID assetID = UUID.Zero;
            byte source = 2;
            if (assetReqPacket.TransferInfo.SourceType == 2)
            {
                //direct asset request
                assetID = new UUID(assetReqPacket.TransferInfo.Params, 0);
            }
            else if (assetReqPacket.TransferInfo.SourceType == 3)
            {
                //inventory asset request
                UUID taskID = new UUID(assetReqPacket.TransferInfo.Params, 48);
                UUID itemID = new UUID(assetReqPacket.TransferInfo.Params, 64);
                UUID requestID = new UUID(assetReqPacket.TransferInfo.Params, 80);
                assetID = new UUID(assetReqPacket.TransferInfo.Params, 80);
                source = 3;

                //m_log.Debug("asset request " + requestID);
                if (taskID == UUID.Zero) // User inventory
                {
                    InventoryItemBase item = GetInventoryItem(assetID);
                    if (item == null)
                    {
                        // We didn't know about this asset, but now we do.
                        // Probably created via CAPs update
                        item = new InventoryItemBase();
                        item.ID = itemID;
                        item.AssetID = assetID;
                        item.Owner = UserID;
                        AddToCache(item);
                    }
                    Console.WriteLine("[UserInventory]: Requested asset is inventory asset");
                    if (connected)
                    {
                        AssetSender sender = new AssetSender(proxy, assetID, assetReqPacket.TransferInfo.TransferID, assetReqPacket.TransferInfo.Params, source);
                        lock (assetSenders)
                            assetSenders.Add(assetID, sender);
                        assDownloader.RequestAsset(item, false);
                        return true;
                    }
                   
                }
            }

            Console.WriteLine("[UserInventory]: Requested asset is not in inventory");
            return false;
        }

        public bool PostAsset(AssetUploadRequestPacket assUpload, UUID secureSessionID)
        {
            if (connected)
            {
                UUID assetID = UUID.Combine(assUpload.AssetBlock.TransactionID, secureSessionID);
                Console.WriteLine(" ------- RequestTransfer -----");
                Console.WriteLine("assetID=" + assetID + "; data.length=" + assUpload.AssetBlock.AssetData.Length + "; local=" + assUpload.AssetBlock.StoreLocal
                    + "; temp=" + assUpload.AssetBlock.Tempfile);

                if (!assUpload.AssetBlock.StoreLocal)
                {
                    Console.WriteLine("[UserInventory]: AssetUploadRequest to inventory.");
                    AssetReceiver assReceiver = new AssetReceiver(proxy, assetID, assUpload.AssetBlock.TransactionID);
                    lock (assetReceivers)
                        assetReceivers.Add(assUpload.AssetBlock.TransactionID, assReceiver);
                    string uri = InventoryServerURL + "/" + AuthToken + "/PostAsset/";
                    assReceiver.ReceiveAndPostAsset(assUpload.AssetBlock.Type, assUpload.AssetBlock.AssetData,
                                                    assUpload.AssetBlock.StoreLocal, assUpload.AssetBlock.Tempfile, uri);
                    return true;
                }
                // Else, StoreLocal=true, seems to be an asset upload for the sim
                Console.WriteLine("[UserInventory]: Forwarding AssetUploadRequest to simulator.");
            }
            return false;
        }

        public bool XferAsset(SendXferPacketPacket xfer)
        {
            if (connected)
            {
                AssetReceiver assReceiver = null;
                lock (assetReceivers)
                    foreach (AssetReceiver assr in assetReceivers.Values)
                        if (xfer.XferID.ID == assr.XferID)
                            assReceiver = assr;
                if (assReceiver == null)
                {
                    //Console.WriteLine("[UserInventory]: Could not find asser receiver for Xfer " + xfer.XferID.ID);
                    return false;
                }
                assReceiver.GetDataPacket(xfer.XferID.Packet, xfer.DataPacket.Data);
                if ((xfer.XferID.Packet & 0x80000000) != 0) // last one, it seems
                {
                    UUID assRecvrID = assReceiver.transferID;
                    lock (assetReceivers)
                        assetReceivers.Remove(assRecvrID);
                    Console.WriteLine("[UserInventory]: Asset Xfer finished, removed asset receiver");
                }
                return true;
            }
            return false;
        }

        private AssetBase NewAsset(string name, string description, sbyte assetType, byte[] data)
        {
            AssetBase asset = new AssetBase();
            asset.Name = name;
            asset.Description = description;
            asset.Type = assetType;
            asset.FullID = UUID.Random();
            asset.ID = asset.FullID.ToString();
            asset.Data = (data == null) ? new byte[1] : data;

            return asset;
        }

        #endregion Assets

        #region Cache

        void AddToCache(InventoryItemBase item)
        {
            string id = item.AssetID.ToString();
            //Console.WriteLine("   >> Adding to cache " + id + " - " + item.ID + " - " + item.Name);
            if (!inventoryItems.ContainsKey(id[0]))
            {
                List<InventoryAsset> list = new List<InventoryAsset>();
                list.Add(new InventoryAsset(item.ID, item.AssetID));
                inventoryItems.Add(id[0], list);
            }
            else
            {
                // Note that we're not checking for repetitions. That's too slow.
                // We assume the calling context does the right thing and doesn't put
                // repeated items in the cache.
                List<InventoryAsset> list = inventoryItems[id[0]];
                list.Add(new InventoryAsset(item.ID, item.AssetID));
            }
        }

        void RemoveFromCache(InventoryItemBase item)
        {
            string id = item.AssetID.ToString();
            if (inventoryItems.ContainsKey(id[0]))
            {
                Console.WriteLine("   >> Removing from cache " + item.ID);
                // Note that we're not checking for repetitions. That's too slow.
                // We assume the calling context does the right thing and doesn't put
                // repeated items in the cache.
                List<InventoryAsset> list = inventoryItems[id[0]];
                list.RemoveAll(delegate (InventoryAsset ia) { return ia.itemID == item.ID; });
            }
        }

        bool IsInventoryAsset(UUID id)
        {
            char key = id.ToString()[0];
            if (inventoryItems.ContainsKey(key))
            {
                List<InventoryAsset> list = inventoryItems[key];
                foreach (InventoryAsset iass in list)
                    if (iass.assetID == id)
                        return true;
            }
            return false;
        }

        InventoryItemBase GetInventoryItem(UUID assetID)
        {
            InventoryItemBase item = new InventoryItemBase();
            char key = assetID.ToString()[0];
            if (inventoryItems.ContainsKey(key))
            {
                List<InventoryAsset> list = inventoryItems[key];
                foreach (InventoryAsset iass in list)
                    if (iass.assetID == assetID)
                    {
                        item.ID = iass.itemID;
                        item.AssetID = iass.assetID;
                        item.Owner = UserID;
                        return item;
                    }
            }
            return null;
        }

        #endregion Cache

        #region IAssetReceiver
        public void AssetReceived(AssetBase asset, bool isTexture)
        {
            Console.WriteLine("[UserInventory]: Asset received " + asset.FullID);
            if (isTexture)
            {
                TextureSender sender = null;
                lock (textureSenders)
                {
                    if (textureSenders.TryGetValue(asset.FullID, out sender))
                        textureSenders.Remove(asset.FullID);
                    else
                        Console.WriteLine("[UserInventory]: received texture but there is no texture sender!");
                }
                if (sender != null)
                    sender.TextureReceived(asset);
            }
            else
            {
                AssetSender sender = null;
                lock (assetSenders)
                {
                    if (assetSenders.TryGetValue(asset.FullID, out sender))
                        assetSenders.Remove(asset.FullID);
                    else
                        Console.WriteLine("[UserInventory]: received asset but there is no asset sender!");
                }
                if (sender != null)
                    sender.AssetReceived(asset);
            }
        }

        public void AssetNotFound(UUID assetID, bool IsTexture)
        {
            Console.WriteLine("[UserInventory]: Asset not found " + assetID);
            if (IsTexture)
            {
                TextureSender sender = null;
                lock (textureSenders)
                {
                    if (textureSenders.TryGetValue(assetID, out sender))
                        textureSenders.Remove(assetID);
                    else
                        Console.WriteLine("[UserInventory]: rceived texture callback but there is no texture sender!");
                }
                if (sender != null)
                    sender.TextureNotFound(assetID);
            }
            else
            {
                AssetSender sender = null;
                lock (assetSenders)
                {
                    if (assetSenders.TryGetValue(assetID, out sender))
                        textureSenders.Remove(assetID);
                    else
                        Console.WriteLine("[UserInventory]: rceived asset callback but there is no asset sender!");
                }
                //if (sender != null)
                //    sender.AssetNotFound(assetID);
            }
        }

        #endregion IAssetReceiver

        #region misc

        public static byte[] StringToPacketBytes(string s)
        {
            // Anything more than 254 will cause libsecondlife to barf
            // (libsl 1550) adds an \0 on the Utils.StringToBytes conversion if it isn't present
            if (s.Length > 254)
            {
                s = s.Remove(254);
            }

            return Utils.StringToBytes(s);
        }

        private void AddNullFolderBlockToDecendentsPacket(ref InventoryDescendentsPacket packet)
        {
            packet.FolderData = new InventoryDescendentsPacket.FolderDataBlock[1];
            packet.FolderData[0] = new InventoryDescendentsPacket.FolderDataBlock();
            packet.FolderData[0].FolderID = UUID.Zero;
            packet.FolderData[0].ParentID = UUID.Zero;
            packet.FolderData[0].Type = -1;
            packet.FolderData[0].Name = new byte[0];
        }

        private void AddNullItemBlockToDescendentsPacket(ref InventoryDescendentsPacket packet)
        {
            packet.ItemData = new InventoryDescendentsPacket.ItemDataBlock[1];
            packet.ItemData[0] = new InventoryDescendentsPacket.ItemDataBlock();
            packet.ItemData[0].ItemID = UUID.Zero;
            packet.ItemData[0].AssetID = UUID.Zero;
            packet.ItemData[0].CreatorID = UUID.Zero;
            packet.ItemData[0].BaseMask = 0;
            packet.ItemData[0].Description = new byte[0];
            packet.ItemData[0].EveryoneMask = 0;
            packet.ItemData[0].OwnerMask = 0;
            packet.ItemData[0].FolderID = UUID.Zero;
            packet.ItemData[0].InvType = (sbyte)0;
            packet.ItemData[0].Name = new byte[0];
            packet.ItemData[0].NextOwnerMask = 0;
            packet.ItemData[0].OwnerID = UUID.Zero;
            packet.ItemData[0].Type = -1;

            packet.ItemData[0].GroupID = UUID.Zero;
            packet.ItemData[0].GroupOwned = false;
            packet.ItemData[0].GroupMask = 0;
            packet.ItemData[0].CreationDate = 0;
            packet.ItemData[0].SalePrice = 0;
            packet.ItemData[0].SaleType = 0;
            packet.ItemData[0].Flags = 0;

            // No need to add CRC
        }

        public void NullCallBack(bool result)
        {
        }

        public void OKCallBack(bool result)
        {
            AlertMessagePacket message = (AlertMessagePacket)PacketPool.Instance.GetPacket(PacketType.AlertMessage);
            message.AlertData.Message = Utils.StringToBytes("Inventory operation succeeded");
            proxy.InjectPacket(message, Direction.Incoming);
        }


        #endregion
    }

    class InventoryAsset
    {
        public UUID itemID;
        public UUID assetID;

        public InventoryAsset(UUID item, UUID asset)
        {
            itemID = item;
            assetID = asset;
        }
    }
}
