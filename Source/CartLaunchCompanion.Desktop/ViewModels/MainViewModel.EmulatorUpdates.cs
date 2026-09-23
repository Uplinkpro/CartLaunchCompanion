using CartLaunchCompanion.Core.Emulators;
using CartLaunchCompanion.Core.Input;
using CartLaunchCompanion.Core.Launching;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CartLaunchCompanion.Desktop.ViewModels;

public partial class MainViewModel
{
    private readonly IEmulatorLaunchUpdates? _emulatorLaunchUpdates;
    private readonly CancellationTokenSource _emulatorLaunchLifetime = new();
    private CancellationTokenSource? _emulatorInstallCancellation;
    private TaskCompletionSource<EmulatorUpdateChoice>? _emulatorChoice;
    private enum EmulatorUpdateChoice { Update, SkipNow, SkipVersion }

    [ObservableProperty] public partial bool IsEmulatorUpdateVisible { get; set; }
    [ObservableProperty] public partial bool IsEmulatorUpdateBusy { get; set; }
    [ObservableProperty] public partial string EmulatorUpdateMessage { get; set; } = "";
    [ObservableProperty] public partial string EmulatorUpdateVersion { get; set; } = "";
    public string EmulatorUpdateControls => $"{ConfirmPrompt}: Update   ·   {BackPrompt}: Skip for now   ·   {TrailerPrompt}: Skip this version";

    public IRelayCommand UpdateEmulatorNowCommand => field ??= new RelayCommand(() => ChooseEmulatorUpdate(EmulatorUpdateChoice.Update));
    public IRelayCommand SkipEmulatorNowCommand => field ??= new RelayCommand(() => ChooseEmulatorUpdate(EmulatorUpdateChoice.SkipNow));
    public IRelayCommand SkipEmulatorVersionCommand => field ??= new RelayCommand(() => ChooseEmulatorUpdate(EmulatorUpdateChoice.SkipVersion));
    public IRelayCommand CancelEmulatorUpdateCommand => field ??= new RelayCommand(() => _emulatorInstallCancellation?.Cancel());

    private void ChooseEmulatorUpdate(EmulatorUpdateChoice choice)
    {
        if (!IsEmulatorUpdateBusy) _emulatorChoice?.TrySetResult(choice);
    }

    private void HandleEmulatorUpdateInput(LauncherAction action)
    {
        if (IsEmulatorUpdateBusy)
        {
            if (action == LauncherAction.Back) _emulatorInstallCancellation?.Cancel();
            return;
        }
        if (action == LauncherAction.Confirm) ChooseEmulatorUpdate(EmulatorUpdateChoice.Update);
        else if (action == LauncherAction.Back) ChooseEmulatorUpdate(EmulatorUpdateChoice.SkipNow);
        else if (action == LauncherAction.Trailer) ChooseEmulatorUpdate(EmulatorUpdateChoice.SkipVersion);
    }

    private async Task<bool> PrepareEmulatorLaunchAsync(GameLaunchRequest request)
    {
        if (_emulatorLaunchUpdates is null) return true;
        var token = _emulatorLaunchLifetime.Token;
        var check = await _emulatorLaunchUpdates.CheckAsync(request, token);
        if (check.BlockingReason is not null)
        {
            MetadataStatus = check.BlockingReason;
            return false;
        }
        if (check.Release is not { } release) return true;
        IsLaunchTransitionVisible = false;
        IsEmulatorUpdateVisible = true;
        EmulatorUpdateVersion = $"PPSSPP {release.Version}";
        EmulatorUpdateMessage = "An update is available before this game starts. Your settings and saves will be preserved.";
        OnPropertyChanged(nameof(EmulatorUpdateControls));
        try
        {
            while (true)
            {
                _emulatorChoice = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var choice = await _emulatorChoice.Task.WaitAsync(token);
                _emulatorChoice = null;
                if (choice == EmulatorUpdateChoice.SkipNow) return true;
                IsEmulatorUpdateBusy = true;
                try
                {
                    if (choice == EmulatorUpdateChoice.SkipVersion)
                    {
                        await _emulatorLaunchUpdates.SkipVersionAsync(release, token);
                        return true;
                    }
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                    _emulatorInstallCancellation = cancellation;
                    EmulatorUpdateMessage = "Downloading and verifying the update. The game will start when installation finishes.";
                    await _emulatorLaunchUpdates.InstallAsync(release, cancellation.Token);
                    return true;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    EmulatorUpdateMessage = "The update was cancelled or timed out. Retry, or skip to launch the installed version.";
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or System.Net.Http.HttpRequestException or NotSupportedException)
                {
                    EmulatorUpdateMessage = "The update could not complete: " + error.Message +
                        " Retry, or skip to use the installed version if recovery is complete.";
                }
                finally
                {
                    _emulatorInstallCancellation = null;
                    IsEmulatorUpdateBusy = false;
                }
            }
        }
        finally
        {
            _emulatorChoice = null;
            IsEmulatorUpdateVisible = false;
            IsEmulatorUpdateBusy = false;
        }
    }
}