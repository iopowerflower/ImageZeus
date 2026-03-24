using System.IO.Pipes;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ImageViewer.App.Services;
using ImageViewer.App.ViewModels;

namespace ImageViewer.App;

public partial class App : Application
{
    private const string PipeName = "ImageZeus_Pipe";

    private CancellationTokenSource? _pipeCts;
    private readonly List<(MainWindow Window, MainWindowViewModel ViewModel)> _viewers = new();

    public required AppServices Services { get; init; }
    public bool IsDaemonStart { get; init; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Services.CrashLogger.Log(e.Exception, "UI thread unhandled exception");
            e.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (!IsDaemonStart)
            {
                var filePath = Services.Args.FirstOrDefault(File.Exists);
                OpenNewViewer(filePath);
            }

            StartPipeServer();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void OpenNewViewer(string? filePath)
    {
        var args = string.IsNullOrEmpty(filePath) ? Array.Empty<string>() : new[] { filePath };
        var childServices = Services.CreateChild(args);

        var viewModel = new MainWindowViewModel(childServices);
        var window = new MainWindow { DataContext = viewModel };

        window.Closed += (_, _) =>
        {
            _viewers.RemoveAll(v => ReferenceEquals(v.Window, window));
            viewModel.Dispose();
        };

        _viewers.Add((window, viewModel));
        window.Show();
        window.Activate();
        window.Topmost = true;

        Dispatcher.UIThread.Post(() =>
        {
            ForceForeground(window);
            window.Topmost = false;
        }, DispatcherPriority.Input);
    }

    private static void ForceForeground(Window window)
    {
        if (window.TryGetPlatformHandle() is { } handle)
        {
            var hwnd = handle.Handle;
            var foreground = GetForegroundWindow();
            var foregroundThread = GetWindowThreadProcessId(foreground, out _);
            var currentThread = GetCurrentThreadId();

            if (foregroundThread != currentThread)
            {
                AttachThreadInput(currentThread, foregroundThread, true);
                SetForegroundWindow(hwnd);
                AttachThreadInput(currentThread, foregroundThread, false);
            }
            else
            {
                SetForegroundWindow(hwnd);
            }
        }

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void OnTrayClicked(object? sender, EventArgs e)
    {
        OpenNewViewer(null);
    }

    private void OnTrayOpenClick(object? sender, EventArgs e)
    {
        OpenNewViewer(null);
    }

    private void OnTrayExitClick(object? sender, EventArgs e)
    {
        ShutdownApp();
    }

    private void ShutdownApp()
    {
        _pipeCts?.Cancel();

        foreach (var (window, viewModel) in _viewers.ToArray())
        {
            viewModel.Dispose();
            window.Close();
        }
        _viewers.Clear();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        System.IO.Pipes.PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server);
                    var path = await reader.ReadLineAsync(token);

                    server.Disconnect();
                    await server.DisposeAsync();
                    server = null;

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        Dispatcher.UIThread.Post(() => OpenNewViewer(path));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Services.CrashLogger.Log(ex, "Pipe server error");
                }
                finally
                {
                    if (server is not null)
                        await server.DisposeAsync();
                }
            }
        }, token);
    }
}
