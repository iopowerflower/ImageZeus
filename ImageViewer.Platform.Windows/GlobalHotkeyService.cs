using System.Runtime.InteropServices;
using ImageViewer.Core.Models;

namespace ImageViewer.Platform.Windows;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_QUIT = 0x0012;
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;
    private const int MOD_NOREPEAT = 0x4000;

    private readonly object _sync = new();
    private Thread? _messageThread;
    private uint _messageThreadId;
    private int _currentId;
    private bool _disposed;

    public Action? HotkeyPressed;

    public void Register(HotkeyBinding binding)
    {
        var modifiers = MOD_NOREPEAT;
        if (binding.Ctrl) modifiers |= MOD_CONTROL;
        if (binding.Alt) modifiers |= MOD_ALT;
        if (binding.Shift) modifiers |= MOD_SHIFT;
        if (binding.Win) modifiers |= MOD_WIN;

        var vk = KeyNameToVirtualKey(binding.Key);
        if (vk == 0) return;

        lock (_sync)
        {
            if (_disposed) return;
            StopMessageThread();
            _currentId = 1;

            var thread = new Thread(() => RunMessageLoop(_currentId, (uint)modifiers, vk))
            {
                IsBackground = true,
                Name = "ImageZeus Global Hotkey",
            };

            _messageThread = thread;
            thread.Start();
        }
    }

    public void Unregister()
    {
        lock (_sync)
        {
            StopMessageThread();
            _currentId = 0;
        }
    }

    private void RunMessageLoop(int id, uint modifiers, uint vk)
    {
        _messageThreadId = GetCurrentThreadId();

        // Force the thread message queue to exist before RegisterHotKey.
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);

        if (!RegisterHotKey(IntPtr.Zero, id, modifiers, vk))
        {
            return;
        }

        try
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == id)
                {
                    HotkeyPressed?.Invoke();
                }
            }
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, id);
        }
    }

    private static uint KeyNameToVirtualKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        var upper = key.ToUpperInvariant();
        if (upper.Length == 1 && upper[0] >= 'A' && upper[0] <= 'Z')
            return upper[0];
        if (upper.Length == 1 && upper[0] >= '0' && upper[0] <= '9')
            return upper[0];
        return VkKeyScanEx(upper[0], GetKeyboardLayout(0)) & 0xFF;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            StopMessageThread();
        }
    }

    private void StopMessageThread()
    {
        if (_messageThread is null) return;

        var threadId = _messageThreadId;
        if (threadId != 0)
        {
            PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (!_messageThread.Join(millisecondsTimeout: 500))
        {
            // Avoid blocking app shutdown/startup if Windows does not deliver the quit message promptly.
        }

        _messageThread = null;
        _messageThreadId = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint VkKeyScanEx(char ch, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    private const uint PM_NOREMOVE = 0x0000;
}
