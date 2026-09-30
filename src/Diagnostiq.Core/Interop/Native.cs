using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Diagnostiq.Core.Interop;

internal static partial class Native
{
    // ----- Power -----
    [StructLayout(LayoutKind.Sequential)]
    public struct SystemPowerStatus
    {
        public byte ACLineStatus;       // 0 offline, 1 online, 255 unknown
        public byte BatteryFlag;        // 128 = no battery, 255 unknown
        public byte BatteryLifePercent; // 255 unknown
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    // ----- Memory -----
    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public static ulong AvailablePhysicalMemory()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref m) ? m.AvailPhys : 0;
    }

    // ----- Raw disk access -----
    public const uint GenericRead = 0x80000000;
    public const uint FileShareRead = 0x1, FileShareWrite = 0x2;
    public const uint OpenExisting = 3;
    public const uint FileFlagNoBuffering = 0x20000000;
    public const uint IoctlDiskGetLengthInfo = 0x0007405C;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        IntPtr inBuffer, uint inBufferSize, out long outBuffer, uint outBufferSize,
        out uint bytesReturned, IntPtr overlapped);

    // ----- PDH (performance counters, locale-independent via English paths) -----
    public const uint PdhFmtDouble = 0x00000200;
    public const uint PdhMoreData = 0x800007D2;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct PdhFmtCounterValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PdhFmtCounterValueItem
    {
        public IntPtr Name;              // wchar_t*
        public PdhFmtCounterValue Value;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCollectQueryData(IntPtr query);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    public static partial uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCloseQuery(IntPtr query);

    // ----- Firmware -----
    public const int FirmwareTypeBios = 1, FirmwareTypeUefi = 2;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFirmwareType(out int firmwareType);

    // ----- TPM Base Services (no admin needed, unlike Win32_Tpm) -----
    [StructLayout(LayoutKind.Sequential)]
    public struct TpmDeviceInfo
    {
        public uint StructVersion;
        public uint TpmVersion;        // 1 = TPM 1.2, 2 = TPM 2.0
        public uint TpmInterfaceType;
        public uint TpmImpRevision;
    }

    public const uint TbsSuccess = 0;
    public const uint TbsTpmNotFound = 0x8028400F;
    public const uint TbsServiceNotRunning = 0x80284008;
    public const uint TbsServiceDisabled = 0x80284010;

    [LibraryImport("tbs.dll")]
    public static partial uint Tbsi_GetDeviceInfo(uint size, out TpmDeviceInfo info);

    // ----- Direct3D 12 (Windows 11 needs DirectX 12 with a WDDM 2.x driver) -----
    public const int D3DFeatureLevel11_0 = 0xB000;
    public static readonly Guid IidID3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");

    /// <summary>With <paramref name="device"/> = null it only tests support: S_FALSE (1) means a device could be created.</summary>
    [LibraryImport("d3d12.dll")]
    public static partial int D3D12CreateDevice(IntPtr adapter, int minimumFeatureLevel, in Guid riid, IntPtr device);
}
