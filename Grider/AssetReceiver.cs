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

using OpenSimLibComms;

namespace Grider
{
    public class AssetReceiver
    {
        private GriderProxy proxy;
        public UUID assetID;
        public UUID transferID;
        public AssetBase m_asset;
        string uri;
        public ulong XferID;

        public AssetReceiver(GriderProxy _proxy, UUID _id, UUID _transfer)
        {
            proxy = _proxy;
            assetID = _id;
            transferID = _transfer;
        }

        public void ReceiveAndPostAsset(sbyte type, byte[] data, bool storeLocal, bool temp, string _uri)
        {
            m_asset = new AssetBase();
            m_asset.FullID = assetID;
            m_asset.Type = type;
            m_asset.Data = data;
            m_asset.Name = "blank";
            m_asset.Description = "empty";
            m_asset.Local = storeLocal;
            m_asset.Temporary = temp;

            uri = _uri;

            if (m_asset.Data.Length > 2)
            {
                CompleteUpload();
            }
            else
            {
                RequestStartXfer();
            }

        }

        protected void CompleteUpload()
        {
            Console.WriteLine("[AssetReceiver]: Uploaded asset data for transaction " + transferID);

            OpenSimComms.CreateAsset(uri, m_asset, AssetPostCallback);
        }

        public void AssetPostCallback(bool result)
        {
            AssetUploadCompletePacket newPack = new AssetUploadCompletePacket();
            newPack.AssetBlock.Type = m_asset.Type;
            newPack.AssetBlock.Success = result;
            newPack.AssetBlock.UUID = assetID;
            newPack.Header.Zerocoded = true;
            //OutPacket(newPack, ThrottleOutPacketType.Asset);
            proxy.InjectPacket(newPack, Direction.Incoming);
        }

        protected void RequestStartXfer()
        {
            Console.WriteLine("[AssetReceiver]: RequestStartXfer for transaction " + transferID);

            XferID = Util.GetNextXferID();
            RequestXferPacket newPack = new RequestXferPacket();
            newPack.XferID.ID = XferID;
            newPack.XferID.VFileType = m_asset.Type;
            newPack.XferID.VFileID = assetID;
            newPack.XferID.FilePath = 0;
            newPack.XferID.Filename = new byte[0];
            newPack.Header.Zerocoded = true;
            //OutPacket(newPack, ThrottleOutPacketType.Asset);
            proxy.InjectPacket(newPack, Direction.Incoming);

        }

        public void GetDataPacket(uint packetId, byte[] data)
        {
            if (m_asset.Data.Length > 1)
            {
                byte[] destinationArray = new byte[m_asset.Data.Length + data.Length];
                Array.Copy(m_asset.Data, 0, destinationArray, 0, m_asset.Data.Length);
                Array.Copy(data, 0, destinationArray, m_asset.Data.Length, data.Length);
                m_asset.Data = destinationArray;
            }
            else
            {
                byte[] buffer2 = new byte[data.Length - 4];
                Array.Copy(data, 4, buffer2, 0, data.Length - 4);
                m_asset.Data = buffer2;
            }

            SendConfirmXfer(packetId);

            if ((packetId & 0x80000000) != 0)
            {
                CompleteUpload();
            }
        }

        protected void SendConfirmXfer(uint PacketID)
        {
            ConfirmXferPacketPacket newPack = new ConfirmXferPacketPacket();
            newPack.XferID.ID = XferID;
            newPack.XferID.Packet = PacketID;
            newPack.Header.Zerocoded = true;
            //OutPacket(newPack, ThrottleOutPacketType.Asset);
            proxy.InjectPacket(newPack, Direction.Incoming);
        }
 
    }
}
