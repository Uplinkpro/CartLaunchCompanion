using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;

namespace CartLaunchCompanion.Core.Tests;

public sealed class ManagedPortableInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-managed-emulator-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ShadPs4ZipInstallsPortableUserFolderAndPreservesItOnUpdate()
    {
        var first = Zip("shadPS4.exe", "old");
        using (var installer = Shad(first)) await installer.InstallAsync(ShadRelease(first, "v0.18.0"));
        var folder = Path.Combine(_root, "Emulators", "Windows", "shadPS4");
        await File.WriteAllTextAsync(Path.Combine(folder, "user", "save.dat"), "keep");

        var second = Zip("shadPS4.exe", "new");
        using (var installer = Shad(second)) await installer.InstallAsync(ShadRelease(second, "v0.19.0"));

        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(folder, "shadPS4.exe")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(folder, "user", "save.dat")));
        Assert.Equal("shadps4", Assert.Single((await new EmulatorRegistryStore(_root).LoadAsync()).Installations).EmulatorId);
    }

    [Fact]
    public async Task Pcsx2LinuxAppImageUsesPortableHomeAndConfigFolders()
    {
        byte[] bytes = [0x7f, 0x45, 0x4c, 0x46];
        using var installer = new Pcsx2Installer(_root, new EmulatorRegistryStore(_root), new HttpClient(new Handler(bytes)));
        var release = Pcsx2LinuxRelease(bytes);
        Assert.Equal(EmulatorInstallAction.Install, (await installer.InspectAsync(release)).Action);
        var installation = await installer.InstallAsync(release);
        var executable = EmulatorPathContract.Resolve(_root, installation.ExecutableRelativePath);
        Assert.True(File.Exists(executable));
        Assert.True(Directory.Exists(executable + ".home"));
        Assert.True(Directory.Exists(executable + ".config"));
        if (OperatingSystem.IsLinux()) Assert.True(File.GetUnixFileMode(executable).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public async Task Rpcs3MissingRecordedProgramCanBeRepairedWithTheSameBuild()
    {
        var folder = Path.Combine(_root, "Emulators", "Linux", "RPCS3");
        var savedData = Path.Combine(folder, "RPCS3.AppImage.config", "rpcs3", "dev_hdd0", "home", "save.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(savedData)!);
        await File.WriteAllTextAsync(savedData, "keep-save");
        await new EmulatorRegistryStore(_root).UpsertAsync(new()
        {
            EmulatorId = "rpcs3", Platform = PlatformKind.Linux,
            ExecutableRelativePath = "Emulators/Linux/RPCS3/RPCS3.AppImage",
            InstalledVersion = "v0.0.42-20031", InstalledChannelId = "rolling", InstalledAt = DateTimeOffset.UtcNow
        });
        byte[] bytes = [0x7f, 0x45, 0x4c, 0x46];
        using var installer = new Rpcs3Installer(_root, new EmulatorRegistryStore(_root), new HttpClient(new Handler(bytes)));
        var release = Rpcs3LinuxRelease(bytes);

        var status = await installer.InspectAsync(release);
        Assert.Equal(EmulatorInstallAction.Update, status.Action);
        Assert.Contains("repair", status.Message, StringComparison.OrdinalIgnoreCase);

        await installer.InstallAsync(release);

        Assert.True(File.Exists(Path.Combine(folder, "RPCS3.AppImage")));
        Assert.Equal("keep-save", await File.ReadAllTextAsync(savedData));
    }

    private ShadPs4Installer Shad(byte[] bytes) =>
        new(_root, new EmulatorRegistryStore(_root), new HttpClient(new Handler(bytes)));

    private static EmulatorRelease ShadRelease(byte[] bytes, string version)
    {
        var tag = "v." + version[1..];
        var asset = $"shadps4-win64-sdl-{version[1..]}.zip";
        return new()
        {
            EmulatorId = "shadps4", ChannelId = "stable", Platform = PlatformKind.Windows, Architecture = Architecture.X64,
            Version = version, RevisionId = "fixture", PublishedAt = DateTimeOffset.UtcNow, IsPrerelease = false,
            ReleasePage = new($"https://github.com/shadps4-emu/shadPS4/releases/tag/{tag}"), AssetName = asset,
            DownloadUrl = new($"https://github.com/shadps4-emu/shadPS4/releases/download/{tag}/{asset}"),
            SizeBytes = bytes.Length, PackageFormat = EmulatorPackageFormat.Zip,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }

    private static EmulatorRelease Pcsx2LinuxRelease(byte[] bytes)
    {
        const string version = "v2.8.2";
        const string asset = "pcsx2-v2.8.2-linux-appimage-x64-Qt.AppImage";
        return new()
        {
            EmulatorId = "pcsx2", ChannelId = "stable", Platform = PlatformKind.Linux, Architecture = Architecture.X64,
            Version = version, RevisionId = "fixture", PublishedAt = DateTimeOffset.UtcNow, IsPrerelease = false,
            ReleasePage = new("https://github.com/PCSX2/pcsx2/releases/tag/v2.8.2"), AssetName = asset,
            DownloadUrl = new("https://github.com/PCSX2/pcsx2/releases/download/v2.8.2/" + asset),
            SizeBytes = bytes.Length, PackageFormat = EmulatorPackageFormat.AppImage,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }

    private static EmulatorRelease Rpcs3LinuxRelease(byte[] bytes)
    {
        const string version = "v0.0.42-20031";
        const string revision = "0123456789abcdef0123456789abcdef01234567";
        const string asset = "rpcs3-v0.0.42-20031-01234567_linux64.AppImage";
        return new()
        {
            EmulatorId = "rpcs3", ChannelId = "rolling", Platform = PlatformKind.Linux, Architecture = Architecture.X64,
            Version = version, RevisionId = revision, PublishedAt = DateTimeOffset.UtcNow, IsPrerelease = true,
            ReleasePage = new($"https://github.com/RPCS3/rpcs3-binaries-linux/releases/tag/build-{revision}"), AssetName = asset,
            DownloadUrl = new($"https://github.com/RPCS3/rpcs3-binaries-linux/releases/download/build-{revision}/{asset}"),
            SizeBytes = bytes.Length, PackageFormat = EmulatorPackageFormat.AppImage,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }

    private static byte[] Zip(string name, string contents)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write(contents);
        return stream.ToArray();
    }

    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
