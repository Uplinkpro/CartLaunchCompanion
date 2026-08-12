using System.Security.Cryptography;
using CartLaunchCompanion.Core.Updating;

namespace CartLaunchCompanion.Core.PhysicalCarts;

public sealed record PreparedCartRuntime(
    string SessionRoot, string ExecutablePath, string CartRoot,
    string Platform, string CartId, string RuntimeFingerprint);

public sealed class TrustedRuntimeStagingService
{
    public async Task<IReadOnlyList<TrustedRuntimeApproval>> CreateApprovalsAsync(
        string mediaRoot, CancellationToken cancellationToken = default)
    {
        var cartRoot = ResolveCartRoot(mediaRoot);
        var approvals = new List<TrustedRuntimeApproval>();
        foreach (var platform in new[] { "Windows-x64", "Linux-x64" })
        {
            var runtime = Path.Combine(cartRoot, "System", platform);
            if (!Directory.Exists(runtime)) continue;
            var files = await InventoryAsync(runtime, cancellationToken);
            var entry = platform == "Windows-x64" ? "CartLaunchCompanion.Desktop.exe" : "CartLaunchCompanion.Desktop";
            if (!files.Any(file => file.Path == entry)) throw new InvalidDataException($"The {platform} CLC launcher is missing.");
            approvals.Add(new TrustedRuntimeApproval
            {
                Platform = platform,
                EntryPoint = entry,
                Files = files,
                RootFingerprint = RuntimeIntegrityVerifier.ComputeRootFingerprint(files)
            });
        }
        if (approvals.Count == 0) throw new InvalidDataException("The cart has no supported CLC runtime to approve.");
        return approvals;
    }

    public async Task<PreparedCartRuntime> PrepareAsync(
        string mediaRoot, VerifiedCartIdentity identity, TrustedCartDatabase trust,
        string platform, string sessionsRoot, CancellationToken cancellationToken = default)
    {
        if (!TrustedCartStore.IsTrusted(trust, identity)) throw new InvalidDataException("The cart identity is not trusted on this computer.");
        var record = trust.Carts.Single(item => string.Equals(item.CartId, identity.Identity.CartId, StringComparison.OrdinalIgnoreCase));
        var approval = record.RuntimeApprovals.SingleOrDefault(item => item.Platform == platform)
            ?? throw new InvalidDataException($"The {platform} runtime was not approved when this cart was trusted.");
        var source = Path.Combine(ResolveCartRoot(mediaRoot), "System", platform);
        await VerifyApprovalAsync(source, approval, cancellationToken);

        var sessions = Path.GetFullPath(sessionsRoot);
        Directory.CreateDirectory(sessions);
        RejectLink(new DirectoryInfo(sessions), "The protected sessions folder cannot be a link or junction.");
        var session = Path.Combine(sessions, $"{identity.Identity.CartId}-{platform}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(session);
        try
        {
            foreach (var file in approval.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var inputPath = RuntimePathPolicy.ResolveContainedFile(source, file.Path);
                var outputPath = Path.Combine(session, file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
                await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
                await input.CopyToAsync(output, cancellationToken);
            }
            await VerifyApprovalAsync(session, approval, cancellationToken);
            var executable = RuntimePathPolicy.ResolveContainedFile(session, approval.EntryPoint);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return new(session, executable, Path.Combine(Path.GetFullPath(mediaRoot), "Cart"), platform, identity.Identity.CartId, approval.RootFingerprint);
        }
        catch
        {
            if (Directory.Exists(session)) Directory.Delete(session, recursive: true);
            throw;
        }
    }

    public static void DeleteSession(PreparedCartRuntime prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (Directory.Exists(prepared.SessionRoot)) Directory.Delete(prepared.SessionRoot, recursive: true);
    }

    private static async Task<List<RuntimeUpdateFile>> InventoryAsync(string root, CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(root);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Runtime links are not allowed.");
        var paths = directory.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).ToArray();
        if (paths.Any(item => (item.Attributes & FileAttributes.ReparsePoint) != 0)) throw new InvalidDataException("Runtime links are not allowed.");
        var files = paths.OfType<FileInfo>().OrderBy(item => Path.GetRelativePath(root, item.FullName), StringComparer.Ordinal).ToArray();
        if (files.Length is 0 or > RuntimeIntegrityVerifier.MaximumFiles) throw new InvalidDataException("The runtime file count is invalid.");
        var result = new List<RuntimeUpdateFile>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = file.OpenRead();
            result.Add(new RuntimeUpdateFile
            {
                Path = Path.GetRelativePath(root, file.FullName).Replace('\\', '/'),
                Length = file.Length,
                Sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken))
            });
        }
        return result;
    }

    private static async Task VerifyApprovalAsync(string root, TrustedRuntimeApproval approval, CancellationToken cancellationToken)
    {
        var manifest = new RuntimeUpdateManifest
        {
            Version = "locally-approved",
            Platform = approval.Platform,
            EntryPoint = approval.EntryPoint,
            RootFingerprint = approval.RootFingerprint,
            Files = approval.Files
        };
        await new RuntimeIntegrityVerifier().VerifyAsync(root, manifest, cancellationToken);
    }

    private static string ResolveCartRoot(string mediaRoot)
    {
        var media = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mediaRoot));
        var cart = Path.Combine(media, "Cart");
        if (!Directory.Exists(cart)) throw new DirectoryNotFoundException("The media root does not contain a Cart folder.");
        RejectLink(new DirectoryInfo(cart), "The cart data folder cannot be a link or junction.");
        return cart;
    }

    private static void RejectLink(FileSystemInfo info, string message)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new InvalidDataException(message);
    }
}
