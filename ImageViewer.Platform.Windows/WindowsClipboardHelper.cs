using System.Runtime.InteropServices;

namespace ImageViewer.Platform.Windows;

public static class WindowsClipboardHelper
{
    public static bool CopyBitmapToClipboard(byte[] bgraPixels, int width, int height, int srcStride)
    {
        var dibStride = width * 4;
        var imageSize = dibStride * height;
        var headerSize = 40;
        var dibSize = headerSize + imageSize;

        var hGlobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)dibSize);
        if (hGlobal == IntPtr.Zero) return false;

        var ptr = GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            GlobalFree(hGlobal);
            return false;
        }

        try
        {
            unsafe
            {
                var dst = (byte*)ptr;

                // BITMAPINFOHEADER
                WriteInt32(dst, 0, headerSize);
                WriteInt32(dst, 4, width);
                WriteInt32(dst, 8, height); // positive = bottom-up
                WriteInt16(dst, 12, 1);     // biPlanes
                WriteInt16(dst, 14, 32);    // biBitCount
                WriteInt32(dst, 16, 0);     // BI_RGB
                WriteInt32(dst, 20, imageSize);
                // biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant = 0 (zeroinit)

                fixed (byte* src = bgraPixels)
                {
                    for (var y = 0; y < height; y++)
                    {
                        var srcRow = src + y * srcStride;
                        var dstRow = dst + headerSize + (height - 1 - y) * dibStride;
                        Buffer.MemoryCopy(srcRow, dstRow, dibStride, dibStride);
                    }
                }
            }
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            GlobalFree(hGlobal);
            return false;
        }

        try
        {
            EmptyClipboard();
            var result = SetClipboardData(CF_DIB, hGlobal);
            return result != IntPtr.Zero;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static unsafe void WriteInt32(byte* dst, int offset, int value)
    {
        *(int*)(dst + offset) = value;
    }

    private static unsafe void WriteInt16(byte* dst, int offset, short value)
    {
        *(short*)(dst + offset) = value;
    }

    private const uint CF_DIB = 8;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
}
