namespace CartLaunchCompanion.Core.Emulators;

/// <summary>Shared catalog identity, independent of installation state and UI.</summary>
public sealed record EmulatorDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public EmulatorBranding? Branding { get; init; }
    public EmulatorAttribution? Attribution { get; init; }
    public string? DefaultChannelId { get; init; }
    public IReadOnlyList<string> SystemIds { get; init; } = [];
    public IReadOnlyList<EmulatorReleaseChannel> ReleaseChannels { get; init; } = [];
}
