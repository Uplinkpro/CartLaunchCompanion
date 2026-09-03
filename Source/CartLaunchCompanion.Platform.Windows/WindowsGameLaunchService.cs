using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Launching;
using CartLaunchCompanion.Core.Platform;
using CartLaunchCompanion.Core.Portable;

namespace CartLaunchCompanion.Platform.Windows;

public sealed class WindowsGameLaunchService : IGameLaunchService
{
    public async Task<GameLaunchResult> LaunchAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Target.Enabled)
        {
            return GameLaunchResult.Failure(
                $"{request.GameName} is disabled on Windows.");
        }

        try
        {
            var readiness = await EnsureRequiredLauncherAsync(request, cancellationToken);
            if (!readiness.Succeeded)
                return GameLaunchResult.Failure(readiness.Message);

            var companion = StartCompanion(request);
            var result = Start(request);
            if (companion is not null && !result.Succeeded)
            {
                if (!companion.HasExited) companion.Kill(entireProcessTree: true);
                companion.Dispose();
            }
            if (companion is not null && result.Succeeded && result.Session is not null)
                result = GameLaunchResult.Success(
                    result.Message + " The companion app was started first.",
                    new CompanionGameLaunchSession(result.Session, companion, request.Target.CompanionApplication.CloseAfterGame));
            if (result.Succeeded && readiness.Started)
                result = GameLaunchResult.Success(
                    readiness.Message + " " + result.Message,
                    result.Session ?? CompletedGameLaunchSession.Instance);
            return result;
        }
        catch (Exception ex)
        {
            return GameLaunchResult.Failure(
                $"Windows could not launch {request.GameName}: {ex.Message}");
        }
    }

    private static async Task<LauncherReadinessResult> EnsureRequiredLauncherAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken)
    {
        var launcher = request.Target.RequiredLauncher;
        if (launcher is null && request.Target.Launcher is
            (LauncherKind.GOG or LauncherKind.Rockstar or LauncherKind.Amazon or
             LauncherKind.EA or LauncherKind.BattleNet or LauncherKind.HoYoverse or
             LauncherKind.ItchIo))
        {
            launcher = request.Target.Launcher;
        }

        if (launcher is null || launcher is
            (LauncherKind.Local or LauncherKind.Custom or LauncherKind.Flash or
             LauncherKind.Wine or LauncherKind.Proton or LauncherKind.Flatpak))
        {
            return LauncherReadinessResult.Ready();
        }

        if (IsLauncherRunning(launcher.Value))
            return LauncherReadinessResult.Ready();

        var detection = new HostLauncherDetectionService().Detect(
            launcher.Value,
            PlatformKind.Windows);
        if (!detection.Found)
        {
            return LauncherReadinessResult.Failure(
                $"{DisplayName(launcher.Value)} is required for {request.GameName}, but it was not found on this computer.");
        }

        var executable = FindLauncherExecutable(launcher.Value, detection.Location);
        if (executable is null)
        {
            return LauncherReadinessResult.Failure(
                $"{DisplayName(launcher.Value)} is required, but CLC could not find its launcher executable. Open it once, then try again.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? ""
        };
        if (launcher == LauncherKind.Steam)
            startInfo.ArgumentList.Add("-silent");
        Process.Start(startInfo);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLauncherRunning(launcher.Value))
            {
                // Give the client a moment to finish loading its registered libraries.
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                return LauncherReadinessResult.StartedSuccessfully(
                    $"{DisplayName(launcher.Value)} was started first.");
            }
            await Task.Delay(250, cancellationToken);
        }

        return LauncherReadinessResult.Failure(
            $"{DisplayName(launcher.Value)} did not become ready. Open it manually, then try again.");
    }

    private static bool IsLauncherRunning(LauncherKind launcher)
    {
        foreach (var processName in LauncherProcessNames(launcher))
        {
            Process[] processes = [];
            try
            {
                processes = Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                    return true;
            }
            catch (InvalidOperationException) { }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
        return false;
    }

    private static string? FindLauncherExecutable(LauncherKind launcher, string detectedLocation)
    {
        if (File.Exists(detectedLocation))
            return detectedLocation;
        if (!Directory.Exists(detectedLocation))
            return null;

        foreach (var relativePath in LauncherExecutableCandidates(launcher))
        {
            var candidate = Path.Combine(detectedLocation, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string[] LauncherExecutableCandidates(LauncherKind launcher) => launcher switch
    {
        LauncherKind.Steam => ["steam.exe"],
        LauncherKind.Epic => [@"Portal\Binaries\Win64\EpicGamesLauncher.exe", "EpicGamesLauncher.exe"],
        LauncherKind.GOG => ["GalaxyClient.exe", @"GalaxyClient\GalaxyClient.exe"],
        LauncherKind.Ubisoft => ["UbisoftConnect.exe", "upc.exe"],
        LauncherKind.Rockstar => ["Launcher.exe", @"Launcher\Launcher.exe", @"Rockstar Games Launcher\Launcher.exe"],
        LauncherKind.Amazon => ["Amazon Games.exe", @"App\Amazon Games.exe"],
        LauncherKind.EA => ["EADesktop.exe", @"EADesktop\EADesktop.exe"],
        LauncherKind.BattleNet => ["Battle.net Launcher.exe"],
        LauncherKind.HoYoverse => ["HYP.exe", "launcher.exe"],
        LauncherKind.ItchIo => ["itch.exe"],
        _ => []
    };

    private static string[] LauncherProcessNames(LauncherKind launcher) => launcher switch
    {
        LauncherKind.Steam => ["steam"],
        LauncherKind.Epic => ["EpicGamesLauncher"],
        LauncherKind.GOG => ["GalaxyClient"],
        LauncherKind.Ubisoft => ["UbisoftConnect", "upc"],
        LauncherKind.Rockstar => ["Launcher", "LauncherPatcher"],
        LauncherKind.Amazon => ["Amazon Games"],
        LauncherKind.EA => ["EADesktop"],
        LauncherKind.BattleNet => ["Battle.net"],
        LauncherKind.HoYoverse => ["HYP", "launcher"],
        LauncherKind.ItchIo => ["itch"],
        _ => []
    };

    private static string DisplayName(LauncherKind launcher) => launcher switch
    {
        LauncherKind.GOG => "GOG Galaxy",
        LauncherKind.EA => "EA app",
        LauncherKind.BattleNet => "Battle.net",
        LauncherKind.HoYoverse => "HoYoPlay",
        LauncherKind.ItchIo => "itch.io",
        _ => launcher.ToString()
    };

    private sealed record LauncherReadinessResult(bool Succeeded, bool Started, string Message)
    {
        public static LauncherReadinessResult Ready() => new(true, false, "");
        public static LauncherReadinessResult StartedSuccessfully(string message) => new(true, true, message);
        public static LauncherReadinessResult Failure(string message) => new(false, false, message);
    }

    private static Process? StartCompanion(GameLaunchRequest request)
    {
        var companion = request.Target.CompanionApplication;
        if (!companion.Enabled) return null;
        if (string.IsNullOrWhiteSpace(companion.Executable))
            throw new InvalidOperationException("The Windows companion app is enabled but has no executable.");
        if (!File.Exists(companion.Executable))
            throw new FileNotFoundException("The companion app was not found.", companion.Executable);

        var startInfo = new ProcessStartInfo
        {
            FileName = companion.Executable,
            UseShellExecute = false,
            WorkingDirectory = !string.IsNullOrWhiteSpace(companion.WorkingDirectory) && Directory.Exists(companion.WorkingDirectory)
                ? companion.WorkingDirectory : request.GameFolder
        };
        AddArguments(startInfo, companion.Arguments);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Windows did not return a process for the companion app.");
    }

    private static GameLaunchResult Start(GameLaunchRequest request)
    {
        var target = request.Target;

        if (!string.IsNullOrWhiteSpace(target.Uri))
        {
            StartShellTarget(target.Uri);

            return CreateShellResult(
                request,
                "The configured URI was opened.");
        }

        return target.Launcher switch
        {
            LauncherKind.Steam =>
                StartSteam(request),

            LauncherKind.Xbox =>
                StartXbox(request),

            LauncherKind.Epic =>
                StartEpic(request),

            LauncherKind.Ubisoft =>
                StartUbisoft(request),

            LauncherKind.Local or
            LauncherKind.Custom or
            LauncherKind.GOG or
            LauncherKind.Rockstar or
            LauncherKind.Amazon or
            LauncherKind.EA or
            LauncherKind.BattleNet or
            LauncherKind.HoYoverse or
            LauncherKind.ItchIo or
            LauncherKind.Flash =>
                StartExecutable(request),

            _ => GameLaunchResult.Failure(
                $"The Windows {target.Launcher} launch method is not configured.")
        };
    }

    private static GameLaunchResult StartSteam(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.SteamId))
        {
            return GameLaunchResult.Failure(
                "The Windows Steam target has no Steam App ID.");
        }

        StartShellTarget(
            $"steam://rungameid/{request.Target.SteamId}");

        return CreateShellResult(
            request,
            "Steam accepted the launch request.");
    }

    private static GameLaunchResult StartXbox(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.ApplicationId))
        {
            return GameLaunchResult.Failure(
                "The Xbox target has no application ID.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        };

        startInfo.ArgumentList.Add(
            $"shell:AppsFolder\\{request.Target.ApplicationId}");

        Process.Start(startInfo);

        return CreateShellResult(
            request,
            "Windows accepted the Xbox application request.");
    }

    private static GameLaunchResult StartEpic(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.ApplicationId))
        {
            return GameLaunchResult.Failure(
                "The Epic target has no application name.");
        }

        var uri =
            "com.epicgames.launcher://apps/" +
            Uri.EscapeDataString(request.Target.ApplicationId) +
            "?action=launch&silent=true";

        StartShellTarget(uri);

        return CreateShellResult(
            request,
            "Epic Games Launcher accepted the launch request.");
    }

    private static GameLaunchResult StartUbisoft(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.ApplicationId))
        {
            return GameLaunchResult.Failure(
                "The Ubisoft target has no game ID.");
        }

        StartShellTarget(
            $"uplay://launch/{request.Target.ApplicationId}/0");

        return CreateShellResult(
            request,
            "Ubisoft Connect accepted the launch request.");
    }

    private static GameLaunchResult StartExecutable(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.Executable))
        {
            return GameLaunchResult.Failure(
                $"The {request.Target.Launcher} target has no executable or URI.");
        }

        if (!File.Exists(request.Target.Executable))
        {
            return GameLaunchResult.Failure(
                $"The executable was not found: {request.Target.Executable}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = request.Target.Executable,
            UseShellExecute = false,
            WorkingDirectory = ResolveWorkingDirectory(request)
        };

        AddArguments(startInfo, request.Target.Arguments);

        var process = Process.Start(startInfo);

        if (process is null)
        {
            return GameLaunchResult.Failure(
                "Windows did not return a process for the launched executable.");
        }

        return GameLaunchResult.Success(
            $"{request.GameName} started.",
            new WindowsProcessLaunchSession(process));
    }

    private static GameLaunchResult CreateShellResult(
        GameLaunchRequest request,
        string message)
    {
        if (!string.IsNullOrWhiteSpace(request.Target.ProcessName))
        {
            return GameLaunchResult.Success(
                message,
                new WindowsNamedProcessLaunchSession(
                    request.Target.ProcessName,
                    request.Behavior.ProcessStartTimeoutSeconds,
                    request.Behavior.ProcessExitPollSeconds));
        }

        return GameLaunchResult.Success(
            message +
            " No process name is configured, so automatic hide/restore is disabled.",
            CompletedGameLaunchSession.Instance);
    }

    private static void StartShellTarget(string target)
    {
        Process.Start(
            new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
    }

    private static string ResolveWorkingDirectory(
        GameLaunchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(
                request.Target.WorkingDirectory) &&
            Directory.Exists(request.Target.WorkingDirectory))
        {
            return request.Target.WorkingDirectory;
        }

        return request.GameFolder;
    }

    private static void AddArguments(
        ProcessStartInfo startInfo,
        string arguments)
    {
        foreach (var argument in CommandLineArgumentParser.Parse(arguments))
            startInfo.ArgumentList.Add(argument);
    }
}
