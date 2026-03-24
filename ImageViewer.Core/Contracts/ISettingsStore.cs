using ImageViewer.Core.Models;

namespace ImageViewer.Core.Contracts;

public interface ISettingsStore
{
    Task<ViewerSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(ViewerSettings settings, CancellationToken cancellationToken);
}
