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
using System.Reflection;
using OpenSim.Framework;

using OpenMetaverse;
using OpenMetaverse.Packets;

namespace Grider
{
    /// <summary>
    /// A TextureSender handles the process of receiving a texture requested by the client from the
    /// AssetCache, and then sending that texture back to the client.
    /// </summary>
    public class TextureSender 
    {
        /// <summary>
        /// Records the number of times texture send has been called.
        /// </summary>
        public int counter = 0;

        public bool ImageLoaded = false;

        /// <summary>
        /// Holds the texture asset to send.
        /// </summary>
        private AssetBase m_asset;


        /// <summary>
        /// This is actually the number of extra packets required to send the texture data!  We always assume
        /// at least one is required.
        /// </summary>
        private int NumPackets = 0;

        /// <summary>
        /// Holds the packet number to send next.  In this case, each packet is 1000 bytes long and starts
        /// at the 600th byte (0th indexed).
        /// </summary>
        private int PacketCounter = 0;

        private int RequestedDiscardLevel = -1;
        private GriderProxy proxy;
        private uint StartPacketNumber = 0;

        public TextureSender(GriderProxy _proxy, int discardLevel, uint packetNumber)
        {
            proxy = _proxy;
            RequestedDiscardLevel = discardLevel;
            StartPacketNumber = packetNumber;
        }

        /// <summary>
        /// Load up the texture data to send.
        /// </summary>
        /// <param name="asset"></param>
        public void TextureReceived(AssetBase asset)
        {
            m_asset = asset;
            NumPackets = CalculateNumPackets(asset.Data.Length);
            PacketCounter = (int)StartPacketNumber;
            ImageLoaded = true;
            SendTexture();
        }

        public void TextureNotFound(UUID uuid)
        {
            SendImageNotFound(uuid);
        }

        private void SendTexture()
        {
            //m_log.DebugFormat("[TEXTURE SENDER]: Sending packet for {0}", m_asset.FullID);
            while (true)
            {
                SendPacket();
                counter++;
                if ((NumPackets == 0) || (RequestedDiscardLevel == -1) || (PacketCounter > NumPackets) ||
                    ((RequestedDiscardLevel > 0) && (counter > 50 + (NumPackets / (RequestedDiscardLevel + 1)))))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Sends a texture packet to the client.
        /// </summary>
        private void SendPacket()
        {
            if (PacketCounter <= NumPackets)
            {
                if (PacketCounter == 0)
                {
                    if (NumPackets == 0)
                    {
                        SendImageFirstPart(1, m_asset.FullID, (uint)m_asset.Data.Length, m_asset.Data, 2);
                        PacketCounter++;
                    }
                    else
                    {
                        byte[] ImageData1 = new byte[600];
                        Array.Copy(m_asset.Data, 0, ImageData1, 0, 600);

                        SendImageFirstPart(
                            (ushort)(NumPackets), m_asset.FullID, (uint)m_asset.Data.Length, ImageData1, 2);
                        PacketCounter++;
                    }
                }
                else
                {
                    int size = m_asset.Data.Length - 600 - (1000 * (PacketCounter - 1));
                    if (size > 1000) size = 1000;
                    byte[] imageData = new byte[size];
                    try
                    {
                        Array.Copy(m_asset.Data, 600 + (1000 * (PacketCounter - 1)), imageData, 0, size);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        Console.WriteLine("[TEXTURE SENDER]: Unable to separate texture into multiple packets: Array bounds failure on asset:" +
                                    m_asset.ID);
                        return;
                    }

                    SendImageNextPart((ushort)PacketCounter, m_asset.FullID, imageData);
                    PacketCounter++;
                }
            }
        }

        /// <summary>
        /// Calculate the number of packets that will be required to send the texture loaded into this sender
        /// This is actually the number of 1000 byte packets not including an initial 600 byte packet...
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        private int CalculateNumPackets(int length)
        {
            int numPackets = 0;

            if (length > 600)
            {
                //over 600 bytes so split up file
                int restData = (length - 600);
                int restPackets = ((restData + 999) / 1000);
                numPackets = restPackets;
            }

            return numPackets;
        }

        public void SendImageFirstPart(ushort numParts, UUID ImageUUID, uint ImageSize, byte[] ImageData, byte imageCodec)
        {
            ImageDataPacket im = new ImageDataPacket();
            im.Header.Reliable = true;
            im.ImageID.Packets = numParts;
            im.ImageID.ID = ImageUUID;

            if (ImageSize > 0)
                im.ImageID.Size = ImageSize;

            im.ImageData.Data = ImageData;
            im.ImageID.Codec = imageCodec;
            im.Header.Zerocoded = true;
            //OutPacket(im, ThrottleOutPacketType.Texture);
            proxy.InjectPacket(im, Direction.Incoming);
        }

        public void SendImageNextPart(ushort partNumber, UUID imageUuid, byte[] imageData)
        {
            ImagePacketPacket im = new ImagePacketPacket();
            im.Header.Reliable = true;
            im.ImageID.Packet = partNumber;
            im.ImageID.ID = imageUuid;
            im.ImageData.Data = imageData;

            //OutPacket(im, ThrottleOutPacketType.Texture);
            proxy.InjectPacket(im, Direction.Incoming);
        }

        public void SendImageNotFound(UUID imageid)
        {
            ImageNotInDatabasePacket notFoundPacket
            = (ImageNotInDatabasePacket)PacketPool.Instance.GetPacket(PacketType.ImageNotInDatabase);

            notFoundPacket.ImageID.ID = imageid;

            //OutPacket(notFoundPacket, ThrottleOutPacketType.Texture);
            proxy.InjectPacket(notFoundPacket, Direction.Incoming);
        }


    }
}
