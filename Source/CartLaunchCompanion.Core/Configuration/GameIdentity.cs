using System.Security.Cryptography;
using System.Text;

namespace CartLaunchCompanion.Core.Configuration;

public static class GameIdentity
{
    public static string Create() => "game-" + Guid.NewGuid().ToString("N");

    public static string Resolve(GameInformation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (!string.IsNullOrWhiteSpace(game.Id))
            return game.Id.Trim();

        var identitySource = !string.IsNullOrWhiteSpace(game.VersionGroup)
            ? "group:" + Normalize(game.VersionGroup)
            : $"game:{Normalize(game.Name)}|platform:{Normalize(game.PlatformLabel)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identitySource));
        return "derived-" + Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
