using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Launching;
using System.Security.Cryptography;
using System.Text;

namespace CartLaunchCompanion.Platform.Linux;

public sealed class LinuxGameLaunchService : IGameLaunchService
{
    public async Task<GameLaunchResult> LaunchAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Target.Enabled)
        {
            return GameLaunchResult.Failure(
                $"{request.GameName} is disabled on Linux.");
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
                $"Linux could not launch {request.GameName}: {ex.Message}");
        }
    }

    private static async Task<LauncherReadinessResult> EnsureRequiredLauncherAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken)
    {
        var launcher = request.Target.RequiredLauncher;
        if (launcher is null || IsLauncherRunning(launcher.Value))
            return LauncherReadinessResult.Ready();

        try
        {
            switch (launcher)
            {
                case LauncherKind.Steam:
                    StartDetached("steam", ["-silent"], request.GameFolder);
                    break;
                case LauncherKind.Heroic:
                    try
                    {
                        StartDetached("heroic", [], request.GameFolder);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        StartDetached("flatpak", ["run", "com.heroicgameslauncher.hgl"], request.GameFolder);
                    }
                    break;
                default:
                    return LauncherReadinessResult.Failure(
                        $"{launcher} cannot be started automatically on Linux. Open it manually, then try again.");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return LauncherReadinessResult.Failure(
                $"{launcher} is required, but it was not found on this Linux computer.");
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLauncherRunning(launcher.Value))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                return LauncherReadinessResult.StartedSuccessfully($"{launcher} was started first.");
            }
            await Task.Delay(250, cancellationToken);
        }

        return LauncherReadinessResult.Failure(
            $"{launcher} did not become ready. Open it manually, then try again.");
    }

    private static bool IsLauncherRunning(LauncherKind launcher)
    {
        var names = launcher switch
        {
            LauncherKind.Steam => new[] { "steam" },
            LauncherKind.Heroic => new[] { "heroic", "Heroic" },
            _ => []
        };
        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length > 0)
                    return true;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
        return false;
    }

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
            throw new InvalidOperationException("The Linux companion app is enabled but has no executable.");
        if (!File.Exists(companion.Executable))
            throw new FileNotFoundException("The companion app was not found.", companion.Executable);
        return StartProcess(
            companion.Executable,
            CommandLineArgumentParser.Parse(companion.Arguments),
            !string.IsNullOrWhiteSpace(companion.WorkingDirectory) && Directory.Exists(companion.WorkingDirectory)
                ? companion.WorkingDirectory : request.GameFolder);
    }

    private static GameLaunchResult Start(GameLaunchRequest request)
    {
        var target = request.Target;

        if (!string.IsNullOrWhiteSpace(target.Uri))
        {
            StartDetached(
                "xdg-open",
                [target.Uri],
                request.GameFolder);

            return CreateDetachedResult(
                request,
                "The configured URI was opened.");
        }

        return target.Launcher switch
        {
            LauncherKind.Steam =>
                StartSteam(request),

            LauncherKind.Heroic =>
                StartHeroic(request),

            LauncherKind.Flatpak =>
                StartFlatpak(request),

            LauncherKind.Wine =>
                StartWine(request),

            LauncherKind.Proton =>
                StartProton(request),

            LauncherKind.Local or
            LauncherKind.Custom =>
                StartExecutable(request),

            _ => GameLaunchResult.Failure(
                $"The Linux {target.Launcher} launch method is not configured.")
        };
    }

    private static GameLaunchResult StartSteam(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.SteamId))
        {
            return GameLaunchResult.Failure(
                "The Linux Steam target has no Steam App ID.");
        }

        StartDetached(
            "steam",
            ["-applaunch", request.Target.SteamId],
            request.GameFolder);

        return CreateDetachedResult(
            request,
            "Steam accepted the launch request.");
    }

    private static GameLaunchResult StartHeroic(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.ApplicationId))
        {
            return GameLaunchResult.Failure(
                "The Heroic target has no game ID or explicit URI.");
        }

        var uri =
            $"heroic://launch/legendary/{request.Target.ApplicationId}";

        StartDetached(
            "xdg-open",
            [uri],
            request.GameFolder);

        return CreateDetachedResult(
            request,
            "Heroic accepted the launch request.");
    }

    private static GameLaunchResult StartFlatpak(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.ApplicationId))
        {
            return GameLaunchResult.Failure(
                "The Flatpak target has no application ID.");
        }

        var arguments = new List<string>
        {
            "run",
            request.Target.ApplicationId
        };

        arguments.AddRange(
            CommandLineArgumentParser.Parse(
                request.Target.Arguments));

        var process = StartProcess(
            "flatpak",
            arguments,
            ResolveWorkingDirectory(request));

        return GameLaunchResult.Success(
            $"{request.GameName} started through Flatpak.",
            new LinuxProcessLaunchSession(process));
    }

    private static GameLaunchResult StartWine(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.Executable))
        {
            return GameLaunchResult.Failure(
                "The Wine target has no executable.");
        }

        var executable = string.IsNullOrWhiteSpace(
            request.Target.CompatibilityTool)
            ? "wine"
            : request.Target.CompatibilityTool;

        var arguments = new List<string>
        {
            request.Target.Executable
        };

        arguments.AddRange(
            CommandLineArgumentParser.Parse(
                request.Target.Arguments));

        var startInfo = CreateStartInfo(
            executable,
            arguments,
            ResolveWorkingDirectory(request));

        if (!string.IsNullOrWhiteSpace(request.Target.WinePrefix))
        {
            startInfo.Environment["WINEPREFIX"] =
                request.Target.WinePrefix;
        }

        var process = StartProcess(startInfo);

        return GameLaunchResult.Success(
            $"{request.GameName} started through Wine.",
            new LinuxProcessLaunchSession(process));
    }

    private static GameLaunchResult StartProton(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.Executable) &&
            !string.IsNullOrWhiteSpace(request.Target.SteamId))
            return StartSteam(request);

        if (string.IsNullOrWhiteSpace(request.Target.Executable))
        {
            return GameLaunchResult.Failure(
                "The UMU-Proton target has no Windows executable.");
        }

        var arguments = new List<string>
        {
            request.Target.Executable
        };

        arguments.AddRange(
            CommandLineArgumentParser.Parse(
                request.Target.Arguments));

        var startInfo = CreateStartInfo(
            "umu-run",
            arguments,
            ResolveWorkingDirectory(request));

        var compatibilityTool = request.Target.CompatibilityTool.Trim();
        if (!string.IsNullOrWhiteSpace(compatibilityTool) &&
            !compatibilityTool.Equals("UMU-Proton", StringComparison.OrdinalIgnoreCase) &&
            !compatibilityTool.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Environment["PROTONPATH"] = compatibilityTool;
        }

        startInfo.Environment["GAMEID"] = CreateUmuGameId(request);
        startInfo.Environment["WINEPREFIX"] = string.IsNullOrWhiteSpace(request.Target.WinePrefix)
            ? CreateDefaultPrefixPath(request)
            : request.Target.WinePrefix;
        startInfo.Environment["STEAM_COMPAT_LIBRARY_PATHS"] = CreateCompatibilityLibraryPaths(request);

        var process = StartProcess(startInfo);

        return GameLaunchResult.Success(
            $"{request.GameName} started through UMU-Proton.",
            new LinuxProcessLaunchSession(process));
    }

    private static string CreateUmuGameId(GameLaunchRequest request) =>
        "clc-" + CreateGameKey(request);

    private static string CreateDefaultPrefixPath(GameLaunchRequest request)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(dataHome, "cart-launch-companion", "compatdata", CreateGameKey(request));
    }

    private static string CreateGameKey(GameLaunchRequest request)
    {
        var identity = $"{request.GameName}|{Path.GetFullPath(request.GameFolder)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12].ToLowerInvariant();
        var name = new string(request.GameName.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        if (name.Length > 40) name = name[..40].TrimEnd('-');
        return $"{(string.IsNullOrWhiteSpace(name) ? "game" : name)}-{hash}";
    }

    private static string CreateCompatibilityLibraryPaths(GameLaunchRequest request)
    {
        var paths = new[]
        {
            Path.GetFullPath(request.GameFolder),
            Path.GetDirectoryName(Path.GetFullPath(request.Target.Executable))
        };
        return string.Join(':', paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal));
    }

    private static GameLaunchResult StartExecutable(
        GameLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Target.Executable))
        {
            return GameLaunchResult.Failure(
                "The Linux target has no executable or URI.");
        }

        if (!File.Exists(request.Target.Executable))
        {
            return GameLaunchResult.Failure(
                $"The executable was not found: {request.Target.Executable}");
        }

        var process = StartProcess(
            request.Target.Executable,
            CommandLineArgumentParser.Parse(
                request.Target.Arguments),
            ResolveWorkingDirectory(request));

        return GameLaunchResult.Success(
            $"{request.GameName} started.",
            new LinuxProcessLaunchSession(process));
    }

    private static GameLaunchResult CreateDetachedResult(
        GameLaunchRequest request,
        string message)
    {
        if (!string.IsNullOrWhiteSpace(request.Target.ProcessName))
        {
            return GameLaunchResult.Success(
                message,
                new LinuxNamedProcessLaunchSession(
                    request.Target.ProcessName,
                    request.Behavior.ProcessStartTimeoutSeconds,
                    request.Behavior.ProcessExitPollSeconds));
        }

        return GameLaunchResult.Success(
            message +
            " No process name is configured, so automatic hide/restore is disabled.",
            CompletedGameLaunchSession.Instance);
    }

    private static void StartDetached(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        _ = StartProcess(
            executable,
            arguments,
            workingDirectory);
    }

    private static Process StartProcess(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
        => StartProcess(
            CreateStartInfo(
                executable,
                arguments,
                workingDirectory));

    private static Process StartProcess(
        ProcessStartInfo startInfo)
        => Process.Start(startInfo)
           ?? throw new InvalidOperationException(
               "Linux did not return a process.");

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
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
}
