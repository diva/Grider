using System;
using System.Collections.Generic;

using OpenSim.Framework;

using OpenMetaverse;
using OpenMetaverse.Packets;

using OpenSim.Framework.Communications.Cache;

namespace Grider
{
    public class RegionClient : IAssetReceiver
    {
        GriderProxy proxy;
        string RegionURL;
        string AuthToken;
        GridAssetClient assClient;
        Dictionary<UUID, TextureSender> textureSenders = new Dictionary<UUID, TextureSender>();
        Dictionary<UUID, AssetSender> assetSenders = new Dictionary<UUID, AssetSender>();
        Dictionary<UUID, AssetReceiver> assetReceivers = new Dictionary<UUID, AssetReceiver>();

        public RegionClient(GriderProxy p, string regionurl, string auth)
        {
            proxy = p;
            RegionURL = regionurl;
            AuthToken = auth;
            //assDownloader = new AssetDownloader(RegionURL, this);
            assClient = new GridAssetClient(RegionURL);
            assClient.SetReceiver(this);
        }

        public bool GetImage(RequestImagePacket imgPacket)
        {
            RequestImagePacket.RequestImageBlock[] invImgBlocks = new RequestImagePacket.RequestImageBlock[imgPacket.RequestImage.Length];
            for (int i = 0; i < imgPacket.RequestImage.Length; i++)
            {
                invImgBlocks[i] = imgPacket.RequestImage[i];

                lock (textureSenders)
                {
                    if (!textureSenders.ContainsKey(imgPacket.RequestImage[i].Image))
                    {
                        lock (textureSenders)
                            textureSenders.Add(imgPacket.RequestImage[i].Image, new TextureSender(proxy, imgPacket.RequestImage[i].DiscardLevel, imgPacket.RequestImage[i].Packet));
                        assClient.RequestAsset(imgPacket.RequestImage[i].Image, true);
                        //assDownloader.RequestAsset(imgPacket.RequestImage[i].Image, true, AuthToken);
                    }
                }
                Console.WriteLine("  >> Image is region asset");
            }

            // Don't forward
            return true;
        }

        #region IAssetReceiver

        public void AssetReceived(AssetBase asset, bool isTexture)
        {
            Console.WriteLine("[RegionClient]: Asset received " + asset.FullID);
            if (isTexture)
            {
                TextureSender sender = null;
                lock (textureSenders)
                {
                    if (textureSenders.TryGetValue(asset.FullID, out sender))
                        textureSenders.Remove(asset.FullID);
                    else
                        Console.WriteLine("[RegionClient]: received texture but there is no texture sender!");
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
                        Console.WriteLine("[RegionClient]: received asset but there is no asset sender!");
                }
                if (sender != null)
                    sender.AssetReceived(asset);
            }
        }

        public void AssetNotFound(UUID assetID, bool IsTexture)
        {
            Console.WriteLine("[RegionClient]: Asset not found " + assetID);
            if (IsTexture)
            {
                TextureSender sender = null;
                lock (textureSenders)
                {
                    if (textureSenders.TryGetValue(assetID, out sender))
                        textureSenders.Remove(assetID);
                    else
                        Console.WriteLine("[RegionClient]: rceived texture callback but there is no texture sender!");
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
                        Console.WriteLine("[RegionClient]: received asset callback but there is no asset sender!");
                }
                //if (sender != null)
                //    sender.AssetNotFound(assetID);
            }
        }

        #endregion IAssetReceiver

    }
}
