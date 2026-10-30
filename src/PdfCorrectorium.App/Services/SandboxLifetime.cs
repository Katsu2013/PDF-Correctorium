using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PdfCorrectorium.App.Services;

/// <summary>Parent-owned leases identify our abandoned sandboxes without guessing ownership.</summary>
internal static class SandboxLifetime
{
    internal const string ProfilePrefix = "PdfCorrectorium.worker.";
    internal const string LeaseName = ".owner.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 8 };
    internal sealed record Owner(int SchemaVersion, string DirectoryId, string ProfileName,
        string OwnerSid, int ProcessId, long ProcessStartUtcTicks, DateTimeOffset CreatedUtc);
    internal readonly record struct CleanupResult(int Scanned, int Removed, int Skipped, int Errors);

    internal static string CurrentOwnerSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new InvalidOperationException("Cannot identify the sandbox owner.");
    }

    internal static FileStream Acquire(string root)
    {
        var id = Path.GetFileName(root);
        if (!Guid.TryParseExact(id, "N", out _) || !IsPlainPath(root)) throw new IOException("Unsafe sandbox lease directory.");
        using var process = Process.GetCurrentProcess();
        var record = new Owner(1, id, ProfilePrefix + id, CurrentOwnerSid(), process.Id,
            process.StartTime.ToUniversalTime().Ticks, DateTimeOffset.UtcNow);
        var stream = new FileStream(Path.Combine(root, LeaseName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            JsonSerializer.Serialize(stream, record, JsonOptions);
            stream.Flush(flushToDisk: true);
            stream.Position = 0;
            return stream; // No write/delete sharing; OS releases this lease if the parent dies.
        }
        catch { stream.Dispose(); throw; }
    }

    internal static CleanupResult Cleanup(string parent, DateTimeOffset now, Action<string> removeProfile,
        Func<int, long, bool?>? ownerAlive = null)
    {
        try { return CleanupCore(parent, now, removeProfile, ownerAlive); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
        { return new(0, 0, 0, 1); } // Optional maintenance must not prevent normal startup.
    }

    private static CleanupResult CleanupCore(string parent, DateTimeOffset now, Action<string> removeProfile,
        Func<int, long, bool?>? ownerAlive)
    {
        parent = Path.GetFullPath(parent);
        if (!Directory.Exists(parent) || !IsPlainPath(parent)) return default;
        ownerAlive ??= IsOwnerAlive;
        var sid = CurrentOwnerSid();
        var scanned = 0; var removed = 0; var skipped = 0; var errors = 0;
        var timer = Stopwatch.StartNew();
        // Bounded startup work; later launches can reclaim additional abandoned roots.
        foreach (var child in Directory.EnumerateDirectories(parent).Take(4096))
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(3)) break;
            scanned++;
            FileStream? lease = null;
            try
            {
                var root = Path.GetFullPath(child);
                var id = Path.GetFileName(root);
                if (!root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParseExact(id, "N", out _) || !IsPlainPath(root) ||
                    new DirectoryInfo(root).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))?.Value != sid)
                { skipped++; continue; }
                var leasePath = Path.Combine(root, LeaseName);
                // Untagged legacy directories are not safe to attribute to a dead owner.
                if (!File.Exists(leasePath) || !IsPlainPath(leasePath) ||
                    new FileInfo(leasePath).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))?.Value != sid)
                { skipped++; continue; }
                lease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                if (!ResolvedPath(lease).Equals(leasePath, StringComparison.OrdinalIgnoreCase) ||
                    lease.Length is <= 0 or > 8192) { skipped++; continue; }
                var bytes = new byte[(int)lease.Length]; lease.ReadExactly(bytes);
                var record = JsonSerializer.Deserialize<Owner>(bytes, JsonOptions);
                if (record is null || record.SchemaVersion != 1 || record.DirectoryId != id ||
                    record.ProfileName != ProfilePrefix + id || record.OwnerSid != sid || record.ProcessId <= 0 ||
                    record.ProcessStartUtcTicks <= 0 || record.CreatedUtc > now - TimeSpan.FromHours(24) ||
                    ownerAlive(record.ProcessId, record.ProcessStartUtcTicks) != false || !BoundedTree(root, timer))
                { skipped++; continue; }
                // Every destructive target is an exact validated child. The lease is
                // held exclusively, and root reparse/identity checks are repeated.
                if (!IsPlainPath(root) || !ResolvedPath(lease).Equals(leasePath, StringComparison.OrdinalIgnoreCase))
                { skipped++; continue; }
                removeProfile(record.ProfileName);
                DeleteTree(root, root, keepLease: true, depth: 0);
                lease.Dispose(); lease = null;
                File.Delete(leasePath);
                Directory.Delete(root);
                removed++;
            }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33) { skipped++; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or Win32Exception or System.Runtime.InteropServices.COMException)
            { errors++; }
            finally { lease?.Dispose(); }
        }
        return new(scanned, removed, skipped, errors);
    }

    internal static bool IsPlainPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        for (string? item = full; !string.IsNullOrEmpty(item); item = Path.GetDirectoryName(item))
            if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    private static bool? IsOwnerAlive(int pid, long startTicks)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startTicks; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return null; } // An inaccessible process is not proven dead.
    }

    private static bool BoundedTree(string root, Stopwatch timer)
    {
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0));
        var entries = 0;
        while (pending.TryPop(out var item))
        {
            if (item.Depth > 64 || timer.Elapsed > TimeSpan.FromSeconds(3)) return false;
            foreach (var entry in Directory.EnumerateFileSystemEntries(item.Path))
            {
                if (++entries > 4096) return false;
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
                    pending.Push((entry, item.Depth + 1));
            }
        }
        return true;
    }

    private static void DeleteTree(string path, string root, bool keepLease, int depth)
    {
        path = Path.GetFullPath(path);
        if (depth > 64 || (path != root && !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Sandbox cleanup target escaped its validated root.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(path); return; }
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (keepLease && entry.Equals(Path.Combine(root, LeaseName), StringComparison.OrdinalIgnoreCase)) continue;
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) == 0) File.Delete(entry);
            else if ((attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(entry);
            else DeleteTree(entry, root, keepLease, depth + 1);
        }
        if (path != root || !keepLease) Directory.Delete(path);
    }

    private static string ResolvedPath(FileStream stream)
    {
        var path = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(stream.SafeFileHandle, path, path.Capacity, 0);
        if (length == 0 || length >= path.Capacity) throw new IOException("Cannot verify sandbox lease handle.");
        var result = path.ToString();
        return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result[4..] : result;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, int size, uint flags);
}
