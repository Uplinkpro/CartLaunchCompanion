namespace CartLaunchCompanion.Updater;

internal static class UpdateTrustAnchor
{
    // Production updating remains fail-closed until the offline release-signing
    // key ceremony is complete and its public key is embedded here.
    public const string OfficialKeyId = "uplinkpro-release-1";
    public const string OfficialPublicKeyPem = "";
}
