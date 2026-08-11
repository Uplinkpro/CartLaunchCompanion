using System.Text.Json.Serialization;

namespace CartLaunchCompanion.Core.Updating;

public sealed class RuntimeUpdateManifest
{
    public int FormatVersion { get; set; } = 1;
    public int SecurityVersion { get; set; } = 1;
    public string Product { get; set; } = "Cart Launch Companion";
    public string Version { get; set; } = "";
    public string Platform { get; set; } = "";
    public string EntryPoint { get; set; } = "";
    public string RootFingerprint { get; set; } = "";
    public string SignerKeyId { get; set; } = "";
    public string Signature { get; set; } = "";
    public List<RuntimeUpdateFile> Files { get; set; } = [];

    [JsonIgnore]
    public bool IsSigned =>
        !string.IsNullOrWhiteSpace(SignerKeyId) &&
        !string.IsNullOrWhiteSpace(Signature);
}

public sealed class RuntimeUpdateFile
{
    public string Path { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}
