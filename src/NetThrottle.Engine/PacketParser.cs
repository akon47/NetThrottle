using NetThrottle.Core.Models;

namespace NetThrottle.Engine;

/// <summary>Lightweight read-only parser for the few header fields the engine needs.</summary>
internal static class PacketParser
{
    public readonly record struct PacketInfo(ProtocolKind Protocol, ushort SrcPort, ushort DstPort);

    private const byte ProtoTcp = 6;
    private const byte ProtoUdp = 17;

    /// <summary>
    /// Total length (bytes) of the IP packet starting at the span, read from the IP
    /// header, or -1 if it cannot be determined. Used to walk a batched buffer.
    /// </summary>
    public static int TotalLength(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 4) return -1;
        int version = buffer[0] >> 4;
        if (version == 4)
            return (buffer[2] << 8) | buffer[3];
        if (version == 6)
            return buffer.Length < 6 ? -1 : 40 + ((buffer[4] << 8) | buffer[5]);
        return -1;
    }

    public static bool TryParse(ReadOnlySpan<byte> packet, out PacketInfo info)
    {
        info = default;
        if (packet.Length < 20) return false;

        int version = packet[0] >> 4;
        int transportOffset;
        byte protocol;

        if (version == 4)
        {
            int ihl = (packet[0] & 0x0F) * 4;
            if (ihl < 20 || packet.Length < ihl + 4) return false;
            protocol = packet[9];
            transportOffset = ihl;
        }
        else if (version == 6)
        {
            if (packet.Length < 40 + 4) return false;
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
