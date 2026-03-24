using System.Text;
using ImageViewer.Core.Contracts;

namespace ImageViewer.Core.Services;

public sealed class FileCrashLogger : ICrashLogger
{
    private readonly string _logPath;
    private readonly object _sync = new();

    public FileCrashLogger(string logPath)
    {
        _logPath = logPath;
    }

    public void Log(Exception exception, string context)
    {
        try
        {
            var directory = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTimeOffset.UtcNow:O}] {context}");
            builder.AppendLine(exception.ToString());
            builder.AppendLine(new string('-', 80));

            lock (_sync)
            {
                File.AppendAllText(_logPath, builder.ToString());
            }
        }
        catch
        {
            // Never throw from crash logging.
        }
    }
}
