using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PdfCorrectorium.Infrastructure;

/// <summary>Detects MSIX identity without requiring WinRT or the Windows App SDK.</summary>
public static class ApplicationPackageIdentity
{
    /// <summary>Read the current package's protected identity, never editable distribution metadata.</summary>
    public static InstalledPackageIdentity? ReadCurrent()
    {
        if (!OperatingSystem.IsWindows()) return null;
        uint length = 0;
        var result = GetCurrentPackageId(ref length, IntPtr.Zero);
        if (result == 15700) return null;
        if (result != 122 || length is < 48 or > 65536) throw new Win32Exception(result, "Could not read package identity.");
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            result = GetCurrentPackageId(ref length, buffer);
            if (result != 0) throw new Win32Exception(result, "Could not read package identity.");
            var value = Marshal.PtrToStructure<PackageId>(buffer);
            return new(Marshal.PtrToStringUni(value.Name) ?? throw new InvalidOperationException("Missing package name."),
                Marshal.PtrToStringUni(value.Publisher) ?? throw new InvalidOperationException("Missing package publisher."),
                new Version((int)(value.Version >> 48), (int)((value.Version >> 32) & 65535), (int)((value.Version >> 16) & 65535), (int)(value.Version & 65535)));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PackageId
    {
        public uint Reserved;
        public uint Architecture;
        public ulong Version;
        public IntPtr Name;
        public IntPtr Publisher;
        public IntPtr ResourceId;
        public IntPtr PublisherId;
    }
    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageId(ref uint bufferLength, IntPtr buffer);

    public static bool IsPackaged
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            uint length = 0;
            var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
            if (result == 15700) return false; // APPMODEL_ERROR_NO_PACKAGE
            if (result == 122 && length > 0) return true; // ERROR_INSUFFICIENT_BUFFER
            if (result == 0) return true;
            throw new Win32Exception(result, "Could not determine application package identity.");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
