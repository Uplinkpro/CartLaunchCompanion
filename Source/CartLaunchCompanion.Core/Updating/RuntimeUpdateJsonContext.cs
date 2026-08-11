using System.Text.Json.Serialization;

namespace CartLaunchCompanion.Core.Updating;

[JsonSerializable(typeof(RuntimeUpdateManifest))]
[JsonSerializable(typeof(RuntimeUpdateJournal))]
internal partial class RuntimeUpdateJsonContext : JsonSerializerContext;
