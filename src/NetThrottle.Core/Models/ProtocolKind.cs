namespace NetThrottle.Core.Models;

/// <summary>Transport protocol a rule applies to.</summary>
[Flags]
public enum ProtocolKind
{
    Tcp = 1,
    Udp = 2,
    Both = Tcp | Udp,
}
