using System.Diagnostics;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using PdfCorrectorium.App.Services;
using State = PdfCorrectorium.App.Services.IsolatedPdfExportService.ExportWorkerState;
using Writer = PdfCorrectorium.App.Services.IsolatedPdfExportService.ExportWorkerStateWriter;

internal static class AuditReviewRegressionChecks
{
    internal static async Task RunAsync(string outputDirectory)
    {
        var root = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(root)) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
            checks.Add("PASS: " + message); Console.WriteLine("PASS: " + message);
        }
        var originalDirectory = Environment.CurrentDirectory;
        try
        {
            var input = Path.Combine(root, "routing"); Directory.CreateDirectory(input);
            Environment.CurrentDirectory = input;
            foreach (var option in new[] { "--smoke-test", "--editor-test", "--pdf-native-worker", "--isolated-pdf-export", "--avalondock-preview" })
            {
                await File.WriteAllTextAsync(Path.Combine(input, option), "diagnostic routing fixture");
                Check(StartupArgumentRouting.FilesTakePriority([option]), "Existing option-shaped file suppresses diagnostic routing: " + option);
            }
            Directory.CreateDirectory(Path.Combine(input, "--render-test"));
            Check(StartupArgumentRouting.FilesTakePriority(["--render-test"]), "An existing option-shaped directory does not trigger a diagnostic either.");
            Check(StartupArgumentRouting.FilesTakePriority(["--similar-word-test", "--smoke-test"]), "File priority is decided before any competing diagnostic handler.");
            await File.WriteAllTextAsync(Path.Combine(input, "input.pdf"), "fixture");
            Check(!StartupArgumentRouting.FilesTakePriority(["--pdf-export-test", "input.pdf", "out.pdf"]), "Ordinary input paths do not disable explicitly invoked diagnostics.");
            var probed = new List<string>();
            Check(!StartupArgumentRouting.FilesTakePriority([@"\\invalid.example\share\input.pdf", "C:\\input.pdf"],
                value => { probed.Add(value); return false; }, _ => false) && probed.Count == 0,
                "Routing does not probe arbitrary absolute or UNC document references.");
            Environment.CurrentDirectory = originalDirectory;

            var parent = Path.Combine(root, "managed-sandboxes"); Directory.CreateDirectory(parent);
            var now = DateTimeOffset.UtcNow;
            var sid = SandboxLifetime.CurrentOwnerSid();
            string Fixture(string label, Func<SandboxLifetime.Owner, SandboxLifetime.Owner>? change = null, bool metadata = true)
            {
                var path = Path.Combine(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "input.pdf"), label);
                var id = Path.GetFileName(path);
                var owner = new SandboxLifetime.Owner(1, id, SandboxLifetime.ProfilePrefix + id, sid, int.MaxValue,
                    now.AddDays(-3).UtcTicks, now.AddDays(-2));
                if (metadata) File.WriteAllText(Path.Combine(path, SandboxLifetime.LeaseName), JsonSerializer.Serialize(change?.Invoke(owner) ?? owner));
                return path;
            }
            var stale = Fixture("owned orphan");
            var profiles = new List<string>();
            var cleaned = SandboxLifetime.Cleanup(parent, now, profiles.Add);
            Check(cleaned.Removed == 1 && !Directory.Exists(stale) && profiles.Single() == SandboxLifetime.ProfilePrefix + Path.GetFileName(stale),
                "An aged, unlocked, same-owner dead-process sandbox and only its matching profile are reclaimed.");
            var legacy = Fixture("untagged legacy", metadata: false);
            var young = Fixture("recent", o => o with { CreatedUtc = now });
            var foreign = Fixture("foreign", o => o with { OwnerSid = "S-1-0-0" });
            var mismatch = Fixture("mismatch", o => o with { DirectoryId = "../outside", ProfileName = "unrelated.profile" });
            var malformed = Fixture("malformed"); File.WriteAllText(Path.Combine(malformed, SandboxLifetime.LeaseName), new string('x', 8193));
            var fresh = SandboxLifetime.Cleanup(parent, now, _ => throw new InvalidOperationException("Unexpected profile deletion."));
            Check(fresh.Removed == 0 && new[] { legacy, young, foreign, mismatch, malformed }.All(Directory.Exists),
                "Legacy, recent, foreign-owner, mismatched and oversized metadata directories remain untouched.");

            using var self = Process.GetCurrentProcess();
            var live = Fixture("live", o => o with { ProcessId = self.Id, ProcessStartUtcTicks = self.StartTime.ToUniversalTime().Ticks });
            SandboxLifetime.Cleanup(parent, now, _ => throw new InvalidOperationException("Live profile removed."));
            Check(File.Exists(Path.Combine(live, "input.pdf")), "PID plus start time protects a live owner even when its lease is unlocked.");
            var held = Fixture("held lease");
            using (new FileStream(Path.Combine(held, SandboxLifetime.LeaseName), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                SandboxLifetime.Cleanup(parent, now, _ => throw new InvalidOperationException("Held profile removed."));
                Check(Directory.Exists(held), "An active OS lease prevents reclamation independently of age and claimed PID.");
            }
            // An uncertain/inaccessible owner is never treated as dead. Apply the
            // callback to all remaining fixtures so it cannot delete an unrelated one.
            SandboxLifetime.Cleanup(parent, now, _ => throw new InvalidOperationException("Uncertain profile removed."), (_, _) => null);
            Check(Directory.Exists(held), "Uncertain process identity is retained, not guessed dead.");
            var refusal = SandboxLifetime.Cleanup(parent, now, _ => throw new IOException("Simulated profile deletion refusal."));
            Check(refusal.Errors > 0 && Directory.Exists(held), "Profile-removal failure preserves its staging data and does not abort maintenance.");
            var finalCleanup = SandboxLifetime.Cleanup(parent, now, profiles.Add);
            Check(finalCleanup.Removed == 1 && !Directory.Exists(held) && Directory.Exists(live), "An unlocked orphan is reclaimed without touching a live or unowned sibling.");
            var linkTarget = Path.Combine(root, "outside-sentinel"); Directory.CreateDirectory(linkTarget);
            var sentinel = Path.Combine(linkTarget, "keep.txt"); File.WriteAllText(sentinel, "must remain");
            var linkRoot = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            CreateFixtureJunction(root, linkRoot, linkTarget);
            SandboxLifetime.Cleanup(parent, now, _ => throw new InvalidOperationException("Link profile removed."));
            Check(File.ReadAllText(sentinel) == "must remain" && Directory.Exists(linkRoot), "A root junction is never followed or reclaimed.");
            var linked = Fixture("linked child");
            CreateFixtureJunction(root, Path.Combine(linked, "external"), linkTarget);
            SandboxLifetime.Cleanup(parent, now, profiles.Add);
            Check(!Directory.Exists(linked) && File.ReadAllText(sentinel) == "must remain", "Owned child junctions are unlinked without deleting their target.");

            using (var sandbox = new WindowsSandboxProcess())
            {
                var statePath = Path.Combine(sandbox.WorkDirectory, "export.state.json");
                var writer = new Writer(statePath);
                await writer.WriteAsync(new("starting"), CancellationToken.None);
                var rejectedFinal = false;
                try { await writer.TryWriteProgressAsync(new("completed"), CancellationToken.None); }
                catch (ArgumentException) { rejectedFinal = true; }
                Check(rejectedFinal, "The best-effort API rejects final completion states so they cannot be silently skipped.");
                using (new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var timer = Stopwatch.StartNew();
                    var updated = await writer.TryWriteProgressAsync(new("project-opened"), CancellationToken.None);
                    Check(!updated && timer.Elapsed < TimeSpan.FromSeconds(2), "A locked progress state is skipped promptly, not treated as fatal completion failure.");
                    updated = await writer.TryWriteProgressAsync(new("exporting"), CancellationToken.None);
                    Check(!updated, "Later progress remains best-effort while the lock persists.");
                    using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(75));
                    var stopped = false;
                    try { await writer.TryWriteProgressAsync(new("cancel"), cancel.Token); }
                    catch (OperationCanceledException) { stopped = true; }
                    Check(stopped, "Best-effort progress still propagates cancellation.");
                    var failed = false;
                    try { await writer.WriteAsync(new("completed"), CancellationToken.None); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; }
                    Check(failed, "Final completion remains mandatory and fails closed under a persistent lock.");
                }
                await writer.WriteAsync(new("completed"), CancellationToken.None);
                using var state = sandbox.OpenStateOutput(statePath, 4 * 1024 * 1024);
                Check(JsonSerializer.Deserialize<State>(state, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Phase == "completed",
                    "Final completion succeeds after release even when earlier progress notifications were skipped.");
                Check(!Directory.EnumerateFiles(sandbox.WorkDirectory, "*.tmp").Any(), "Skipped, cancelled and failed notifications clean operation-owned temporary files.");
            }
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(root, "failure.txt"), error.ToString()); throw; }
        finally { Environment.CurrentDirectory = originalDirectory; await File.WriteAllLinesAsync(Path.Combine(root, "checks.txt"), checks); }
        Console.WriteLine($"Audit review checks passed: {checks.Count(c => c.StartsWith("PASS:"))}; skips: {checks.Count(c => c.StartsWith("SKIP:"))}. {root}");
    }

    // A junction exercises real reparse points without requiring symlink privilege.
    // Only fresh descendants of this diagnostic's output directory may be used.
    private static void CreateFixtureJunction(string fixtureRoot, string link, string target)
    {
        fixtureRoot = Path.GetFullPath(fixtureRoot);
        link = Path.GetFullPath(link); target = Path.GetFullPath(target);
        if (!link.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !target.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(link))
            throw new IOException("Junction fixture escaped its fresh output directory.");
        Directory.CreateDirectory(link);
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        var printable = Encoding.Unicode.GetBytes(target);
        var buffer = new byte[16 + substitute.Length + printable.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 0xA0000003); // IO_REPARSE_TAG_MOUNT_POINT
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), checked((ushort)(buffer.Length - 8)));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), checked((ushort)substitute.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), checked((ushort)(substitute.Length + 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14), checked((ushort)printable.Length));
        substitute.CopyTo(buffer, 16); printable.CopyTo(buffer, 16 + substitute.Length + 2);
        using var handle = CreateFile(link, 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create the junction containment fixture.");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[] input, int size, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
}
