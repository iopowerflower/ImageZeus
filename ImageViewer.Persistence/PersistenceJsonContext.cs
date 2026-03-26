using System.Text.Json.Serialization;
using ImageViewer.Core.Models;

namespace ImageViewer.Persistence;

[JsonSerializable(typeof(ViewerSettings))]
[JsonSerializable(typeof(WindowGeometry))]
[JsonSerializable(typeof(Dictionary<string, uint>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public partial class PersistenceJsonContext : JsonSerializerContext;
