using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WBImageViewer.Services;

/// <summary>
/// Last-resort decoder for formats WIC cannot open directly (SVG, PDF, XMind,
/// draw.io, Visio, ...). Windows asks the shell thumbnail provider associated with
/// the file type for a bitmap, so no third-party library is required. If a matching
/// application is not installed, the associated icon is used instead.
/// </summary>
internal static class ShellPreviewService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Cx;
        public int Cy;
    }

    [Flags]
    private enum SigbFlags
    {
        ResizeToFit = 0,
        BiggerSizeOk = 1,
        MemoryOnly = 2,
        IconOnly = 4,
        ThumbnailOnly = 8,
        InCacheOnly = 16,
        ScaleUp = 256
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Size size, SigbFlags flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path, IntPtr bindContext, ref Guid riid, out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    public static BitmapSource? TryLoad(string path, int maxSize = 2048)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory is null)
            return null;

        try
        {
            var size = new Size { Cx = maxSize, Cy = maxSize };
            var bitmap = IntPtr.Zero;

            // Prefer a real thumbnail; fall back to the associated icon when the
            // file type has no thumbnail handler installed.
            var hr = factory.GetImage(size,
                SigbFlags.BiggerSizeOk | SigbFlags.ThumbnailOnly | SigbFlags.ScaleUp, out bitmap);
            if (hr != 0 || bitmap == IntPtr.Zero)
                hr = factory.GetImage(size, SigbFlags.BiggerSizeOk | SigbFlags.IconOnly | SigbFlags.ScaleUp, out bitmap);

            if (hr != 0 || bitmap == IntPtr.Zero) return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }
}
