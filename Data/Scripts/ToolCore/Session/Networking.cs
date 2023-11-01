using ProtoBuf;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using ToolCore.Comp;
using ToolCore.Utils;

namespace ToolCore.Session
{
    internal class Networking
    {
        internal const ushort ServerPacketId = 65351;
        internal const ushort ClientPacketId = 65352;

        internal readonly ToolSession Session;

        internal Networking(ToolSession session)
        {
            Session = session;
        }

        public void SendPacketToServer(Packet packet)
        {
            var rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);
            MyModAPIHelper.MyMultiplayer.Static.SendMessageToServer(ServerPacketId, rawData, true);
        }

        public void SendPacketToClient(Packet packet, ulong client)
        {
            var rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);
            MyModAPIHelper.MyMultiplayer.Static.SendMessageTo(ClientPacketId, rawData, client, true);
        }

        public void SendPacketToClients(Packet packet, List<ulong> clients)
        {
            var rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);

            foreach (var client in clients)
                MyModAPIHelper.MyMultiplayer.Static.SendMessageTo(ClientPacketId, rawData, client, true);
        }

        internal void ProcessPacket(ushort id, byte[] rawData, ulong sender, bool reliable)
        {
            try
            {
                var packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(rawData);
                if (packet == null || packet.EntityId != 0 && !Session.ToolMap.ContainsKey(packet.EntityId))
                {
                    Logs.WriteLine($"Invalid packet - null:{packet == null}");
                    return;
                }

                var comp = packet.EntityId == 0 ? null : Session.ToolMap[packet.EntityId];
                switch ((PacketType)packet.Type)
                {
                    case PacketType.Update:
                        var uPacket = packet as UpdatePacket;
                        UpdateComp(uPacket, comp);
                        SendPacketToClients(uPacket, comp.ReplicatedClients);
                        break;
                    case PacketType.Replicate:
                        var rPacket = packet as ReplicationPacket;
                        if (rPacket.Add)
                            comp.ReplicatedClients.Add(sender);
                        else
                            comp.ReplicatedClients.Remove(sender);
                        break;
                    case PacketType.Settings:
                        var sPacket = packet as SettingsPacket;
                        //UpdateEnforcement(sPacket.Settings);
                        break;
                    default:
                        Logs.WriteLine($"Invalid packet type - {packet.GetType()}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }

        }

        internal void UpdateComp(UpdatePacket packet, ToolComp comp)
        {
            switch ((FieldType)packet.Field)
            {
                case FieldType.Activated:
                    comp.Activated = packet.Value == 1 ? true : false;
                    break;
                case FieldType.Mode:
                    comp.Mode = (ToolComp.ToolMode)packet.Value;
                    break;
                case FieldType.Action:
                    comp.Action = (ToolComp.ToolAction)packet.Value;
                    break;
                case FieldType.Draw:
                    comp.Draw = packet.Value == 1 ? true : false;
                    break;
                default:
                    Logs.WriteLine($"Invalid packet value: {packet.Value} for field: {packet.Field}");
                    break;
            }
        }

    }

    [ProtoContract]
    [ProtoInclude(4, typeof(UpdatePacket))]
    [ProtoInclude(6, typeof(ReplicationPacket))]
    [ProtoInclude(7, typeof(SettingsPacket))]
    public class Packet
    {
        [ProtoMember(1)] internal long EntityId;
        [ProtoMember(2)] internal byte Type;
    }

    [ProtoContract]
    public class UpdatePacket : Packet
    {
        [ProtoMember(1)] internal byte Field;
        [ProtoMember(2)] internal byte Value;

        public UpdatePacket(long entityId, FieldType field, int value)
        {
            EntityId = entityId;
            Type = (byte)PacketType.Update;
            Field = (byte)field;
            Value = (byte)value;
        }

        public UpdatePacket()
        {

        }
    }

    [ProtoContract]
    public class ReplicationPacket : Packet
    {
        [ProtoMember(1)] internal bool Add;
    }

    [ProtoContract]
    public class SettingsPacket : Packet
    {
        //[ProtoMember(1)] internal StealthSettings Settings;
    }

    public enum PacketType : byte
    {
        Update,
        Replicate,
        Settings
    }

    public enum FieldType : byte
    {
        Invalid = 0,
        Activated = 1,
        Mode = 2,
        Action = 3,
        Draw = 4,
    }

}
