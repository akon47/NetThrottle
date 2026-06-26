namespace NetThrottle.Core.Models;

/// <summary>Traffic direction a rule applies to.</summary>
[Flags]
public enum Direction
{
    Inbound = 1,
    Outbound = 2,
    Both = Inbound | Outbound,
}
