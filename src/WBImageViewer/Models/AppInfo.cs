namespace WBImageViewer.Models;

/// <summary>Product identity shared by the UI, installer and documentation.</summary>
public static class AppInfo
{
    public const string Name = "该怪叔叔看图";
    public const string Version = "1.2.0";
    public const string GitHub = "https://github.com/yysdgss/guaishushu-image-viewer";
}

/// <summary>Image adjustment values. Zero means the image is completely untouched.</summary>
public sealed class ImageAdjustments
{
    public int Brightness { get; set; }
    public int Contrast { get; set; }
    public int Saturation { get; set; }
    public int Denoise { get; set; }
    public int Sharpen { get; set; }

    public bool IsNeutral => Brightness == 0 && Contrast == 0 && Saturation == 0 &&
                             Denoise == 0 && Sharpen == 0;

    /// <summary>Conservative auto-enhancement preset applied when a picture is opened.</summary>
    public static ImageAdjustments SmartPreset() => new()
    {
        Brightness = 4,
        Contrast = 12,
        Saturation = 8,
        Denoise = 18,
        Sharpen = 32
    };

    public ImageAdjustments Clone() => new()
    {
        Brightness = Brightness,
        Contrast = Contrast,
        Saturation = Saturation,
        Denoise = Denoise,
        Sharpen = Sharpen
    };
}
