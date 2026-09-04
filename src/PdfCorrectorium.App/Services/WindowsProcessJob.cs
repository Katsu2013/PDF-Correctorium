using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PdfCorrectorium.App.Services;

/// <summary>外部PDF処理をWindowsジョブへ収容し、終了と資源上限を親プロセスから強制します。</summary>
internal sealed class WindowsProcessJob : IDisposable
{
    private const uint JobObjectLimitActiveProcess = 0x00000008;
    private const uint JobObjectLimitProcessMemory = 0x00000100;
    private const uint JobObjectLimitJobMemory = 0x00000200;
    private const uint JobObjectLimitDieOnUnhandledException = 0x00000400;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    private readonly SafeFileHandle _handle;

    private WindowsProcessJob(SafeFileHandle handle) => _handle = handle;

    public static WindowsProcessJob Attach(
        Process process,
        long processMemoryLimitBytes = 2L * 1024 * 1024 * 1024,
        long jobMemoryLimitBytes = 3L * 1024 * 1024 * 1024,
        uint activeProcessLimit = 4)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WindowsジョブはWindowsでのみ利用できます。");
        if (processMemoryLimitBytes <= 0 || jobMemoryLimitBytes < processMemoryLimitBytes)
            throw new ArgumentOutOfRangeException(nameof(jobMemoryLimitBytes));
        if (activeProcessLimit == 0) throw new ArgumentOutOfRangeException(nameof(activeProcessLimit));

        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "PDF処理用Windowsジョブを作成できませんでした。");
        try
        {
            var limits = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitActiveProcess |
                                 JobObjectLimitProcessMemory |
                                 JobObjectLimitJobMemory |
                                 JobObjectLimitDieOnUnhandledException |
                                 JobObjectLimitKillOnJobClose,
                    ActiveProcessLimit = activeProcessLimit,
                },
                ProcessMemoryLimit = new UIntPtr((ulong)processMemoryLimitBytes),
                JobMemoryLimit = new UIntPtr((ulong)jobMemoryLimitBytes),
            };
            var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, buffer, false);
                if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformationClass, buffer, (uint)size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "PDF処理用Windowsジョブの制限を設定できませんでした。");
            }
            finally { Marshal.FreeHGlobal(buffer); }

            if (!AssignProcessToJobObject(handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "外部PDF処理をWindowsジョブへ収容できませんでした。");
            return new WindowsProcessJob(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job,
        int informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
