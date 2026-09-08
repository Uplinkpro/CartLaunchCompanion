namespace CartLaunchCompanion.Core.Updating;

public sealed class UpdateReminderStore
{
    private const string FileName = "skipped-update-version.txt";
    private readonly string _path;

    public UpdateReminderStore(string configurationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _path = Path.Combine(Path.GetFullPath(configurationDirectory), FileName);
    }

    public bool IsSkipped(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || !File.Exists(_path))
            return false;

        try
        {
            return string.Equals(File.ReadAllText(_path).Trim(), version.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task SkipAsync(string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, version.Trim(), cancellationToken);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
