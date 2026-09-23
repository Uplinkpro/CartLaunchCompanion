using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class DuckStationInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-duckstation-" + Guid.NewGuid().ToString("N"));

    private static byte[] Zip(string contents = "fixture")
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var executable = archive.CreateEntry("duckstation-qt-x64-ReleaseLTCG.exe");
            using (var writer = new StreamWriter(executable.Open())) writer.Write(contents);
            var resource = archive.CreateEntry("resources/database.bin");
            using var resourceWriter = new StreamWriter(resource.Open());
            resourceWriter.Write("resource");
        }
        return stream.ToArray();
    }

    private static EmulatorRelease Release(byte[] bytes, PlatformKind platform = PlatformKind.Windows,
        string version = "stable-2026.09.12.122020", string channel = "stable")
    {
        var preview = channel == "preview";
        var tag = preview ? "preview" : "latest";
        var asset = platform == PlatformKind.Windows ? "duckstation-windows-x64-release.zip" : "DuckStation-x64.AppImage";
        return new()
        {
            EmulatorId = "duckstation", ChannelId = channel, Platform = platform, Architecture = Architecture.X64,
            Version = version, RevisionId = "fixture", PublishedAt = DateTimeOffset.UtcNow, IsPrerelease = preview,
            ReleasePage = new($"https://github.com/stenzek/duckstation/releases/tag/{tag}"), AssetName = asset,
            DownloadUrl = new($"https://github.com/stenzek/duckstation/releases/download/{tag}/{asset}"),
            SizeBytes = bytes.Length,
            PackageFormat = platform == PlatformKind.Windows ? EmulatorPackageFormat.Zip : EmulatorPackageFormat.AppImage,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }

    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { RequestMessage = request, Content = new ByteArrayContent(bytes) });
        }
    }

    private DuckStationInstaller Installer(byte[] bytes, IEmulatorRegistryStore? registry = null)
    {
        var handler = new Handler(bytes);
        return new(_root, registry ?? new EmulatorRegistryStore(_root), new HttpClient(handler));
    }

    [Fact]
    public async Task VerifiedWindowsArchiveInstallsPortableBuildAndRegistryRecord()
    {
        var bytes = Zip();
        using var installer = Installer(bytes);
        var installation = await installer.InstallAsync(Release(bytes));
        var folder = Path.Combine(_root, "Emulators", "Windows", "DuckStation");
        Assert.Equal("fixture", await File.ReadAllTextAsync(Path.Combine(folder, "duckstation-qt-x64-ReleaseLTCG.exe")));
        Assert.True(File.Exists(Path.Combine(folder, "portable.txt")));
        Assert.Equal(installation, Assert.Single((await new EmulatorRegistryStore(_root).LoadAsync()).Installations));
    }

    [Fact]
    public async Task VerifiedLinuxAppImageUsesStablePortableLayout()
    {
        byte[] bytes = [0x7f, 0x45, 0x4c, 0x46];
        using var installer = Installer(bytes);
        var installation = await installer.InstallAsync(Release(bytes, PlatformKind.Linux,
            "stable-2026.09.12.122021"));
        var executable = EmulatorPathContract.Resolve(_root, installation.ExecutableRelativePath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(executable));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "portable.txt")));
        if (OperatingSystem.IsLinux()) Assert.True(File.GetUnixFileMode(executable).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public async Task UpdatePreservesPortableUserFiles()
    {
        var first = Zip("old");
        using (var installer = Installer(first)) await installer.InstallAsync(Release(first));
        var folder = Path.Combine(_root, "Emulators", "Windows", "DuckStation");
        Directory.CreateDirectory(Path.Combine(folder, "memcards"));
        await File.WriteAllTextAsync(Path.Combine(folder, "settings.ini"), "keep-settings");
        await File.WriteAllTextAsync(Path.Combine(folder, "memcards", "card.mcd"), "keep-save");

        var second = Zip("new");
        using (var installer = Installer(second))
            await installer.InstallAsync(Release(second, version: "stable-2026.09.21.045911"));

        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(folder, "duckstation-qt-x64-ReleaseLTCG.exe")));
        Assert.Equal("keep-settings", await File.ReadAllTextAsync(Path.Combine(folder, "settings.ini")));
        Assert.Equal("keep-save", await File.ReadAllTextAsync(Path.Combine(folder, "memcards", "card.mcd")));
    }

    [Fact]
    public async Task PublisherDigestIsRequiredBeforeDownload()
    {
        var bytes = Zip();
        using var installer = Installer(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Release(bytes) with { Sha256 = null }));
        Assert.False(Directory.Exists(Path.Combine(_root, "Emulators", "Windows", "DuckStation")));
    }

    [Fact]
    public async Task UnregisteredFolderIsLeftUntouched()
    {
        var folder = Path.Combine(_root, "Emulators", "Windows", "DuckStation");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "settings.ini"), "mine");
        var bytes = Zip();
        using var installer = Installer(bytes);
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(Release(bytes)));
        Assert.Equal("mine", await File.ReadAllTextAsync(Path.Combine(folder, "settings.ini")));
    }

    [Fact]
    public async Task ExistingPortableFolderIsAdoptedAndUserDataIsPreserved()
    {
        var folder = Path.Combine(_root, "Emulators", "Windows", "DuckStation");
        Directory.CreateDirectory(Path.Combine(folder, "memcards"));
        await File.WriteAllTextAsync(Path.Combine(folder, "duckstation-qt-x64-ReleaseLTCG.exe"), "older");
        await File.WriteAllBytesAsync(Path.Combine(folder, "portable.txt"), []);
        await File.WriteAllTextAsync(Path.Combine(folder, "settings.ini"), "keep-settings");
        await File.WriteAllTextAsync(Path.Combine(folder, "memcards", "card.mcd"), "keep-save");
        var bytes = Zip("current");
        using var installer = Installer(bytes);
        var status = await installer.InspectAsync(Release(bytes));
        Assert.Equal(EmulatorInstallAction.Install, status.Action);
        Assert.Contains("existing DuckStation", status.Message, StringComparison.OrdinalIgnoreCase);

        await installer.InstallAsync(Release(bytes));

        Assert.Equal("current", await File.ReadAllTextAsync(Path.Combine(folder, "duckstation-qt-x64-ReleaseLTCG.exe")));
        Assert.Equal("keep-settings", await File.ReadAllTextAsync(Path.Combine(folder, "settings.ini")));
        Assert.Equal("keep-save", await File.ReadAllTextAsync(Path.Combine(folder, "memcards", "card.mcd")));
        Assert.Equal("duckstation", Assert.Single((await new EmulatorRegistryStore(_root).LoadAsync()).Installations).EmulatorId);
    }

    [Fact]
    public async Task EmptyPlatformFolderDoesNotBlockInstallation()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Emulators", "Linux", "DuckStation"));
        byte[] bytes = [0x7f, 0x45, 0x4c, 0x46];
        using var installer = Installer(bytes);
        var release = Release(bytes, PlatformKind.Linux, "stable-2026.09.12.122036");
        Assert.Equal(EmulatorInstallAction.Install, (await installer.InspectAsync(release)).Action);
        await installer.InstallAsync(release);
        Assert.True(File.Exists(Path.Combine(_root, "Emulators", "Linux", "DuckStation", "DuckStation.AppImage")));
    }

    [Fact]
    public async Task RegistryFailureRollsBackPublishedFiles()
    {
        var bytes = Zip();
        using var installer = Installer(bytes, new FailingRegistry());
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(Release(bytes)));
        Assert.False(Directory.Exists(Path.Combine(_root, "Emulators", "Windows", "DuckStation")));
    }

    private sealed class FailingRegistry : IEmulatorRegistryStore
    {
        public Task<EmulatorRegistry> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EmulatorRegistry.Empty);
        public Task UpsertAsync(EmulatorInstallation installation, CancellationToken cancellationToken = default) =>
            throw new IOException("Registry unavailable");
        public Task RemoveAsync(string emulatorId, PlatformKind platform, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
