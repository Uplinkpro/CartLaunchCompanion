using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Tests;

public sealed class GameContentImportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clc-import-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ShadPs4ImportsUpdateAndDlcWithoutRemovingSourcesAndRecordsCompletion()
    {
        var game = GameContentLayout.EnsureGame(_root, "PlayStation 4", "Example CUSA00001");
        var update = Path.Combine(game, "Updates", "CUSA00001-patch");
        var dlc = Path.Combine(game, "DLC", "DLC00001");
        Directory.CreateDirectory(Path.Combine(update, "sce_sys"));
        Directory.CreateDirectory(Path.Combine(dlc, "sce_sys"));
        await File.WriteAllTextAsync(Path.Combine(update, "sce_sys", "param.sfo"), "update");
        await File.WriteAllTextAsync(Path.Combine(dlc, "sce_sys", "param.sfo"), "dlc");
        var executable = Path.Combine(_root, "Emulators", "Windows", "shadPS4", "shadPS4.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllTextAsync(executable, "exe");
        var target = new PortableSetupTarget(PlatformKind.Windows, executable,
            Path.Combine(Path.GetDirectoryName(executable)!, "user", "config.json"), false);
        var service = new GameContentImportService(_root, _root, "shadps4");

        var preview = await service.PreviewAsync(PlatformKind.Windows);
        Assert.True(preview.CanImport);
        Assert.Equal(2, preview.Items.Count);

        await service.ImportAsync(target);

        Assert.True(File.Exists(Path.Combine(_root, "Emulators", "Shared", "shadPS4", "games",
            "CUSA00001-patch", "sce_sys", "param.sfo")));
        Assert.True(File.Exists(Path.Combine(_root, "Emulators", "Shared", "shadPS4", "addcont",
            "CUSA00001", "DLC00001", "sce_sys", "param.sfo")));
        Assert.True(File.Exists(Path.Combine(update, "sce_sys", "param.sfo")));
        Assert.Empty((await service.PreviewAsync(PlatformKind.Windows)).Items);
        Assert.Empty((await service.PreviewAsync(PlatformKind.Linux)).Items);
        Assert.True(File.Exists(Path.Combine(_root, "Config", "EmulatorCompanion", "ContentImports", "shadps4", "Shared.json")));
    }

    [Fact]
    public async Task Rpcs3UsesOfficialPackageInstallCommandAndDoesNotRepeatCompletedPackage()
    {
        var game = GameContentLayout.EnsureGame(_root, "PlayStation 3", "Example Game");
        var package = Path.Combine(game, "Updates", "UPDATE.pkg");
        await File.WriteAllTextAsync(package, "package");
        var executable = Path.Combine(_root, "Emulators", "Windows", "RPCS3", "rpcs3.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllTextAsync(executable, "exe");
        var runner = new RecordingRunner();
        var service = new GameContentImportService(_root, _root, "rpcs3", runner);
        var target = new PortableSetupTarget(PlatformKind.Windows, executable, "config.yml", false);

        await service.ImportAsync(target);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(executable, call.Executable);
        Assert.Equal(new[] { "--installpkg", package }, call.Arguments);
        Assert.Empty((await service.PreviewAsync(PlatformKind.Windows)).Items);
        Assert.Empty((await service.PreviewAsync(PlatformKind.Linux)).Items);
        Assert.True(File.Exists(Path.Combine(_root, "Config", "EmulatorCompanion", "ContentImports", "rpcs3", "Shared.json")));
        Assert.True(File.Exists(package));
    }

    [Fact]
    public async Task ShadPs4RejectsUpdateFolderWithoutOfficialTitleNaming()
    {
        var game = GameContentLayout.EnsureGame(_root, "PlayStation 4", "Example Game");
        Directory.CreateDirectory(Path.Combine(game, "Updates", "Patch"));
        var preview = await new GameContentImportService(_root, _root, "shadps4")
            .PreviewAsync(PlatformKind.Windows);

        Assert.False(preview.CanImport);
        Assert.Contains(preview.Problems, item => item.Contains("CUSA", StringComparison.Ordinal));
    }

    private sealed class RecordingRunner : IGameContentCommandRunner
    {
        public List<(string Executable, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
        {
            Calls.Add((executable, arguments));
            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
