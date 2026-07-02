using ImageViewer.Core.Contracts;

namespace ImageViewer.Core.Services;

public sealed class NullCrashLogger : ICrashLogger
{
    public void Log(Exception exception, string context)
    {
    }
}
