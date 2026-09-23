using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CartLaunchCompanion.Core.Emulators;

namespace CartLaunchCompanion.EmulatorCompanion;

public sealed record LibraryRow
{
    public required EmulatorLibraryEntry Entry { get; init; }
    public required string Name { get; init; }
    public required string Platform { get; init; }
    public required string Version { get; init; }
    public required string Channel { get; init; }
    public required string Status { get; init; }
    public bool IsInstalled => Entry.Installations.Count > 0;
    public string Availability => IsInstalled ? "INSTALLED" : "AVAILABLE";
    public string NextStep => IsInstalled ? "Review setup and launch readiness" : "Choose a platform and install";

    public static LibraryRow From(EmulatorLibraryEntry entry, bool registryUnavailable)
    {
        var installations = entry.Installations;
        var channelIds = installations.Select(item => item.InstalledChannelId).Distinct(StringComparer.Ordinal).ToArray();
        var channelId = channelIds.Length == 1 ? channelIds[0] : null;
        var defaultChannel = entry.Definition?.ReleaseChannels.FirstOrDefault(item =>
            item.Id == entry.Definition.DefaultChannelId);
        var canInstall = entry.Definition?.Id is "ppsspp" or "duckstation" or "pcsx2" or "rpcs3" or "shadps4";
        return new()
        {
            Entry = entry,
            Name = entry.Definition?.DisplayName ?? entry.EmulatorId,
            Platform = InstalledPlatforms(installations) ?? SupportedPlatforms(entry.Definition),
            Version = InstalledVersions(installations) ?? (registryUnavailable ? "Unknown" : canInstall ? "Not installed" : "Not managed"),
            Channel = channelIds.Length > 1 ? "Mixed"
                : entry.Definition?.ReleaseChannels.FirstOrDefault(item => item.Id == channelId)?.DisplayName
                    ?? channelId ?? defaultChannel?.DisplayName ?? "Not specified",
            Status = registryUnavailable ? "Installation unknown"
                : installations.Count == 0 ? canInstall ? "Available to install" : "Release checks available"
                : entry.Definition is null ? "Recorded · not in catalog"
                : installations.Count > 1 ? "Installed on Windows + Linux"
                : $"Installed on {installations[0].Platform}"
        };
    }

    private static string? InstalledPlatforms(IReadOnlyList<EmulatorInstallation> installations) => installations.Count switch
    {
        0 => null,
        1 => installations[0].Platform.ToString(),
        _ => string.Join(" + ", installations.Select(item => item.Platform.ToString()))
    };

    private static string? InstalledVersions(IReadOnlyList<EmulatorInstallation> installations)
    {
        if (installations.Count == 0) return null;
        var versions = installations.Select(item => item.InstalledVersion ?? "Unknown").Distinct(StringComparer.Ordinal).ToArray();
        return versions.Length == 1 ? versions[0] : "Platform-specific builds";
    }

    private static string SupportedPlatforms(EmulatorDefinition? definition)
    {
        if (definition is null) return "Not specified";
        var platforms = definition.ReleaseChannels
            .SelectMany(channel => channel.SupportedPlatforms)
            .Distinct()
            .Select(platform => platform.ToString())
            .ToArray();
        return platforms.Length == 0 ? "Not specified" : string.Join(" / ", platforms);
    }
}

public sealed class LibraryViewModel(IEmulatorLibraryService service, string mediaRoot, Func<CancellationToken, Task>? recover = null,
    string? stateRoot = null) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private LibraryRow? _selectedRow;
    public LibraryRow? SelectedRow
    {
        get => _selectedRow;
        set { if (_selectedRow == value) return; _selectedRow = value; NotifyAll(); }
    }
    public bool CanViewReleases => !IsLoading && SelectedRow?.Entry.Definition?.Id is "ppsspp" or "duckstation" or "pcsx2" or "rpcs3" or "shadps4";
    public bool CanSetUp => !IsLoading && SelectedRow?.Entry.Definition?.Id is
        "ppsspp" or "duckstation" or "pcsx2" or "rpcs3" or "shadps4" && SelectedRow.Entry.Installations.Count > 0;
    public bool CanUninstall => !IsLoading && SelectedRow?.IsInstalled == true && SelectedRow.Entry.Definition?.Id is
        "ppsspp" or "duckstation" or "pcsx2" or "rpcs3" or "shadps4";
    public string SetupActionLabel => SelectedRow is null ? "Set up emulator" : $"Set up {SelectedRow.Name}";
    public string SelectedName => SelectedRow?.Name ?? "Choose an emulator";
    public string SelectedStatus => SelectedRow?.Status ?? "Select an emulator from the library.";
    public string SelectedPlatform => SelectedRow?.Platform ?? "—";
    public string SelectedVersion => SelectedRow?.Version ?? "—";
    public string SelectedChannel => SelectedRow?.Channel ?? "—";
    public IImage? SelectedIcon => SelectedRow is null ? null : EmulatorIcons.GetValueOrDefault(SelectedRow.Entry.EmulatorId);
    public string SelectedNextStep => SelectedRow?.NextStep ?? "Choose an emulator to continue.";
    public string SelectedActionLabel => SelectedRow?.Entry.Definition?.Id switch
    {
        "ppsspp" when SelectedRow.Entry.Installations.Count == 0 => "Install PPSSPP",
        "ppsspp" => "Manage PPSSPP",
        "duckstation" when SelectedRow.Entry.Installations.Count == 0 => "Install DuckStation",
        "duckstation" => "Manage DuckStation",
        "pcsx2" when SelectedRow.Entry.Installations.Count == 0 => "Install PCSX2",
        "pcsx2" => "Manage PCSX2",
        "rpcs3" when SelectedRow.Entry.Installations.Count == 0 => "Install RPCS3",
        "rpcs3" => "Manage RPCS3",
        "shadps4" when SelectedRow.Entry.Installations.Count == 0 => "Install shadPS4",
        "shadps4" => "Manage shadPS4",
        _ => "Install or update"
    };
    public bool CanViewAbout => !IsLoading;
    public string ControllerActionLabel { get; private set; } = "Set up controller";
    public string MediaRoot { get; } = mediaRoot;
    public string StateRoot { get; } = stateRoot ?? mediaRoot;
    public IReadOnlyList<LibraryRow> Rows { get; private set; } = [];
    public bool IsLoading { get; private set; }
    public bool CanRefresh => !IsLoading;
    public bool HasRows => Rows.Count > 0;
    public string ErrorMessage { get; private set; } = "";
    public bool HasError => ErrorMessage.Length > 0;
    public bool ShowEmpty => !IsLoading && !HasError && !HasRows;
    public bool ShowLoading => IsLoading && !HasRows;
    public string Summary => IsLoading ? "Loading library…" : $"{Rows.Count} library entries";
    public string EmptyMessage => "No emulators are listed yet. Catalog entries and recorded installations will appear here.";

    private static readonly IReadOnlyDictionary<string, IImage> EmulatorIcons = new Dictionary<string, IImage>(StringComparer.Ordinal)
    {
        ["ppsspp"] = LoadIcon("ppsspp"),
        ["duckstation"] = LoadIcon("duckstation"),
        ["pcsx2"] = LoadIcon("pcsx2"),
        ["rpcs3"] = LoadIcon("rpcs3"),
        ["shadps4"] = LoadIcon("shadps4")
    };

    private static IImage LoadIcon(string id) => new Bitmap(AssetLoader.Open(
        new Uri($"avares://CartLaunchCompanion.EmulatorCompanion/Assets/Emulators/{id}.png")));

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
            return;
        IsLoading = true;
        ErrorMessage = "";
        NotifyAll();
        try
        {
            if (recover is not null) await recover(cancellationToken);
            var snapshot = await service.LoadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var previousSelection = SelectedRow?.Entry.EmulatorId;
            Rows = snapshot.Entries.Select(entry => LibraryRow.From(entry, snapshot.RegistryUnavailable)).ToArray();
            await RefreshControllerAsync(cancellationToken);
            SelectedRow = Rows.FirstOrDefault(row => row.Entry.EmulatorId == previousSelection) ?? Rows.FirstOrDefault();
            ErrorMessage = (snapshot.CatalogUnavailable, snapshot.RegistryUnavailable) switch
            {
                (true, true) => "The catalog and installation records could not be loaded. Check that this library is available, then refresh.",
                (true, false) => "The catalog could not be loaded. Recorded installations are still shown. Restore the catalog, then refresh.",
                (false, true) => "Installation records could not be loaded. Installation status is unknown. Check this library, then refresh.",
                _ => ""
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The window owns cancellation and is closing; preserve the last completed snapshot.
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Rows = [];
            SelectedRow = null;
            ErrorMessage = "An interrupted installation needs recovery: " + error.Message + " Close the emulator, then refresh.";
        }
        finally
        {
            IsLoading = false;
            NotifyAll();
        }
    }

    public async Task RefreshControllerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var platform = OperatingSystem.IsLinux()
                ? CartLaunchCompanion.Core.Platform.PlatformKind.Linux
                : CartLaunchCompanion.Core.Platform.PlatformKind.Windows;
            var store = new VerifiedControllerProfileStore(StateRoot);
            var player1 = await store.LoadAsync(VerifiedControllerProfileStore.SharedProfileIdForPlayer(1), platform, cancellationToken);
            var player2 = await store.LoadAsync(VerifiedControllerProfileStore.SharedProfileIdForPlayer(2), platform, cancellationToken);
            ControllerActionLabel = player1 is null ? "Set up controllers" : player2 is null
                ? $"Player 1: {player1.Controller.Family.DisplayName()} ready"
                : "2 controllers ready";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ControllerActionLabel = "Repair controller setup";
        }
        NotifyAll();
    }

    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
