using System.Security.Cryptography;
using System.Text.Json;
using CartLaunchCompanion.Core.Updating;

namespace CartLaunchCompanion.Core.Tests;

public sealed class RuntimeUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CLC-UpdateTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task IntegrityVerifier_AcceptsExactPayload()
    {
        var payload = CreatePayload("Windows-x64", "new runtime");
        var manifest = await CreateManifestAsync(payload, "Windows-x64", "2.3.0");

        await new RuntimeIntegrityVerifier().VerifyAsync(payload, manifest);
    }

    [Fact]
    public async Task IntegrityVerifier_RejectsChangedFile()
    {
        var payload = CreatePayload("Windows-x64", "new runtime");
        var manifest = await CreateManifestAsync(payload, "Windows-x64", "2.3.0");
        await File.WriteAllTextAsync(
            Path.Combine(payload, "CartLaunchCompanion.Desktop.exe"),
            "tampered runtime");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new RuntimeIntegrityVerifier().VerifyAsync(payload, manifest));

        Assert.Contains("mismatch", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IntegrityVerifier_RejectsUnexpectedFile()
    {
        var payload = CreatePayload("Windows-x64", "new runtime");
        var manifest = await CreateManifestAsync(payload, "Windows-x64", "2.3.0");
        await File.WriteAllTextAsync(Path.Combine(payload, "unexpected.exe"), "unlisted");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new RuntimeIntegrityVerifier().VerifyAsync(payload, manifest));

        Assert.Contains("Unexpected", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("/tmp/outside")]
    [InlineData("C:/Windows/System32/cmd.exe")]
    [InlineData("//server/share/tool.exe")]
    public void PathPolicy_RejectsEscapes(string path)
    {
        Directory.CreateDirectory(_root);
        Assert.Throws<InvalidDataException>(() =>
            RuntimePathPolicy.ResolveContainedFile(_root, path));
    }

    [Fact]
    public async Task ManifestJson_RejectsUnknownSecurityField()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "manifest.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "FormatVersion": 1,
              "SecurityVersion": 1,
              "Product": "Cart Launch Companion",
              "Version": "2.3.0",
              "Platform": "Windows-x64",
              "EntryPoint": "CartLaunchCompanion.Desktop.exe",
              "RootFingerprint": "00",
              "SignerKeyId": "test",
              "Signature": "test",
              "Files": [],
              "ExecuteThisInstead": "cmd.exe"
            }
            """);

        await Assert.ThrowsAsync<JsonException>(() =>
            RuntimeUpdateManifestJson.LoadAsync(path));
    }

    [Fact]
    public async Task ManifestJson_AcceptsProductionSizedManifest()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "large-manifest.json");
        var files = Enumerable.Range(0, 2_000)
            .Select(index => new RuntimeUpdateFile
            {
                Path = $"runtime/file-{index:D4}.dll",
                Length = index + 1,
                Sha256 = new string('a', 64)
            })
            .ToList();
        var manifest = new RuntimeUpdateManifest
        {
            Version = "2.3.0",
            Platform = "Windows-x64",
            EntryPoint = "CartLaunchCompanion.Desktop.exe",
            RootFingerprint = new string('b', 64),
            SignerKeyId = "test",
            Signature = "dGVzdA==",
            Files = files
        };

        await RuntimeUpdateManifestJson.SaveAsync(path, manifest);
        Assert.InRange(new FileInfo(path).Length, 64 * 1024 + 1, RuntimeUpdateManifestJson.MaximumManifestBytes);

        var loaded = await RuntimeUpdateManifestJson.LoadAsync(path);
        Assert.Equal(files.Count, loaded.Files.Count);
    }

    [Fact]
    public async Task TrustedSignatureVerifier_AcceptsCurrentAndSuccessorKeys()
    {
        using var currentKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var successorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = new TrustedUpdateSignatureVerifier(
            new Dictionary<string, string>
            {
                ["release-current"] = currentKey.ExportSubjectPublicKeyInfoPem(),
                ["release-successor"] = successorKey.ExportSubjectPublicKeyInfoPem()
            });
        var payload = CreatePayload("Windows-x64", "new runtime");

        var currentManifest = await CreateManifestAsync(payload, "Windows-x64", "2.3.0");
        SignManifest(currentManifest, "release-current", currentKey);
        var successorManifest = await CreateManifestAsync(payload, "Windows-x64", "2.4.0");
        SignManifest(successorManifest, "release-successor", successorKey);

        Assert.True(verifier.Verify(currentManifest));
        Assert.True(verifier.Verify(successorManifest));
    }

    [Fact]
    public async Task TrustedSignatureVerifier_RejectsUnknownOrMismatchedKeyId()
    {
        using var trustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = new TrustedUpdateSignatureVerifier(
            new Dictionary<string, string>
            {
                ["release-trusted"] = trustedKey.ExportSubjectPublicKeyInfoPem()
            });
        var payload = CreatePayload("Windows-x64", "new runtime");

        var unknownManifest = await CreateManifestAsync(payload, "Windows-x64", "2.3.0");
        SignManifest(unknownManifest, "release-unknown", otherKey);
        var mismatchedManifest = await CreateManifestAsync(payload, "Windows-x64", "2.3.0");
        SignManifest(mismatchedManifest, "release-trusted", otherKey);

        Assert.False(verifier.Verify(unknownManifest));
        Assert.False(verifier.Verify(mismatchedManifest));
    }

    [Fact]
    public async Task TransactionalUpdater_ActivatesAndCanRollBack()
    {
        var cart = Path.Combine(_root, "Cart");
        var active = Path.Combine(cart, "System", "Windows-x64");
        Directory.CreateDirectory(active);
        var activeEntryPoint = Path.Combine(active, "CartLaunchCompanion.Desktop.exe");
        File.Copy(typeof(RuntimeUpdateTests).Assembly.Location, activeEntryPoint);

        var staging = Path.Combine(
            cart,
            ".cartlaunch",
            "update-staging",
            "Windows-x64-payload");
        Directory.CreateDirectory(staging);
        await File.WriteAllTextAsync(
            Path.Combine(staging, "CartLaunchCompanion.Desktop.exe"),
            "new runtime");
        var manifest = await CreateManifestAsync(staging, "Windows-x64", "2.4.0");
        manifest.SignerKeyId = "test";
        manifest.Signature = "dGVzdA==";
        var manifestPath = Path.Combine(
            cart,
            ".cartlaunch",
            "update-staging",
            "update-manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest));

        var updater = new TransactionalRuntimeUpdater(
            new RuntimeIntegrityVerifier(),
            new AlwaysTrustedSignatureVerifier());
        await updater.ApplyAsync(new RuntimeUpdateRequest(
            cart,
            "Windows-x64",
            staging,
            manifestPath));

        Assert.Equal(
            "new runtime",
            await File.ReadAllTextAsync(Path.Combine(active, "CartLaunchCompanion.Desktop.exe")));

        TransactionalRuntimeUpdater.RollBackActivatedUpdate(cart, "Windows-x64");

        Assert.Equal(
            await File.ReadAllBytesAsync(typeof(RuntimeUpdateTests).Assembly.Location),
            await File.ReadAllBytesAsync(Path.Combine(active, "CartLaunchCompanion.Desktop.exe")));
    }

    [Theory]
    [InlineData("2.3.0")]
    [InlineData("2.2.9")]
    public async Task TransactionalUpdater_RejectsReplayOrDowngrade(string updateVersion)
    {
        var cart = Path.Combine(_root, "Cart");
        var active = Path.Combine(cart, "System", "Windows-x64");
        Directory.CreateDirectory(active);
        File.Copy(
            typeof(RuntimeUpdateTests).Assembly.Location,
            Path.Combine(active, "CartLaunchCompanion.Desktop.exe"));
        var staging = Path.Combine(cart, ".cartlaunch", "update-staging", "payload");
        Directory.CreateDirectory(staging);
        await File.WriteAllTextAsync(
            Path.Combine(staging, "CartLaunchCompanion.Desktop.exe"),
            "signed older runtime");
        var manifest = await CreateManifestAsync(staging, "Windows-x64", updateVersion);
        manifest.SignerKeyId = "test";
        manifest.Signature = "dGVzdA==";
        var manifestPath = Path.Combine(cart, ".cartlaunch", "update-staging", "manifest.json");
        await RuntimeUpdateManifestJson.SaveAsync(manifestPath, manifest);

        var updater = new TransactionalRuntimeUpdater(
            new RuntimeIntegrityVerifier(),
            new AlwaysTrustedSignatureVerifier());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            updater.ApplyAsync(new RuntimeUpdateRequest(cart, "Windows-x64", staging, manifestPath)));

        Assert.Contains("must be newer", error.Message);
        Assert.True(File.Exists(Path.Combine(active, "CartLaunchCompanion.Desktop.exe")));
    }

    [Fact]
    public async Task TransactionalUpdater_RejectsUnverifiableInstalledVersion()
    {
        var cart = Path.Combine(_root, "Cart");
        var active = Path.Combine(cart, "System", "Windows-x64");
        Directory.CreateDirectory(active);
        await File.WriteAllTextAsync(
            Path.Combine(active, "CartLaunchCompanion.Desktop.exe"),
            "not a versioned executable");
        var staging = Path.Combine(cart, ".cartlaunch", "update-staging", "payload");
        Directory.CreateDirectory(staging);
        await File.WriteAllTextAsync(
            Path.Combine(staging, "CartLaunchCompanion.Desktop.exe"),
            "new runtime");
        var manifest = await CreateManifestAsync(staging, "Windows-x64", "2.4.0");
        manifest.SignerKeyId = "test";
        manifest.Signature = "dGVzdA==";
        var manifestPath = Path.Combine(cart, ".cartlaunch", "update-staging", "manifest.json");
        await RuntimeUpdateManifestJson.SaveAsync(manifestPath, manifest);

        var updater = new TransactionalRuntimeUpdater(
            new RuntimeIntegrityVerifier(),
            new AlwaysTrustedSignatureVerifier());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            updater.ApplyAsync(new RuntimeUpdateRequest(cart, "Windows-x64", staging, manifestPath)));

        Assert.Contains("cannot be verified", error.Message);
    }

    [Fact]
    public async Task TransactionalUpdater_RejectsMalformedSignedVersion()
    {
        var cart = Path.Combine(_root, "Cart");
        var active = Path.Combine(cart, "System", "Windows-x64");
        Directory.CreateDirectory(active);
        File.Copy(
            typeof(RuntimeUpdateTests).Assembly.Location,
            Path.Combine(active, "CartLaunchCompanion.Desktop.exe"));
        var staging = Path.Combine(cart, ".cartlaunch", "update-staging", "payload");
        Directory.CreateDirectory(staging);
        await File.WriteAllTextAsync(
            Path.Combine(staging, "CartLaunchCompanion.Desktop.exe"),
            "new runtime");
        var manifest = await CreateManifestAsync(staging, "Windows-x64", "release-latest");
        manifest.SignerKeyId = "test";
        manifest.Signature = "dGVzdA==";
        var manifestPath = Path.Combine(cart, ".cartlaunch", "update-staging", "manifest.json");
        await RuntimeUpdateManifestJson.SaveAsync(manifestPath, manifest);

        var updater = new TransactionalRuntimeUpdater(
            new RuntimeIntegrityVerifier(),
            new AlwaysTrustedSignatureVerifier());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            updater.ApplyAsync(new RuntimeUpdateRequest(cart, "Windows-x64", staging, manifestPath)));

        Assert.Contains("manifest version is invalid", error.Message);
    }

    [Fact]
    public async Task Recovery_RestoresRuntimeMovedBeforeActivation()
    {
        var cart = Path.Combine(_root, "Cart");
        var maintenance = Path.Combine(cart, ".cartlaunch");
        var backup = Path.Combine(maintenance, "previous-runtime", "Windows-x64");
        Directory.CreateDirectory(backup);
        await File.WriteAllTextAsync(
            Path.Combine(backup, "CartLaunchCompanion.Desktop.exe"),
            "old runtime");
        Directory.CreateDirectory(maintenance);
        await File.WriteAllTextAsync(
            Path.Combine(maintenance, "update-journal.json"),
            JsonSerializer.Serialize(new RuntimeUpdateJournal
            {
                Platform = "Windows-x64",
                State = RuntimeUpdateState.ActiveMovedToBackup
            }));

        var updater = new TransactionalRuntimeUpdater(
            new RuntimeIntegrityVerifier(),
            new AlwaysTrustedSignatureVerifier());
        await updater.RecoverInterruptedUpdateAsync(cart);

        Assert.True(File.Exists(Path.Combine(
            cart,
            "System",
            "Windows-x64",
            "CartLaunchCompanion.Desktop.exe")));
        Assert.False(File.Exists(Path.Combine(maintenance, "update-journal.json")));
    }

    [Theory]
    [InlineData(RuntimeUpdateState.Prepared)]
    [InlineData(RuntimeUpdateState.ActiveMovedToBackup)]
    [InlineData(RuntimeUpdateState.NewRuntimeActivated)]
    [InlineData(RuntimeUpdateState.Restarted)]
    public async Task Recovery_RestoresPreviousRuntimeUntilHealthIsConfirmed(
        RuntimeUpdateState state)
    {
        var cart = Path.Combine(_root, "Cart");
        var active = Path.Combine(cart, "System", "Windows-x64");
        var maintenance = Path.Combine(cart, ".cartlaunch");
        var backup = Path.Combine(maintenance, "previous-runtime", "Windows-x64");
        Directory.CreateDirectory(active);
        Directory.CreateDirectory(backup);
        await File.WriteAllTextAsync(
            Path.Combine(active, "CartLaunchCompanion.Desktop.exe"),
            "unconfirmed runtime");
        await File.WriteAllTextAsync(
            Path.Combine(backup, "CartLaunchCompanion.Desktop.exe"),
            "known-good runtime");
        await File.WriteAllTextAsync(
            Path.Combine(maintenance, "update-journal.json"),
            JsonSerializer.Serialize(new RuntimeUpdateJournal
            {
                Platform = "Windows-x64",
                State = state
            }));

        var updater = new TransactionalRuntimeUpdater(
            new RuntimeIntegrityVerifier(),
            new AlwaysTrustedSignatureVerifier());
        await updater.RecoverInterruptedUpdateAsync(cart);

        Assert.Equal(
            "known-good runtime",
            await File.ReadAllTextAsync(Path.Combine(
                active,
                "CartLaunchCompanion.Desktop.exe")));
        Assert.False(Directory.Exists(backup));
        Assert.False(File.Exists(Path.Combine(maintenance, "update-journal.json")));
    }

    [Fact]
    public async Task Recovery_RejectsUnconfirmedRuntimeWhenBackupIsMissing()
    {
        var cart = Path.Combine(_root, "Cart");
        var maintenance = Path.Combine(cart, ".cartlaunch");
        Directory.CreateDirectory(maintenance);
        await File.WriteAllTextAsync(
            Path.Combine(maintenance, "update-journal.json"),
            JsonSerializer.Serialize(new RuntimeUpdateJournal
            {
                Platform = "Windows-x64",
                State = RuntimeUpdateState.NewRuntimeActivated
            }));

        var updater = new TransactionalRuntimeUpdater(
            new RuntimeIntegrityVerifier(),
            new AlwaysTrustedSignatureVerifier());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            updater.RecoverInterruptedUpdateAsync(cart));
        Assert.Contains("previous runtime is missing", error.Message);
    }

    private string CreatePayload(string platform, string content)
    {
        var payload = Path.Combine(_root, platform, "payload");
        Directory.CreateDirectory(payload);
        var entryPoint = platform == "Windows-x64"
            ? "CartLaunchCompanion.Desktop.exe"
            : "CartLaunchCompanion.Desktop";
        File.WriteAllText(Path.Combine(payload, entryPoint), content);
        return payload;
    }

    private static async Task<RuntimeUpdateManifest> CreateManifestAsync(
        string payload,
        string platform,
        string version)
    {
        var files = new List<RuntimeUpdateFile>();
        foreach (var path in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories))
        {
            await using var stream = File.OpenRead(path);
            files.Add(new RuntimeUpdateFile
            {
                Path = Path.GetRelativePath(payload, path).Replace('\\', '/'),
                Length = stream.Length,
                Sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream))
            });
        }

        return new RuntimeUpdateManifest
        {
            Version = version,
            Platform = platform,
            EntryPoint = platform == "Windows-x64"
                ? "CartLaunchCompanion.Desktop.exe"
                : "CartLaunchCompanion.Desktop",
            Files = files,
            RootFingerprint = RuntimeIntegrityVerifier.ComputeRootFingerprint(files)
        };
    }

    private static void SignManifest(
        RuntimeUpdateManifest manifest,
        string keyId,
        ECDsa signingKey)
    {
        manifest.SignerKeyId = keyId;
        manifest.Signature = Convert.ToBase64String(signingKey.SignData(
            RuntimeUpdateManifestJson.GetUnsignedCanonicalBytes(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class AlwaysTrustedSignatureVerifier : IUpdateSignatureVerifier
    {
        public bool Verify(RuntimeUpdateManifest manifest) => true;
    }
}
