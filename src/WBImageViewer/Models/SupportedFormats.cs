namespace WBImageViewer.Models;

/// <summary>
/// The single list of file types accepted by the viewer, the file-association
/// registration and the installer. The installer links this source file so the
/// three places can never drift apart.
/// </summary>
public static class SupportedFormats
{
    /// <summary>Formats decoded directly by Windows Imaging Component (WIC).</summary>
    public static readonly string[] Raster =
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".jpe", ".bmp", ".dib", ".gif", ".webp",
        ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif", ".jxl", ".jp2", ".j2k",
        ".jpf", ".jpx", ".wdp", ".hdp", ".exif", ".tga", ".pcx", ".pbm", ".pgm",
        ".ppm", ".pnm", ".sgi", ".ras"
    };

    /// <summary>
    /// Vector/document formats rendered through the Windows shell thumbnail provider.
    /// A matching application must be installed for Windows to generate a thumbnail.
    /// </summary>
    public static readonly string[] RichPreview =
    {
        ".svg", ".svgz", ".emf", ".wmf", ".pdf", ".psd", ".ai", ".eps",
        ".xmind", ".mm", ".drawio", ".vsdx", ".vsd", ".vdw", ".vst", ".graphml"
    };

    /// <summary>Guaishushu editable annotated-picture container.</summary>
    public static readonly string[] ProjectFiles = { ".gjpg" };

    public static string[] All => Raster.Concat(RichPreview).Concat(ProjectFiles).ToArray();

    /// <summary>Human-readable list used by the About window and README.</summary>
    public const string DisplayText =
        "直接解码：JPG/JPEG/JFIF/JPE、PNG、BMP/DIB、GIF、WebP、TIFF、ICO、HEIC/HEIF、AVIF、JXL、JP2/J2K、WDP/HDP、EXIF、TGA、PCX、PBM/PGM/PPM/PNM 等。" +
        "Windows 缩略图预览：SVG/SVGZ、EMF/WMF、PDF、PSD/AI/EPS，以及 XMind(.xmind)、FreeMind(.mm)、draw.io(.drawio)、Visio(.vsdx/.vsd/.vdw) 等流程图与脑图文件（需系统已安装对应软件以生成缩略图）。";
}
