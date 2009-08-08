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
using System.Collections.Generic;
using System.IO;
using System.Threading;

using OpenSim.Framework;
using OpenMetaverse;
using OpenSimLibComms;

namespace Grider
{
    public class AssetDownloader
    {
        string URL;
        IAssetReceiver receiver;

        Dictionary<UUID, bool> pendingRequests = new Dictionary<UUID, bool>();

        public AssetDownloader(string url, IAssetReceiver rcvr)
        {
            URL = url;
            receiver = rcvr;
        }

        public void RequestAsset(InventoryItemBase item, bool isTexture)
        {
            bool fetch = true;
            lock (pendingRequests)
            {
                if (!pendingRequests.ContainsKey(item.AssetID))
                {
                    pendingRequests.Add(item.AssetID, isTexture);
                    Console.WriteLine("    >>> RequestAsset Added " + item.AssetID);
                }
                else
                    fetch = false;
            }

            if (fetch)
            {
                string uri = URL + "/GetAsset/" + item.ID + "/";
                if (!OpenSimComms.RequestAsset(uri, item, AssetCallback))
                    receiver.AssetNotFound(item.AssetID, isTexture);
            }
        }

        public void RequestAsset(UUID assetID, bool isTexture, string AuthToken)
        {
            //bool fetch = true;
            //lock (pendingRequests)
            //{
            //    if (!pendingRequests.ContainsKey(assetID))
            //    {
            //        pendingRequests.Add(assetID, isTexture);
            //        Console.WriteLine("    >>> RequestAsset Added " + assetID);
            //    }
            //    else
            //        fetch = false;
            //}

            //if (fetch)
            //{
            //    string uri = URL + "/assets/" + assetID + "/";
            //    if (!OpenSimComms.RequestAsset(uri, item, AssetCallback))
            //        receiver.AssetNotFound(assetID, isTexture);
            //}
        }

        void AssetCallback(AssetBase asset)
        {
            if (asset == null)
            {
                Console.WriteLine("[AssetDownloader]: Received null asset");
                return;
            }

            Console.WriteLine("[AssetDownloader]: Received asset " + asset.FullID);
            UUID assetID = UUID.Zero;
            bool isTexture = false;

            lock (pendingRequests)
            {
                if (pendingRequests.ContainsKey(asset.FullID))
                {
                    assetID = asset.FullID;
                    isTexture = pendingRequests[asset.FullID];
                    pendingRequests.Remove(assetID);
                }
            }

            if (asset.Data == null)
            {
                receiver.AssetNotFound(asset.FullID, isTexture);
                return;
            }

            if ((asset != null) && !assetID.Equals(UUID.Zero))
                receiver.AssetReceived(asset, isTexture);
            else
            {
                Console.WriteLine("[AssetDownloader]: Received asset but couldn't find request");
                receiver.AssetNotFound(asset.FullID, false);
            }
        }
    }
}
