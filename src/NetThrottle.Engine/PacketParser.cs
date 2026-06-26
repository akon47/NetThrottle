using NetThrottle.Core.Models;

namespace NetThrottle.Engine;

/// <summary>Lightweight read-only parser for the few header fields the engine needs.</summary>
internal static class PacketParser
{
    public readonly record struct PacketInfo(ProtocolKind Protocol, ushort SrcPort, ushort DstPort);

    private const byte ProtoTcp = 6;
    private const byte ProtoUdp = 17;

    public static bool TryParse(byte[] packet, uint len, out PacketInfo info)
    {
        info = default;
        if (len < 20) return false;

        int version = packet[0] >> 4;
        int transportOffset;
        byte protocol;

        if (version == 4)
        {
            int ihl = (packet[0] & 0x0F) * 4;
            if (ihl < 20 || len < ihl + 4) return false;
            protocol = packet[9];
            transportOffset = ihl;
        }
        else if (version == 6)
        {
            if (len < 40 + 4) return false;
            protocol = packet[6];      // Next Header; extension headers are not followed.
            transportOffset = 40;
        }
        else
        {
            return false;
        }

        ProtocolKind kind = protocol switch
        {
            ProtoTcp => ProtocolKind.Tcp,
            ProtoUdp => ProtocolKind.Udp,
            _ => (ProtocolKind)0,
        };
        if (kind == 0) return false;

        ushort src = (ushort)((packet[transportOffset] << 8) | packet[transportOffset + 1]);
        ushort dst = (ushort)((packet[transportOffset + 2] << 8) | packet[transportOffset + 3]);
        info = new PacketInfo(kind, src, dst);
        return true;
    }
}
