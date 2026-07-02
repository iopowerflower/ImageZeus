using System.Runtime.InteropServices;
using ImageViewer.Imaging.Models;

namespace ImageViewer.Platform.Windows;

public sealed class ScreenCaptureService
{
    public InMemoryImageSource? CaptureVirtualDesktop()
    {
        var virtualX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var virtualY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var virtualW = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var virtualH = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (virtualW <= 0 || virtualH <= 0) return null;

        var hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero) return null;

        try
        {
            var hdcMem = CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero) return null;

            try
            {
                var hBitmap = CreateCompatibleBitmap(hdcScreen, virtualW, virtualH);
                if (hBitmap == IntPtr.Zero) return null;

                try
                {
                    var hOld = SelectObject(hdcMem, hBitmap);

                    try
                    {
                        if (!BitBlt(hdcMem, 0, 0, virtualW, virtualH, hdcScreen, virtualX, virtualY, SRCCOPY))
                            return null;

                        var bmi = new BITMAPINFO
                        {
                            biSize = 40,
                            biWidth = virtualW,
                            biHeight = -virtualH, // top-down
                            biPlanes = 1,
                            biBitCount = 32,
                            biCompression = BI_RGB,
                        };

                        var stride = virtualW * 4;
                        var pixels = new byte[stride * virtualH];

                        var result = GetDIBits(hdcMem, hBitmap, 0, (uint)virtualH, pixels, ref bmi, DIB_RGB_COLORS);
                        if (result == 0) return null;

                        return new InMemoryImageSource(pixels, virtualW, virtualH, stride, "Snip", virtualX, virtualY);
                    }
                    finally
                    {
                        SelectObject(hdcMem, hOld);
                    }
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            finally
            {
                DeleteDC(hdcMem);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    public static InMemoryImageSource Crop(InMemoryImageSource source, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return source;

        var srcStride = source.Stride;
        var dstStride = width * 4;
        var pixels = new byte[dstStride * height];

        for (var row = 0; row < height; row++)
        {
            var srcY = y + row;
            if (srcY < 0 || srcY >= source.Height) continue;
            var srcOffset = srcY * srcStride + Math.Max(0, x) * 4;
            var dstOffset = row * dstStride + (x < 0 ? -x * 4 : 0);
            var copyBytes = Math.Min(srcStride - Math.Max(0, x) * 4, dstStride - (x < 0 ? -x * 4 : 0));
            if (copyBytes <= 0) continue;
            Buffer.BlockCopy(source.BgraPixels, srcOffset, pixels, dstOffset, copyBytes);
        }

        return new InMemoryImageSource(pixels, width, height, dstStride, source.Title);
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public uint biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }
}
