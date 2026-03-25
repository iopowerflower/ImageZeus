using System.Text.Json;
using ImageViewer.Core.Contracts;
using ImageViewer.Core.Models;

namespace ImageViewer.Persistence;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _settingsPath;

    public JsonSettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public async Task<ViewerSettings> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new ViewerSettings();

            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync(
                stream, PersistenceJsonContext.Default.ViewerSettings, cancellationToken);
            return settings ?? new ViewerSettings();
        }
        catch
        {
            return new ViewerSettings();
        }
    }

    public async Task SaveAsync(ViewerSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temp = _settingsPath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(
                stream, settings, PersistenceJsonContext.Default.ViewerSettings, cancellationToken);
        }

        File.Move(temp, _settingsPath, true);
    }
}
