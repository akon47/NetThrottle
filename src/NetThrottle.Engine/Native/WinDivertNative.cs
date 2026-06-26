using System.Reflection;
using System.Runtime.InteropServices;

namespace NetThrottle.Engine.Native;

/// <summary>
/// P/Invoke bindings for WinDivert 2.2 (https://reqrypt.org/windivert.html).
/// Requires WinDivert.dll + WinDivert64.sys next to the executable and admin rights.
/// </summary>
internal static class WinDivertNative
{
    private const string Dll = "WinDivert.dll";

    static WinDivertNative()
    {
        // Resolve WinDivert.dll explicitly from the app directory. This makes the
        // engine robust under single-file publishing, where the default native
        // probing path can differ from the executable's folder.
        NativeLibrary.SetDllImportResolver(typeof(WinDivertNative).Assembly, static (name, asm, path) =>
        {
            if (string.Equals(name, Dll, StringComparison.OrdinalIgnoreCase))
            {
                string candidate = System.IO.Path.Combine(AppContext.BaseDirectory, Dll);
                if (System.IO.File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out nint handle))
                    return handle;
            }
            return nint.Zero; // fall back to default resolution
        });
    }

    /// <summary>Forces the static constructor (and DLL resolver) to run.</summary>
    public static void EnsureInitialized() { }

    public enum Layer
    {
        Network = 0,
        NetworkForward = 1,
        Flow = 2,
        Socket = 3,
        Reflect = 4,
    }

    public enum Param
    {
        QueueLength = 0,
        QueueTime = 1,
        QueueSize = 2,
    }

    [Flags]
    public enum OpenFlags : ulong
    {
        None = 0,
        Sniff = 0x0001,
        Drop = 0x0002,
        RecvOnly = 0x0004,
        SendOnly = 0x0008,
        NoInstall = 0x0010,
        Fragments = 0x0020,
    }

    /// <summary>
    /// WINDIVERT_ADDRESS (80 bytes). The bitfield word is exposed raw via
    /// <see cref="Flags"/>; use the helper properties to decode it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 80)]
    public struct Address
    {
        public long Timestamp;
        public uint Flags;       // Layer:8 | Event:8 | Sniffed | Outbound | ... | Reserved1:8
        public uint Reserved2;
        // union { WINDIVERT_DATA_NETWORK { IfIdx; SubIfIdx; } ... } (64 bytes)
        public uint IfIdx;
        public uint SubIfIdx;
        // remaining 56 bytes of the union are not needed by this engine.

        public readonly bool IsOutbound => ((Flags >> 17) & 1) != 0;
        public readonly bool IsLoopback => ((Flags >> 18) & 1) != 0;
        public readonly bool IsIPv6 => ((Flags >> 20) & 1) != 0;

        public void SetOutbound(bool value)
        {
            if (value) Flags |= 1u << 17;
            else Flags &= ~(1u << 17);
        }
    }

    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern nint WinDivertOpen(string filter, Layer layer, short priority, OpenFlags flags);

    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertRecv(nint handle, byte[] packet, uint packetLen, out uint readLen, ref Address address);

    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSend(nint handle, byte[] packet, uint packetLen, out uint writeLen, ref Address address);

    /// <summary>Batched receive: fills the buffer with multiple packets and the
    /// matching address array. <paramref name="addrLen"/> is in bytes (count * sizeof(Address)).</summary>
    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertRecvEx(nint handle, byte[] packet, uint packetLen, out uint readLen,
        ulong flags, [In, Out] Address[] address, ref uint addrLen, nint overlapped);

    /// <summary>Batched send of multiple packets with their address array.</summary>
    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSendEx(nint handle, byte[] packet, uint packetLen, out uint writeLen,
        ulong flags, [In] Address[] address, uint addrLen, nint overlapped);

    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertClose(nint handle);

    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSetParam(nint handle, Param param, ulong value);

    /// <summary>Recompute checksums after a packet is modified. Flags 0 = all.</summary>
    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperCalcChecksums(byte[] packet, uint packetLen, ref Address address, ulong flags);

    public static readonly nint InvalidHandle = -1;
}
