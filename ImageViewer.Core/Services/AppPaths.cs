namespace ImageViewer.Core.Services;

public static class AppPaths
{
    public static string GetAppDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageZeus");
    }

    public static string GetCrashLogPath() => Path.Combine(GetAppDataDirectory(), "crash.log");

    public static string GetSettingsPath() => Path.Combine(GetAppDataDirectory(), "settings.json");
}
