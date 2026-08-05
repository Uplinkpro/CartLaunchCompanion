using CartLaunchCompanion.Core.Configuration;
using CartLaunchCompanion.Core.Launching;

namespace CartLaunchCompanion.Platform.Windows;

public sealed class WindowsGameLaunchService : IGameLaunchService
{
    public Task<GameLaunchResult> LaunchAsync(
        GameLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Target.Enabled)
        {
            return Task.FromResult(
                GameLaunchResult.Failure(
                    $"{request.GameName} is disabled on Windows."));
        }

        try
        {
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
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                GameLaunchResult.Failure(
                    $"Windows could not launch {request.GameName}: {ex.Message}"));
        }
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
            LauncherKind.Amazon =>
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
