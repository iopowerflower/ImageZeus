namespace ImageViewer.Core.Contracts;

public interface IShellService
{
    void ShowInExplorer(string fullPath);

    void OpenProperties(string fullPath);

    void Print(string fullPath);

    void CopyFilesToClipboard(IReadOnlyList<string> fullPaths, bool cut);

    Task DeleteToRecycleBinAsync(string fullPath, CancellationToken cancellationToken);
}
