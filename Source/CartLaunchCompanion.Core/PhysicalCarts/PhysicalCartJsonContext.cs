using System.Text.Json.Serialization;
using CartLaunchCompanion.Core.Updating;

namespace CartLaunchCompanion.Core.PhysicalCarts;

[JsonSerializable(typeof(CartIdentity))]
[JsonSerializable(typeof(TrustedCartDatabase))]
[JsonSerializable(typeof(TrustedRuntimeApproval))]
[JsonSerializable(typeof(RuntimeUpdateFile))]
internal partial class PhysicalCartJsonContext : JsonSerializerContext;
