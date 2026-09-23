using CartLaunchCompanion.Core.Emulators;

namespace CartLaunchCompanion.Core.Tests;

public sealed class SimpleEmulationPresetTests
{
    [Theory]
    [InlineData(0x2dc8, 0x310b, "8BitDo Ultimate 2", ControllerProtocolKind.Xbox360, ControllerFamilyKind.EightBitDo)]
    [InlineData(0x054c, 0x0268, "PLAYSTATION(R)3 Controller", ControllerProtocolKind.PlayStation3, ControllerFamilyKind.PlayStation3)]
    [InlineData(0x054c, 0x09cc, "Wireless Controller", ControllerProtocolKind.PlayStation4, ControllerFamilyKind.PlayStation4)]
    [InlineData(0x054c, 0x0ce6, "DualSense Wireless Controller", ControllerProtocolKind.PlayStation5, ControllerFamilyKind.PlayStation5)]
    [InlineData(0x054c, 0x0df2, "DualSense Edge Wireless Controller", ControllerProtocolKind.PlayStation5, ControllerFamilyKind.DualSenseEdge)]
    [InlineData(0x28de, 0x1142, "Steam Controller", ControllerProtocolKind.Steam, ControllerFamilyKind.SteamController)]
    [InlineData(0x28de, 0x1304, "Steam Controller", ControllerProtocolKind.Steam, ControllerFamilyKind.SteamController2)]
    [InlineData(0x045e, 0x028e, "Xbox 360 Controller", ControllerProtocolKind.Xbox360, ControllerFamilyKind.Xbox360)]
    [InlineData(0x045e, 0x02e3, "Xbox Elite Controller", ControllerProtocolKind.XboxOne, ControllerFamilyKind.XboxElite)]
    [InlineData(0x045e, 0x0b12, "Xbox Controller", ControllerProtocolKind.XboxOne, ControllerFamilyKind.XboxSeries)]
    [InlineData(0x045e, 0x0b0a, "Xbox Adaptive Controller", ControllerProtocolKind.XboxOne, ControllerFamilyKind.XboxAdaptive)]
    [InlineData(0x1234, 0x5678, "USB Gamepad", ControllerProtocolKind.Standard, ControllerFamilyKind.GenericXboxStyle)]
    public void ControllerFamiliesAreClassifiedByProtocolAndHardware(int vendor, int product, string name,
        ControllerProtocolKind protocol, ControllerFamilyKind expected)
    {
        Assert.Equal(expected, ControllerCompatibilityCatalog.Classify((ushort)vendor, (ushort)product, name, protocol));
    }

    [Fact]
    public void CatalogHasStableUniqueChoicesAndBalancedDefault()
    {
        var presets = SimpleEmulationPresetCatalog.All;

        Assert.NotEmpty(presets);
        Assert.Equal(presets.Count, presets.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(presets, preset => Assert.Equal(SimpleEmulationPresetCatalog.CurrentSchemaVersion, preset.SchemaVersion));
        Assert.Equal(EmulationPerformanceGoal.Balanced,
            SimpleEmulationPresetCatalog.Get("balanced").Performance);
    }

    [Theory]
    [InlineData("compatibility", "1", "Auto 4:3/3:2", "false")]
    [InlineData("balanced", "2", "Auto 4:3/3:2", "false")]
    [InlineData("quality", "3", "16:9", "true")]
    [InlineData("four-k", "6", "16:9", "true")]
    public void Pcsx2TranslatesSharedPreset(string id, string scale, string aspect, string widescreen)
    {
        var adapter = new Pcsx2ConfigurationAdapter(Path.GetTempPath(), Path.GetTempPath());

        var profile = adapter.Translate(SimpleEmulationPresetCatalog.Get(id));

        Assert.Equal("pcsx2", profile.EmulatorId);
        Assert.Equal(id, profile.Id);
        Assert.Contains(profile.Values, item => item.Key == "upscale_multiplier" && item.Value == scale);
        Assert.Contains(profile.Values, item => item.Key == "AspectRatio" && item.Value == aspect);
        Assert.Contains(profile.Values, item => item.Key == "EnableWideScreenPatches" && item.Value == widescreen);
    }

    [Fact]
    public void ControllerRequestKeepsDirectSdlLayoutSeparateFromEmulatorSettings()
    {
        var controller = new DetectedGameController("03000000-test", "Test Controller",
            ControllerLayoutKind.Xbox, HasStandardSdlMapping: true, SupportsRumble: true);
        var request = new ControllerAutoConfigurationRequest(controller, 1,
            ControllerFaceButtonPreference.PhysicalPosition,
            [new(CanonicalControllerInput.South, "SDL-0/A")]);

        Assert.True(request.Controller.HasStandardSdlMapping);
        Assert.Equal(CanonicalControllerInput.South, request.Bindings.Single().Input);
        Assert.Equal("SDL-0/A", request.Bindings.Single().SdlBinding);
    }

    [Fact]
    public async Task VerifiedControllerProfileRoundTripsPerEmulatorAndPlatform()
    {
        var root = Path.Combine(Path.GetTempPath(), "clc-controller-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var controller = new DetectedGameController("03000000-test", "Test Controller",
                ControllerLayoutKind.Xbox, true, true);
            var profile = new VerifiedControllerProfile(VerifiedControllerProfileStore.CurrentSchemaVersion,
                "pcsx2", CartLaunchCompanion.Core.Platform.PlatformKind.Windows, controller,
                ControllerFaceButtonPreference.PhysicalPosition,
                [new(CanonicalControllerInput.South, "SDL-0/A")], [CanonicalControllerInput.South], DateTimeOffset.UtcNow);
            var store = new VerifiedControllerProfileStore(root);

            await store.SaveAsync(profile);
            var loaded = await store.LoadAsync("pcsx2", CartLaunchCompanion.Core.Platform.PlatformKind.Windows);

            Assert.NotNull(loaded);
            Assert.Equal(profile.EmulatorId, loaded.EmulatorId);
            Assert.Equal(profile.Platform, loaded.Platform);
            Assert.Equal(profile.Controller, loaded.Controller);
            Assert.Equal(profile.FaceButtonPreference, loaded.FaceButtonPreference);
            Assert.Equal(profile.Bindings, loaded.Bindings);
            Assert.Equal(profile.VerifiedInputs, loaded.VerifiedInputs);
            Assert.Null(await store.LoadAsync("pcsx2", CartLaunchCompanion.Core.Platform.PlatformKind.Linux));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
