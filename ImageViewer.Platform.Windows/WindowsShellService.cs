using System.Diagnostics;
using System.Runtime.InteropServices;
using ImageViewer.Core.Contracts;

namespace ImageViewer.Platform.Windows;

public sealed class WindowsShellService : IShellService
{
    public void ShowInExplorer(string fullPath)
    {
        var canonical = Canonicalize(fullPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{canonical}\"",
            UseShellExecute = true,
        });
    }

    public void OpenProperties(string fullPath)
    {
        var canonical = Canonicalize(fullPath);
        var sei = new SHELLEXECUTEINFO();
        sei.cbSize = Marshal.SizeOf(sei);
        sei.lpVerb = "properties";
        sei.lpFile = canonical;
        sei.fMask = SEE_MASK_INVOKEIDLIST;
        sei.nShow = SW_SHOW;
        ShellExecuteEx(ref sei);
    }

    public void Print(string fullPath)
    {
        var canonical = Canonicalize(fullPath);
        Process.Start(new ProcessStartInfo(canonical)
        {
            Verb = "print",
            UseShellExecute = true,
        });
    }

    public void CopyFilesToClipboard(IReadOnlyList<string> fullPaths, bool cut)
    {
        // Clipboard file drop list requires Windows Forms COM interop, not available here.
        // Fallback: no-op. A proper implementation would use OLE clipboard with HDROP + PreferredDropEffect.
        _ = fullPaths;
        _ = cut;
    }

    public async Task DeleteToRecycleBinAsync(string fullPath, CancellationToken cancellationToken)
    {
        var canonical = Canonicalize(fullPath);
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileOp = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = canonical + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            };

            SHFileOperation(ref fileOp);
        }, cancellationToken);
    }

    private static string Canonicalize(string fullPath)
    {
        return Path.GetFullPath(fullPath);
    }

    #region P/Invoke

    private const int SW_SHOW = 5;
    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    private const int FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public int wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    #endregion
}
