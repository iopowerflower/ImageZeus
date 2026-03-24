namespace ImageViewer.Core.Contracts;

public interface IRatingService
{
    Task<uint?> GetRatingAsync(string fullPath, CancellationToken cancellationToken);

    Task SetRatingAsync(string fullPath, uint rating, CancellationToken cancellationToken);
}
