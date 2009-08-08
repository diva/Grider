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

using OpenMetaverse;
using OpenMetaverse.Packets;
using OpenSim.Framework;

namespace Grider
{
    public class AssetSender
    {
        private GriderProxy proxy;
        UUID assetID;
        UUID transferID;
        byte[] Params;
        byte source;

        public AssetSender(GriderProxy _proxy, UUID _assetID, UUID _transferID, byte[] _params, byte _source)
        {
            proxy = _proxy;
            assetID = _assetID;
            transferID = _transferID;
            Params = _params;
            source = _source;
        }

        public void AssetReceived(AssetBase asset)
        {
            //m_log.Debug("sending asset " + req.RequestAssetID);
            TransferInfoPacket Transfer = new TransferInfoPacket();
            Transfer.TransferInfo.ChannelType = 2;
            Transfer.TransferInfo.Status = 0;
            Transfer.TransferInfo.TargetType = 0;
            if (source == 2)
            {
                Transfer.TransferInfo.Params = new byte[20];
                Array.Copy(assetID.GetBytes(), 0, Transfer.TransferInfo.Params, 0, 16);
                int assType = asset.Type;
                Array.Copy(Utils.IntToBytes(assType), 0, Transfer.TransferInfo.Params, 16, 4);
            }
            else if (source == 3)
            {
                Transfer.TransferInfo.Params = Params;
            }

            Transfer.TransferInfo.Size = asset.Data.Length; //req.AssetInf.Data.Length;
            Transfer.TransferInfo.TransferID = transferID;
            Transfer.Header.Zerocoded = true;
            //OutPacket(Transfer, ThrottleOutPacketType.Asset);
            proxy.InjectPacket(Transfer, Direction.Incoming);

            int npackets = CalculateNumPackets(asset.Data);

            if (npackets == 1)
            {
                TransferPacketPacket TransferPacket = new TransferPacketPacket();
                TransferPacket.TransferData.Packet = 0;
                TransferPacket.TransferData.ChannelType = 2;
                TransferPacket.TransferData.TransferID = transferID;
                TransferPacket.TransferData.Data = asset.Data;
                TransferPacket.TransferData.Status = 1;
                TransferPacket.Header.Zerocoded = true;
                //OutPacket(TransferPacket, ThrottleOutPacketType.Asset);
                proxy.InjectPacket(TransferPacket, Direction.Incoming);
            }
            else
            {
                int processedLength = 0;
                int maxChunkSize = Settings.MAX_PACKET_SIZE - 100;
                int packetNumber = 0;

                while (processedLength < asset.Data.Length)
                {
                    TransferPacketPacket TransferPacket = new TransferPacketPacket();
                    TransferPacket.TransferData.Packet = packetNumber;
                    TransferPacket.TransferData.ChannelType = 2;
                    TransferPacket.TransferData.TransferID = transferID;

                    int chunkSize = Math.Min(asset.Data.Length - processedLength, maxChunkSize);
                    byte[] chunk = new byte[chunkSize];
                    Array.Copy(asset.Data, processedLength, chunk, 0, chunk.Length);

                    TransferPacket.TransferData.Data = chunk;

                    // 0 indicates more packets to come, 1 indicates last packet
                    if (asset.Data.Length - processedLength > maxChunkSize)
                    {
                        TransferPacket.TransferData.Status = 0;
                    }
                    else
                    {
                        TransferPacket.TransferData.Status = 1;
                    }
                    TransferPacket.Header.Zerocoded = true;
                    //OutPacket(TransferPacket, ThrottleOutPacketType.Asset);
                    proxy.InjectPacket(TransferPacket, Direction.Incoming);

                    processedLength += chunkSize;
                    packetNumber++;
                }
            }
        }

        private static int CalculateNumPackets(byte[] data)
        {
            const uint m_maxPacketSize = 600;
            int numPackets = 1;

            if (data.LongLength > m_maxPacketSize)
            {
                // over max number of bytes so split up file
                long restData = data.LongLength - m_maxPacketSize;
                int restPackets = (int)((restData + m_maxPacketSize - 1) / m_maxPacketSize);
                numPackets += restPackets;
            }

            return numPackets;
        }

    }
}
