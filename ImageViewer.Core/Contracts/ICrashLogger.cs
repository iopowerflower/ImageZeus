namespace ImageViewer.Core.Contracts;

public interface ICrashLogger
{
    void Log(Exception exception, string context);
}
