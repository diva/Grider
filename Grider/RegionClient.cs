using System;
using System.Collections.Generic;

using OpenSim.Framework;

using OpenMetaverse;
using OpenMetaverse.Packets;

using OpenSim.Framework.Communications.Cache;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Connectors;

namespace Grider
{
    public class RegionClient
    {
        GriderProxy proxy;
        string RegionURL;
        string AuthToken;
        IAssetService m_RegionAssetService;

        Dictionary<UUID, TextureSender> textureSenders = new Dictionary<UUID, TextureSender>();

        public RegionClient(GriderProxy p, string regionurl, string auth)
        {
            proxy = p;
            RegionURL = regionurl;
            AuthToken = auth;
            //assDownloader = new AssetDownloader(RegionURL, this);

            m_RegionAssetService = new AssetServicesConnector(RegionURL);
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
                        TextureSender sender = new TextureSender(proxy, imgPacket.RequestImage[i].DiscardLevel, imgPacket.RequestImage[i].Packet);
                        textureSenders.Add(imgPacket.RequestImage[i].Image, sender);
                        m_RegionAssetService.Get(imgPacket.RequestImage[i].Image.ToString(), sender, TextureReceived);
                        //old: assClient.RequestAsset(imgPacket.RequestImage[i].Image, true);
                        //older: assDownloader.RequestAsset(imgPacket.RequestImage[i].Image, true, AuthToken);
                    }
                }
                Console.WriteLine("  >> Image is region asset");
            }

            // Don't forward
            return true;
        }

        #region IAssetReceiver

        public void TextureReceived(string id, Object sender, AssetBase asset)
        {

            if ((sender != null) && (sender is TextureSender))
            {
                TextureSender tsender = (TextureSender) sender;
                lock (textureSenders)
                    textureSenders.Remove(new UUID(id));

                if (asset == null)
                {
                    Console.WriteLine("[RegionClient]: Texture not found " + id);
                    return;
                }

                Console.WriteLine("[RegionClient]: Texture received " + asset.FullID);
                tsender.TextureReceived(asset);
            }
            else
            {
                Console.WriteLine("[RegionClient]: Something wrong with texture sender for texture " + asset.FullID);
           }
        }

        #endregion IAssetReceiver

    }
}
