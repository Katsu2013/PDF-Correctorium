using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PdfCorrectorium.App.Services;

/// <summary>Capability-free AppContainer. Only staged binaries and operation files are granted access.</summary>
internal sealed class WindowsSandboxProcess : IDisposable
{
    private const uint ExtendedStartup = 0x00080000, Suspended = 4, NoWindow = 0x08000000, UnicodeEnvironment = 0x400;
    private readonly string _profile;
    private FileStream? _ownerLease;
    private bool _ownsRoot;
    private IntPtr _sid;
    private readonly AnonymousPipeServerStream _input = new(PipeDirection.Out, HandleInheritability.Inheritable);
    private readonly AnonymousPipeServerStream _output = new(PipeDirection.In, HandleInheritability.Inheritable);
    private readonly AnonymousPipeServerStream _error = new(PipeDirection.In, HandleInheritability.Inheritable);
    private WindowsProcessJob? _job;
    private bool _disposed;
    internal string Root { get; }
    internal string WorkDirectory { get; }
    internal string RuntimeDirectory { get; }
    internal Process Process { get; private set; } = null!;
    internal StreamWriter StandardInput { get; private set; } = null!;
    internal StreamReader StandardOutput { get; private set; } = null!;
    internal StreamReader StandardError { get; private set; } = null!;
    internal long MemoryLimitBytes => _job?.ProcessMemoryLimitBytes ?? 0;
    internal long? PeakMemoryBytes => _job?.TryGetPeakProcessMemoryBytes();

    internal WindowsSandboxProcess()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("安全なPDF処理にはWindows 10以降が必要です。");
        Root = Path.Combine(Path.GetTempPath(), "PdfCorrectorium", "sandbox", Guid.NewGuid().ToString("N"));
        _profile = SandboxLifetime.ProfilePrefix + Path.GetFileName(Root);
        WorkDirectory = Path.Combine(Root, "work"); RuntimeDirectory = Path.Combine(Root, "runtime");
        try
        {
            if (Directory.Exists(Root)) throw new IOException("Sandbox directory already exists.");
            Directory.CreateDirectory(Root);
            _ownerLease = SandboxLifetime.Acquire(Root);
            _ownsRoot = true;
            Marshal.ThrowExceptionForHR(CreateAppContainerProfile(_profile, "PDF Correctorium worker", "Ephemeral isolated PDF processing", IntPtr.Zero, 0, out _sid));
            var sid = new SecurityIdentifier(_sid);
            Grant(Root, sid, FileSystemRights.ReadAndExecute);
            Directory.CreateDirectory(WorkDirectory); Grant(WorkDirectory, sid, FileSystemRights.Modify);
            Directory.CreateDirectory(RuntimeDirectory); Grant(RuntimeDirectory, sid, FileSystemRights.ReadAndExecute);
            // AppContainer uses low integrity; writable operation files must carry a matching label.
            SetLowIntegrity(WorkDirectory);
        }
        catch { Dispose(); throw; }
    }

    internal static SandboxLifetime.CleanupResult CleanupAbandoned() =>
        SandboxLifetime.Cleanup(Path.Combine(Path.GetTempPath(), "PdfCorrectorium", "sandbox"), DateTimeOffset.UtcNow,
            profile =>
            {
                var result = DeleteAppContainerProfile(profile);
                if (result != unchecked((int)0x80070002)) Marshal.ThrowExceptionForHR(result);
            });

    private static void Grant(string directory, SecurityIdentifier sid, FileSystemRights rights)
    {
        var info = new DirectoryInfo(directory);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(sid, rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(acl);
    }
    private static void SetLowIntegrity(string path)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;OICI;NW;;;LW)", 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!GetSecurityDescriptorSacl(descriptor, out _, out var sacl, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = SetNamedSecurityInfo(path, 1, 0x10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, sacl);
            if (result != 0) throw new Win32Exception((int)result);
        }
        finally { LocalFree(descriptor); }
    }

    internal string StageApplication(string executable, bool includeOcr = false)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        long copied = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(file);
            if (extension is not (".dll" or ".exe" or ".json") && Path.GetFileName(file) != "portable.marker") continue;
            // RID-specific self-contained publishing places these natives at the
            // root. Keep the same OCR exclusion as for runtimes/ below.
            if (!includeOcr && Path.GetFileName(file) is "onnxruntime.dll" or "onnxruntime_providers_shared.dll" or "libSkiaSharp.dll") continue;
            Copy(file, Path.Combine(RuntimeDirectory, Path.GetFileName(file)));
        }
        foreach (var name in new[] { "components", "runtimes", "ja" })
        {
            var source = Path.Combine(directory, name);
            if (!Directory.Exists(source)) continue;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                // Do not copy large OCR models/native runtimes into every PDF/thumbnail worker.
                if (!includeOcr && (relative.StartsWith("components/rapidocr/", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(file) is "onnxruntime.dll" or "onnxruntime_providers_shared.dll" or "libSkiaSharp.dll")) continue;
                if ((relative.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase) || relative.Contains("SkiaSharp", StringComparison.OrdinalIgnoreCase)) &&
                    relative.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase) && !relative.StartsWith("runtimes/win-x64/", StringComparison.OrdinalIgnoreCase)) continue;
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Runtime links are not allowed.");
                Copy(file, Path.Combine(RuntimeDirectory, Path.GetRelativePath(directory, file)));
            }
        }
        return Path.Combine(RuntimeDirectory, Path.GetFileName(executable));
        void Copy(string source, string destination)
        {
            copied = checked(copied + new FileInfo(source).Length);
            if (copied > 512L * 1024 * 1024) throw new IOException("Worker runtime exceeds the staging budget.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, false);
        }
    }
    internal string StageTool(string executable)
    {
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetFullPath(executable))!))
            if (Path.GetExtension(file) is ".dll" or ".exe") File.Copy(file, Path.Combine(RuntimeDirectory, Path.GetFileName(file)), false);
        return Path.Combine(RuntimeDirectory, Path.GetFileName(executable));
    }
    internal string StageInput(string path, string? name = null)
    {
        var destination = Path.Combine(WorkDirectory, name ?? Guid.NewGuid().ToString("N") + Path.GetExtension(path));
        File.Copy(Path.GetFullPath(path), destination, false); return destination;
    }

    internal void Start(string executable, IEnumerable<string> arguments)
    {
        var attributeSize = IntPtr.Zero;
        _ = InitializeProcThreadAttributeList(IntPtr.Zero, 2, 0, ref attributeSize);
        var attributes = Marshal.AllocHGlobal(attributeSize);
        var capabilities = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityCapabilities>());
        var handles = Marshal.AllocHGlobal(IntPtr.Size * 3);
        var environment = IntPtr.Zero; var initialized = false;
        ProcessInformation created = default;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 2, 0, ref attributeSize)) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            Marshal.StructureToPtr(new SecurityCapabilities { AppContainerSid = _sid }, capabilities, false);
            if (!UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x00020009, capabilities, (IntPtr)Marshal.SizeOf<SecurityCapabilities>(), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var childHandles = new[] { _input.ClientSafePipeHandle.DangerousGetHandle(), _output.ClientSafePipeHandle.DangerousGetHandle(), _error.ClientSafePipeHandle.DangerousGetHandle() };
            for (var i = 0; i < childHandles.Length; i++) Marshal.WriteIntPtr(handles, i * IntPtr.Size, childHandles[i]);
            if (!UpdateProcThreadAttribute(attributes, 0, (IntPtr)0x00020002, handles, (IntPtr)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 0x100,
                    StandardInput = childHandles[0], StandardOutput = childHandles[1], StandardError = childHandles[2] },
                Attributes = attributes,
            };
            var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()) variables[(string)entry.Key] = (string)entry.Value!;
            // Runtime overrides and profiler/startup hooks are never inherited by document workers.
            foreach (var key in variables.Keys.Where(k => k.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("COR_", StringComparison.OrdinalIgnoreCase)).ToArray()) variables.Remove(key);
            variables["TEMP"] = WorkDirectory; variables["TMP"] = WorkDirectory;
            variables["PDFCORRECTORIUM_WORKER_ROOT"] = WorkDirectory;
            variables["DOTNET_EnableDiagnostics"] = "0";
            environment = Marshal.StringToHGlobalUni(string.Join('\0', variables.Select(x => x.Key + "=" + x.Value)) + "\0\0");
            var command = new StringBuilder(Quote(Path.GetFullPath(executable)) + " " + string.Join(' ', arguments.Select(Quote)));
            if (!CreateProcess(Path.GetFullPath(executable), command, IntPtr.Zero, IntPtr.Zero, true,
                    ExtendedStartup | Suspended | NoWindow | UnicodeEnvironment, environment, RuntimeDirectory, ref startup, out created))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "権限を制限したPDF処理を開始できませんでした。安全でない通常起動へは切り替えません。");
            Process = Process.GetProcessById(created.ProcessId);
            _job = WindowsProcessJob.Attach(Process);
            _input.DisposeLocalCopyOfClientHandle(); _output.DisposeLocalCopyOfClientHandle(); _error.DisposeLocalCopyOfClientHandle();
            StandardInput = new StreamWriter(_input, new UTF8Encoding(false)) { AutoFlush = true };
            StandardOutput = new StreamReader(_output, Encoding.UTF8); StandardError = new StreamReader(_error, Encoding.UTF8);
            if (ResumeThread(created.Thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch
        {
            if (Process is not null) ExternalProcessRunner.TryTerminate(Process);
            else if (created.Process != IntPtr.Zero) _ = TerminateProcess(created.Process, 1);
            throw;
        }
        finally
        {
            if (created.Thread != IntPtr.Zero) CloseHandle(created.Thread);
            if (created.Process != IntPtr.Zero) CloseHandle(created.Process);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(attributes); Marshal.FreeHGlobal(capabilities); Marshal.FreeHGlobal(handles);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
        }
    }
    internal FileStream OpenOutput(string path, long maximumBytes) =>
        OpenCheckedOutput(path, maximumBytes, FileShare.Read);

    // State snapshots are published by rename, never modified in place. Delete
    // sharing keeps this reader compatible with rename-capable publishers; Windows
    // overwrite-move may still need the publisher's bounded retry until it closes.
    // This does not grant write sharing or relax path/size checks for either reader.
    internal FileStream OpenStateOutput(string path, long maximumBytes) =>
        OpenCheckedOutput(path, maximumBytes, FileShare.Read | FileShare.Delete);

    private FileStream OpenCheckedOutput(string path, long maximumBytes, FileShare sharing)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, sharing);
        try
        {
            var final = new StringBuilder(32768);
            var length = GetFinalPathNameByHandle(stream.SafeFileHandle, final, final.Capacity, 0);
            if (length == 0 || length >= final.Capacity) throw new IOException("Cannot verify worker output path.");
            var resolved = final.ToString();
            if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
            if (!resolved.StartsWith(WorkDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                stream.Length > maximumBytes) throw new InvalidDataException("Worker output escapes its operation directory or size budget.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
    internal void CopyOutput(string source, string destination, long maximumBytes)
    {
        using var input = OpenOutput(source, maximumBytes);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }
    internal static async Task<int> RunProbeAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--sandbox-probe");
        if (index < 0 || args.Length < index + 4) return 2;
        var inside = File.ReadAllText(args[index + 1]);
        var readDenied = false; var writeDenied = false; var runtimeDenied = false; var networkDenied = false;
        var ownerLeaseWriteDenied = false; var ownerLeaseDeleteDenied = false;
        try { _ = File.ReadAllText(args[index + 2]); } catch (UnauthorizedAccessException) { readDenied = true; }
        try { File.WriteAllText(args[index + 2] + ".write", "denied"); } catch (UnauthorizedAccessException) { writeDenied = true; }
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "forbidden-write.txt"), "denied"); } catch (UnauthorizedAccessException) { runtimeDenied = true; }
        var ownerLease = Path.Combine(Directory.GetParent(Environment.GetEnvironmentVariable("PDFCORRECTORIUM_WORKER_ROOT")!)!.FullName, SandboxLifetime.LeaseName);
        try { File.WriteAllText(ownerLease, "tamper"); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ownerLeaseWriteDenied = true; }
        try { File.Delete(ownerLease); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ownerLeaseDeleteDenied = true; }
        var networkOutcome = "connected";
        using var socket = new System.Net.Sockets.TcpClient();
        try { await socket.ConnectAsync(System.Net.IPAddress.Loopback, int.Parse(args[index + 3])).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AccessDenied) { networkDenied = true; networkOutcome = "access-denied"; }
        catch (TimeoutException) { networkDenied = true; networkOutcome = "timed-out"; }
        var result = new { appContainer = IsAppContainer, capabilityCount = CapabilityCount, inside = inside == "allowed", readDenied, writeDenied, runtimeDenied, ownerLeaseWriteDenied, ownerLeaseDeleteDenied, networkConnected = !networkDenied, networkOutcome };
        await Console.Out.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(result));
        return result.appContainer && result.capabilityCount == 0 && result.inside && readDenied && writeDenied && runtimeDenied &&
            ownerLeaseWriteDenied && ownerLeaseDeleteDenied && !result.networkConnected ? 0 : 3;
    }
    internal static int CapabilityCount
    {
        get
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using (token)
            {
                _ = GetTokenInformationBuffer(token, 30, IntPtr.Zero, 0, out var required);
                if (required < 4 || required > 65536) throw new InvalidDataException("Unexpected token capabilities.");
                var buffer = Marshal.AllocHGlobal(required);
                try
                {
                    if (!GetTokenInformationBuffer(token, 30, buffer, required, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    return Marshal.ReadInt32(buffer);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
    }
    internal static bool IsAppContainer
    {
        get
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using (token)
            {
                if (!GetTokenInformation(token, 29, out var value, sizeof(int), out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return value != 0;
            }
        }
    }
    internal static string Quote(string value)
    {
        var result = new StringBuilder("\""); var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { result.Append('\\', slashes * 2 + 1).Append(c); slashes = 0; continue; }
            result.Append('\\', slashes).Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        if (Process is not null)
        {
            ExternalProcessRunner.TryTerminate(Process);
            try { Process.WaitForExit(5000); } catch (InvalidOperationException) { }
            Process.Dispose();
        }
        _job?.Dispose(); StandardInput?.Dispose(); StandardOutput?.Dispose(); StandardError?.Dispose();
        _input.Dispose(); _output.Dispose(); _error.Dispose();
        _ownerLease?.Dispose(); _ownerLease = null;
        if (_sid != IntPtr.Zero) { FreeSid(_sid); _sid = IntPtr.Zero; _ = DeleteAppContainerProfile(_profile); }
        // Delete only the exact, random child under our designated temporary root. Never follow links.
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PdfCorrectorium", "sandbox"));
        if (!_ownsRoot || !Path.GetFullPath(Root).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        try { if (Directory.Exists(Root) && SandboxLifetime.IsPlainPath(Root)) DeleteTree(Root); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private static void DeleteTree(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) == 0) File.Delete(entry);
            else if ((attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(entry);
            else DeleteTree(entry);
        }
        Directory.Delete(path);
    }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityCapabilities { public IntPtr AppContainerSid, Capabilities; public uint CapabilityCount, Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, ReservedSize; public IntPtr ReservedData, StandardInput, StandardOutput, StandardError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] private static extern int CreateAppContainerProfile(string name, string display, string description, IntPtr capabilities, uint count, out IntPtr sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)] private static extern int DeleteAppContainerProfile(string name);
    [DllImport("advapi32.dll")] private static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetSecurityDescriptorSacl(IntPtr descriptor, out bool present, out IntPtr sacl, out bool defaulted);
    [DllImport("advapi32.dll", EntryPoint = "SetNamedSecurityInfoW", CharSet = CharSet.Unicode)] private static extern uint SetNamedSecurityInfo(string name, int type, uint information, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, int size, uint flags);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)] private static extern bool GetTokenInformationBuffer(SafeAccessTokenHandle token, int informationClass, IntPtr information, int length, out int returned);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int length, out int returned);
}
