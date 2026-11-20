using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WBImageViewer.Services;

/// <summary>
/// Real PDF rendering through the Windows PDF engine. Pages are rendered into a frozen
/// Bgra32 bitmap so the viewer's annotation/adjustment pipeline can treat them like any
/// other picture. No third-party native library is needed on Windows 10/11.
/// </summary>
internal static class PdfService
{
    public static bool IsPdf(string path) =>
        string.Equals(System.IO.Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static async Task<PdfDocument> OpenAsync(string path)
    {
        // Load from a stream instead of StorageFile.GetFileFromPathAsync: the latter
        // mangles non-ASCII paths in unpackaged desktop apps on some Windows builds.
        var bytes = await System.IO.File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync().AsTask().ConfigureAwait(false);
            await writer.FlushAsync().AsTask().ConfigureAwait(false);
            // DataWriter disposes its stream unless explicitly detached; the PDF
            // engine needs the stream to stay alive.
            writer.DetachStream();
        }
        stream.Seek(0);
        return await PdfDocument.LoadFromStreamAsync(stream).AsTask().ConfigureAwait(false);
    }

    public static async Task<BitmapSource?> RenderPageAsync(PdfDocument document, int pageIndex, int maxPixels = 2600)
    {
        if (pageIndex < 0 || pageIndex >= document.PageCount) return null;
        var page = document.GetPage((uint)pageIndex);
        try
        {
            var size = page.Size;
            var scale = Math.Min(1.0, maxPixels / Math.Max(1.0, Math.Max(size.Width, size.Height)));
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, (int)Math.Round(size.Width * scale)),
                DestinationHeight = (uint)Math.Max(1, (int)Math.Round(size.Height * scale))
            };

            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, options).AsTask().ConfigureAwait(false);
            stream.Seek(0);

            var bytes = new byte[(int)stream.Size];
            if (bytes.Length > 0)
            {
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)bytes.Length).AsTask().ConfigureAwait(false);
                reader.ReadBytes(bytes);
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new System.IO.MemoryStream(bytes);
            image.EndInit();
            BitmapSource result = image;
            if (result.Format != PixelFormats.Bgra32)
                result = new FormatConvertedBitmap(result, PixelFormats.Bgra32, null, 0);
            result.Freeze();
            return result;
        }
        finally
        {
        }
    }
}
