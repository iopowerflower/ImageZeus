using System.Text.Json.Serialization;
using ImageViewer.Core.Models;

namespace ImageViewer.Persistence;

[JsonSerializable(typeof(ViewerSettings))]
[JsonSerializable(typeof(Dictionary<string, uint>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
internal partial class PersistenceJsonContext : JsonSerializerContext;
