namespace CartLaunchCompanion.Core.Updating;

public static class OfficialUpdateTrust
{
    public const string Repository = "Uplinkpro/CartLaunchCompanion";
    public const string KeyId = "uplinkpro-release-2";
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIGbMBAGByqGSM49AgEGBSuBBAAjA4GGAAQAob9spemtIpwj6clGA1MJ47b0YSRI
        LKvjffK6ggFpOxos9iP5VzQNSpykv41f5nm+B3fXkPaq0WOViBQMnrAidPoBE+3X
        2hDLiqQdtVn0KbqNQL/xqbf4ThkW+r/9g6u6mI80Jz4clFMfvVPdTMN3wUweefzm
        yiBAEdj/zODhs+aG/BQ=
        -----END PUBLIC KEY-----
        """;

    private static readonly IReadOnlyDictionary<string, string> TrustedKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KeyId] = PublicKeyPem
        };

    public static IEnumerable<string> TrustedKeyIds => TrustedKeys.Keys;

    public static bool TryGetPublicKey(string keyId, out string publicKeyPem) =>
        TrustedKeys.TryGetValue(keyId, out publicKeyPem!);
}
