using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Core.Emulators;

public enum GameContentKind { Update, Dlc }

public sealed record GameContentImportItem(
    string GameName, string? TitleId, GameContentKind Kind, string SourcePath, string DisplayName);

public sealed record GameContentImportPreview(
    IReadOnlyList<GameContentImportItem> Items, IReadOnlyList<string> Problems, string Guidance)
{
    public bool CanImport => Items.Count > 0 && Problems.Count == 0;
}

public interface IGameContentCommandRunner
{
    Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token);
}

public sealed class GameContentCommandRunner : IGameContentCommandRunner
{
    public async Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new IOException("The emulator content installer could not be started.");
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0)
            throw new IOException($"The emulator content installer exited with code {process.ExitCode}.");
    }
}

public sealed partial class GameContentImportService(
    string mediaRoot, string stateRoot, string emulatorId, IGameContentCommandRunner? commandRunner = null)
{
    private readonly string _mediaRoot = Path.GetFullPath(mediaRoot);
    private readonly string _stateRoot = Path.GetFullPath(stateRoot);
    private readonly string _emulatorId = emulatorId;
    private readonly IGameContentCommandRunner _commandRunner = commandRunner ?? new GameContentCommandRunner();
    private const int MaximumItems = 1000;

    [GeneratedRegex(@"CUSA\d{5}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Ps4TitleIdRegex();

    public bool IsSupported => _emulatorId is "rpcs3" or "shadps4";

    public async Task<GameContentImportPreview> PreviewAsync(PlatformKind platform, CancellationToken token = default)
    {
        if (!IsSupported)
            return new([], [], "This console does not use managed updates or DLC.");
        var platformFolder = _emulatorId == "rpcs3" ? "PlayStation 3" : "PlayStation 4";
        var root = Path.Combine(_mediaRoot, "Roms", platformFolder);
        if (!Directory.Exists(root))
            return new([], [], "No game library folder exists yet.");

        var items = new List<GameContentImportItem>();
        var problems = new List<string>();
        foreach (var game in SafeDirectories(root, problems))
        {
            token.ThrowIfCancellationRequested();
            ScanFolder(game, GameContentKind.Update, items, problems);
            ScanFolder(game, GameContentKind.Dlc, items, problems);
            if (items.Count > MaximumItems)
            {
                problems.Add($"More than {MaximumItems} content items were found. Split the import into smaller groups.");
                break;
            }
        }
        var receipts = await LoadReceiptsAsync(platform, token);
        items = items.Where(item => !receipts.TryGetValue(ReceiptKey(item), out var signature) ||
            signature != Signature(item.SourcePath)).ToList();
        var guidance = problems.Count > 0
            ? "Resolve the content-library problems below before importing."
            : items.Count == 0
                ? $"Place dumped updates and DLC inside each game's Updates and DLC folders."
                : $"{items.Count} update or DLC item{(items.Count == 1 ? " is" : "s are")} ready to import. Source files will be preserved.";
        return new(items, problems, guidance);
    }

    public async Task ImportAsync(PortableSetupTarget target, IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        var preview = await PreviewAsync(target.Platform, token);
        if (!preview.CanImport) throw new InvalidOperationException(preview.Guidance);
        if (_emulatorId == "rpcs3")
        {
            var host = OperatingSystem.IsLinux() ? PlatformKind.Linux : PlatformKind.Windows;
            if (target.Platform != host)
                throw new NotSupportedException($"RPCS3 packages must be imported with the {host} build on this computer.");
            for (var index = 0; index < preview.Items.Count; index++)
            {
                var item = preview.Items[index];
                progress?.Report($"Importing {index + 1} of {preview.Items.Count}: {item.DisplayName}");
                await _commandRunner.RunAsync(target.ExecutablePath, ["--installpkg", item.SourcePath], token);
                await SaveReceiptsAsync(target.Platform, [item], token);
            }
            return;
        }

        var sharedRoot = Path.Combine(_mediaRoot, "Emulators", "Shared", "shadPS4");
        foreach (var (item, index) in preview.Items.Select((item, index) => (item, index)))
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"Importing {index + 1} of {preview.Items.Count}: {item.DisplayName}");
            var destination = item.Kind == GameContentKind.Update
                ? Path.Combine(sharedRoot, "games", Path.GetFileName(item.SourcePath))
                : Path.GetFileName(item.SourcePath).Equals(item.TitleId, StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(sharedRoot, "addcont", item.TitleId!)
                    : Path.Combine(sharedRoot, "addcont", item.TitleId!, Path.GetFileName(item.SourcePath));
            await CopyDirectoryAsync(item.SourcePath, destination, token);
            await SaveReceiptsAsync(target.Platform, [item], token);
        }
    }

    private void ScanFolder(DirectoryInfo game, GameContentKind kind,
        List<GameContentImportItem> items, List<string> problems)
    {
        var source = Path.Combine(game.FullName,
            kind == GameContentKind.Update ? GameContentLayout.UpdatesFolderName : GameContentLayout.DlcFolderName);
        if (!Directory.Exists(source)) return;
        if (_emulatorId == "rpcs3")
        {
            foreach (var file in SafeFiles(source, problems).Where(file =>
                         file.Extension.Equals(".pkg", StringComparison.OrdinalIgnoreCase) ||
                         file.Extension.Equals(".rap", StringComparison.OrdinalIgnoreCase) ||
                         file.Extension.Equals(".edat", StringComparison.OrdinalIgnoreCase)))
                items.Add(new(game.Name, null, kind, file.FullName, $"{game.Name}: {file.Name}"));
            if (Directory.EnumerateFileSystemEntries(source).Any() && !items.Any(item => item.SourcePath.StartsWith(source, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"{game.Name}/{Path.GetFileName(source)} contains no RPCS3 .pkg, .rap, or .edat files.");
            return;
        }

        var titleId = FindTitleId(game.Name);
        var startingCount = items.Count;
        foreach (var directory in SafeDirectories(source, problems))
        {
            ValidateTree(directory.FullName, problems);
            var itemTitleId = FindTitleId(directory.Name) ?? titleId;
            if (itemTitleId is null)
            {
                problems.Add($"{game.Name}/{Path.GetFileName(source)}/{directory.Name} has no recognizable CUSA title ID.");
                continue;
            }
            if (kind == GameContentKind.Update &&
                !directory.Name.Equals(itemTitleId + "-patch", StringComparison.OrdinalIgnoreCase) &&
                !directory.Name.Equals(itemTitleId + "-UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{directory.Name} must be named {itemTitleId}-patch or {itemTitleId}-UPDATE for shadPS4.");
                continue;
            }
            items.Add(new(game.Name, itemTitleId.ToUpperInvariant(), kind, directory.FullName,
                $"{game.Name}: {directory.Name}"));
        }
        if (Directory.EnumerateFileSystemEntries(source).Any() && items.Count == startingCount)
            problems.Add($"{game.Name}/{Path.GetFileName(source)} contains no valid shadPS4 content folders.");
    }

    private static string? FindTitleId(string value) => Ps4TitleIdRegex().Match(value) is { Success: true } match
        ? match.Value : null;

    private static IEnumerable<DirectoryInfo> SafeDirectories(string root, List<string> problems)
    {
        foreach (var directory in new DirectoryInfo(root).EnumerateDirectories())
        {
            if (directory.LinkTarget is not null || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                problems.Add($"Linked folder is not supported: {directory.Name}");
                continue;
            }
            yield return directory;
        }
    }

    private static IEnumerable<FileInfo> SafeFiles(string root, List<string> problems)
    {
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                problems.Add($"Linked file is not supported: {file.Name}");
                continue;
            }
            yield return file;
        }
    }

    private static void ValidateTree(string root, List<string> problems)
    {
        foreach (var directory in new DirectoryInfo(root).EnumerateDirectories("*", SearchOption.AllDirectories))
            if (directory.LinkTarget is not null || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                problems.Add($"Linked folder is not supported: {directory.Name}");
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
            if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                problems.Add($"Linked file is not supported: {file.Name}");
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in SafeDirectories(source, []))
            await CopyDirectoryAsync(directory.FullName, Path.Combine(destination, directory.Name), token);
        foreach (var file in new DirectoryInfo(source).EnumerateFiles())
        {
            token.ThrowIfCancellationRequested();
            if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked content files are not supported.");
            var target = Path.Combine(destination, file.Name);
            if (File.Exists(target) && FilesEqual(file.FullName, target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        var first = new FileInfo(left);
        var second = new FileInfo(right);
        return first.Length == second.Length && first.LastWriteTimeUtc == second.LastWriteTimeUtc;
    }

    private string ReceiptPath(PlatformKind platform) => Path.Combine(_stateRoot, "Config", "EmulatorCompanion",
        "ContentImports", _emulatorId, (_emulatorId is "rpcs3" or "shadps4" ? "Shared" : platform.ToString()) + ".json");

    private string LegacyReceiptPath(PlatformKind platform) => Path.Combine(_stateRoot, "Config", "EmulatorCompanion",
        "ContentImports", _emulatorId, platform + ".json");

    private string ReceiptKey(GameContentImportItem item) =>
        Path.GetRelativePath(_mediaRoot, item.SourcePath).Replace('\\', '/');

    private async Task<Dictionary<string, string>> LoadReceiptsAsync(PlatformKind platform, CancellationToken token)
    {
        var receipts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var paths = _emulatorId is "rpcs3" or "shadps4"
            ? new[] { LegacyReceiptPath(PlatformKind.Windows), LegacyReceiptPath(PlatformKind.Linux), ReceiptPath(platform) }
            : new[] { ReceiptPath(platform) };
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The content import record is too large.");
            if (JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(path, token)) is not { } values)
                continue;
            foreach (var (key, value) in values) receipts[key] = value;
        }
        return receipts;
    }

    private async Task SaveReceiptsAsync(PlatformKind platform, IReadOnlyList<GameContentImportItem> items,
        CancellationToken token)
    {
        var receipts = await LoadReceiptsAsync(platform, token);
        foreach (var item in items) receipts[ReceiptKey(item)] = Signature(item.SourcePath);
        var path = ReceiptPath(platform);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await NativeControllerConfigurationFiles.AtomicWriteAsync(path,
            JsonSerializer.Serialize(receipts, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, token);
    }

    private static string Signature(string path)
    {
        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            return $"f:{file.Length}:{file.LastWriteTimeUtc.Ticks}";
        }
        var files = new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories)
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            var entry = $"{Path.GetRelativePath(path, file.FullName).Replace('\\', '/')}:{file.Length}:{file.LastWriteTimeUtc.Ticks}\n";
            hash.AppendData(Encoding.UTF8.GetBytes(entry));
        }
        return "d:" + Convert.ToHexString(hash.GetHashAndReset());
    }
}
