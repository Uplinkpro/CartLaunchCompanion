using System.Text.Json.Serialization;

namespace CartLaunchCompanion.Core.PhysicalCarts;

[JsonSerializable(typeof(CartIdentity))]
[JsonSerializable(typeof(TrustedCartDatabase))]
internal partial class PhysicalCartJsonContext : JsonSerializerContext;
