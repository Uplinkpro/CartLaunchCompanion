using System.Diagnostics;
using CartLaunchCompanion.Core.Updating;

namespace CartLaunchCompanion.Updater;

internal static class Program
{
    private const int HealthyStartupSeconds = 10;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = UpdateCommandLine.Parse(args);
            if (string.IsNullOrWhiteSpace(UpdateTrustAnchor.OfficialPublicKeyPem))
            {
                Console.Error.WriteLine(
                    "Updates are not enabled because the official verification key has not been configured.");
                return 20;
            }

            if (options.WaitForProcessId is int processId)
            {
                await WaitForExitAsync(processId, options.WaitTimeout);
            }

            using var signatures = new EcdsaUpdateSignatureVerifier(
                UpdateTrustAnchor.OfficialPublicKeyPem);
            var updater = new TransactionalRuntimeUpdater(
                new RuntimeIntegrityVerifier(),
                signatures);

            var result = await updater.ApplyAsync(
                new RuntimeUpdateRequest(
                    options.CartRoot,
                    options.Platform,
                    options.StagedRuntimeRoot,
                    options.ManifestPath));

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = result.EntryPoint,
                WorkingDirectory = result.ActiveRuntimeRoot,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("The updated launcher did not start.");

            var healthDelay = Task.Delay(TimeSpan.FromSeconds(HealthyStartupSeconds));
            var exited = process.WaitForExitAsync();
            if (await Task.WhenAny(healthDelay, exited) == exited)
            {
                TransactionalRuntimeUpdater.RollBackActivatedUpdate(
                    options.CartRoot,
                    options.Platform);
                RestartPreviousRuntime(options.CartRoot, options.Platform);
                Console.Error.WriteLine("The update failed its startup health check and was rolled back.");
                return 30;
            }

            TransactionalRuntimeUpdater.CompleteSuccessfulUpdate(
                options.CartRoot,
                options.Platform);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Update failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task WaitForExitAsync(int processId, TimeSpan timeout)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        using (var timeoutCancellation = new CancellationTokenSource(timeout))
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
    }

    private static void RestartPreviousRuntime(string cartRoot, string platform)
    {
        var runtime = Path.Combine(Path.GetFullPath(cartRoot), "System", platform);
        var entryPoint = platform == "Windows-x64"
            ? "CartLaunchCompanion.Desktop.exe"
            : "CartLaunchCompanion.Desktop";
        var path = RuntimePathPolicy.ResolveContainedFile(runtime, entryPoint);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = runtime,
            UseShellExecute = false
        });
    }
}

internal sealed record UpdateCommandLine(
    string CartRoot,
    string Platform,
    string StagedRuntimeRoot,
    string ManifestPath,
    int? WaitForProcessId,
    TimeSpan WaitTimeout)
{
    public static UpdateCommandLine Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Updater arguments must use --name value pairs.");
            }

            if (!values.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException($"Duplicate updater argument: {args[index]}.");
            }
        }

        var cartRoot = Required(values, "--cart-root");
        var platform = Required(values, "--platform");
        var stagedRuntime = Required(values, "--staged-runtime");
        var manifest = Required(values, "--manifest");
        int? waitPid = values.TryGetValue("--wait-pid", out var pidText)
            ? int.Parse(pidText, System.Globalization.CultureInfo.InvariantCulture)
            : null;
        var waitSeconds = values.TryGetValue("--wait-timeout-seconds", out var timeoutText)
            ? int.Parse(timeoutText, System.Globalization.CultureInfo.InvariantCulture)
            : 60;

        if (waitPid <= 0 || waitSeconds is < 1 or > 300)
        {
            throw new ArgumentException("Updater wait options are invalid.");
        }

        return new UpdateCommandLine(
            cartRoot,
            platform,
            stagedRuntime,
            manifest,
            waitPid,
            TimeSpan.FromSeconds(waitSeconds));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required updater argument: {name}.");
}
