using System.IO.Pipes;
using Avalonia;
using Avalonia.Win32;
using ImageViewer.App.Services;

namespace ImageViewer.App;

internal static class Program
{
    private const string MutexName = "ImageZeus_Singleton_Mutex";
    private const string PipeName = "ImageZeus_Pipe";

    [STAThread]
    public static void Main(string[] args)
    {
        var filePath = args.FirstOrDefault(File.Exists);
        var isDaemon = args.Any(a => a.Equals("--daemon", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, MutexName, out var createdNew);

        if (!createdNew)
        {
            if (!string.IsNullOrEmpty(filePath))
                SendPathToDaemon(filePath);
            return;
        }

        var services = new AppServices(args);
        RegisterGlobalExceptionHandlers(services);

        AppBuilder.Configure(() => new App
            {
                Services = services,
                IsDaemonStart = isDaemon,
            })
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = new[]
                {
                    Win32RenderingMode.AngleEgl,
                    Win32RenderingMode.Software,
                },
            })
            .StartWithClassicDesktopLifetime(args);
    }

    private static void SendPathToDaemon(string filePath)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(filePath);
        }
        catch
        {
            // Daemon not responding — fall through and exit silently
        }
    }

    private static void RegisterGlobalExceptionHandlers(AppServices services)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception ex)
            {
                services.CrashLogger.Log(ex, "AppDomain unhandled exception");
                return;
            }

            services.CrashLogger.Log(new Exception("Non-exception AppDomain crash payload"), "AppDomain unhandled exception");
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            services.CrashLogger.Log(eventArgs.Exception, "Unobserved task exception");
            eventArgs.SetObserved();
        };
    }
}
