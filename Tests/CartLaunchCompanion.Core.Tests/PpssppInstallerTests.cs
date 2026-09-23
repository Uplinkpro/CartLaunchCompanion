using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed partial class PpssppInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-install-" + Guid.NewGuid().ToString("N"));
    private static byte[] Zip(string name = "PPSSPPWindows64.exe", bool link = false, string contents = "fixture")
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry(name);
            if (link) entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(contents);
        }
        return stream.ToArray();
    }
    private static EmulatorRelease Release(byte[] bytes, PlatformKind platform = PlatformKind.Windows) => new()
    {
        EmulatorId = "ppsspp", ChannelId = "stable", Platform = platform, Architecture = Architecture.X64,
        Version = "v1.20.4", RevisionId = "fixture", PublishedAt = DateTimeOffset.UtcNow, IsPrerelease = false,
        ReleasePage = new("https://github.com/hrydgard/ppsspp/releases/tag/v1.20.4"),
        AssetName = platform == PlatformKind.Windows ? "PPSSPP-v1.20.4-Windows-x64.zip" : "PPSSPP-v1.20.4-anylinux-x86_64.AppImage",
        DownloadUrl = new("https://github.com/hrydgard/ppsspp/releases/download/v1.20.4/" +
            (platform == PlatformKind.Windows ? "PPSSPP-v1.20.4-Windows-x64.zip" : "PPSSPP-v1.20.4-anylinux-x86_64.AppImage")),
        SizeBytes = bytes.Length, PackageFormat = platform == PlatformKind.Windows ? EmulatorPackageFormat.Zip : EmulatorPackageFormat.AppImage,
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
    };
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; token.ThrowIfCancellationRequested(); return Task.FromResult(response()); }
    }
    private PpssppInstaller Installer(byte[] bytes, IEmulatorRegistryStore? store = null, Action<string>? checkpoint = null) =>
        new(_root, store ?? new EmulatorRegistryStore(_root), new HttpClient(new Handler(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }))) { Checkpoint = checkpoint };
    private string Destination => Path.Combine(_root, "Emulators", "Windows", "PPSSPP");
    private void AssertClean()
    {
        Assert.False(Directory.Exists(Destination));
        var parent = Path.GetDirectoryName(Destination)!;
        if (Directory.Exists(parent)) Assert.Empty(Directory.GetDirectories(parent, ".ppsspp-*"));
    }

    [Fact]
    public async Task VerifiedWindowsPackageIsInstalledAndRegistered()
    {
        var bytes = Zip();
        using var installer = Installer(bytes);
        var result = await installer.InstallAsync(Release(bytes));
        Assert.Equal("fixture", await File.ReadAllTextAsync(Path.Combine(Destination, "PPSSPPWindows64.exe")));
        Assert.Equal("v1.20.4", result.InstalledVersion);
        Assert.Equal(result, Assert.Single((await new EmulatorRegistryStore(_root).LoadAsync()).Installations));
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("C:/escape.exe")]
    [InlineData("folder/../../escape.exe")]
    [InlineData("folder\\escape.exe")]
    [InlineData("CON")]
    public async Task UnsafeArchivePathsNeverPublish(string name)
    {
        var bytes = Zip(name);
        using var installer = Installer(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Release(bytes)));
        AssertClean();
    }

    [Fact]
    public async Task SymbolicLinkIsRejected()
    {
        var bytes = Zip("PPSSPPWindows64.exe", true);
        using var installer = Installer(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Release(bytes)));
        AssertClean();
    }

    [Fact]
    public async Task DigestMismatchNeverPublishes()
    {
        var bytes = Zip();
        using var installer = Installer(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Release(bytes) with { Sha256 = new string('0', 64) }));
        AssertClean();
    }

    [Fact]
    public async Task SizeMismatchNeverPublishes()
    {
        var bytes = Zip();
        using var installer = Installer(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Release(bytes) with { SizeBytes = bytes.Length + 1 }));
        AssertClean();
    }

    [Fact]
    public async Task MissingChecksumDoesNotRequestDownload()
    {
        var handler = new Handler(() => throw new Exception("Must not request."));
        using var installer = new PpssppInstaller(_root, new EmulatorRegistryStore(_root), new HttpClient(handler));
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Release(Zip()) with { Sha256 = null }));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ExistingSettingsAreUntouched()
    {
        Directory.CreateDirectory(Destination);
        var settings = Path.Combine(Destination, "settings.ini");
        await File.WriteAllTextAsync(settings, "keep");
        using var installer = Installer(Zip());
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(Release(Zip())));
        Assert.Equal("keep", await File.ReadAllTextAsync(settings));
    }

    [Fact]
    public async Task ExistingPortableFolderIsAdoptedAndUserDataIsPreserved()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "assets"));
        Directory.CreateDirectory(Path.Combine(Destination, "memstick", "PSP", "SAVEDATA"));
        await File.WriteAllTextAsync(Path.Combine(Destination, "PPSSPPWindows64.exe"), "older");
        await File.WriteAllTextAsync(Path.Combine(Destination, "memstick", "PSP", "SAVEDATA", "save.dat"), "keep-save");
        var bytes = Zip(contents: "current");
        using var installer = Installer(bytes);
        var status = await installer.InspectAsync(Release(bytes));
        Assert.Equal(EmulatorInstallAction.Install, status.Action);
        Assert.Contains("existing PPSSPP", status.Message, StringComparison.OrdinalIgnoreCase);

        await installer.InstallAsync(Release(bytes));

        Assert.Equal("current", await File.ReadAllTextAsync(Path.Combine(Destination, "PPSSPPWindows64.exe")));
        Assert.Equal("keep-save", await File.ReadAllTextAsync(Path.Combine(Destination, "memstick", "PSP", "SAVEDATA", "save.dat")));
        Assert.Equal("ppsspp", Assert.Single((await new EmulatorRegistryStore(_root).LoadAsync()).Installations).EmulatorId);
    }

    [Fact]
    public async Task EmptyLinuxFolderDoesNotBlockInstallation()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Emulators", "Linux", "PPSSPP"));
        byte[] bytes = [0x7f, 0x45, 0x4c, 0x46];
        using var installer = Installer(bytes);
        var release = Release(bytes, PlatformKind.Linux);
        Assert.Equal(EmulatorInstallAction.Install, (await installer.InspectAsync(release)).Action);
        await installer.InstallAsync(release);
        Assert.True(File.Exists(Path.Combine(_root, "Emulators", "Linux", "PPSSPP", "PPSSPP.AppImage")));
    }

    [Fact]
    public async Task CancellationDoesNotPublish()
    {
        var bytes = Zip();
        using var installer = Installer(bytes);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(Release(bytes), cancelled.Token));
        AssertClean();
    }

    [Fact]
    public async Task UntrustedRedirectIsRejected()
    {
        var handler = new Handler(() => { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new("https://example.com/package"); return response; });
        using var installer = new PpssppInstaller(_root, new EmulatorRegistryStore(_root), new HttpClient(handler));
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(Release(Zip())));
        AssertClean();
    }

    private sealed class FailingRegistry : IEmulatorRegistryStore
    {
        public Task<EmulatorRegistry> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(EmulatorRegistry.Empty);
        public Task UpsertAsync(EmulatorInstallation installation, CancellationToken cancellationToken = default) => throw new IOException("Registry unavailable");
        public Task RemoveAsync(string id, PlatformKind platform, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    [Fact]
    public async Task RegistryFailureRollsBackNewFiles()
    {
        var bytes = Zip();
        using var installer = Installer(bytes, new FailingRegistry());
        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(Release(bytes)));
        AssertClean();
    }

    [Fact]
    public async Task RegistryFailureRestoresAdoptedPortableFolder()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "assets"));
        await File.WriteAllTextAsync(Path.Combine(Destination, "PPSSPPWindows64.exe"), "older");
        await File.WriteAllTextAsync(Path.Combine(Destination, "settings.ini"), "keep");
        var bytes = Zip(contents: "current");
        using var installer = Installer(bytes, new FailingRegistry());

        await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(Release(bytes)));

        Assert.Equal("older", await File.ReadAllTextAsync(Path.Combine(Destination, "PPSSPPWindows64.exe")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(Destination, "settings.ini")));
    }

    [Fact]
    public async Task AppImageUsesStableNameAndPortableDirectories()
    {
        byte[] bytes = [0x7f, 0x45, 0x4c, 0x46];
        using var installer = Installer(bytes);
        var result = await installer.InstallAsync(Release(bytes, PlatformKind.Linux));
        var path = EmulatorPathContract.Resolve(_root, result.ExecutableRelativePath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.True(Directory.Exists(path + ".home"));
        Assert.True(Directory.Exists(path + ".config"));
        if (OperatingSystem.IsLinux()) Assert.True(File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
