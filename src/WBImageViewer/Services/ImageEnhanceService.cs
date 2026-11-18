using WBImageViewer.Models;

namespace WBImageViewer.Services;

/// <summary>
/// Small, dependency-free image pipeline. Everything runs only when the image or an
/// adjustment changes - there is no per-frame cost and no GPU render loop.
/// </summary>
internal static class ImageEnhanceService
{
    public static void Apply(int[] source, int[] destination, int width, int height,
        ImageAdjustments adjustments)
    {
        if (source.Length != destination.Length)
            throw new ArgumentException("Source and destination buffers must have the same length.");

        Array.Copy(source, destination, source.Length);
        if (width <= 0 || height <= 0) return;

        var denoise = adjustments.Denoise;
        if (denoise > 0)
            EdgeAwareDenoise(destination, width, height, denoise);

        var brightness = adjustments.Brightness;
        var contrast = adjustments.Contrast;
        var saturation = adjustments.Saturation;
        var contrastFactor = (100 + contrast) / 100.0;
        var saturationFactor = (100 + saturation) / 100.0;
        var brightnessDelta = brightness * 2;

        if (brightness != 0 || contrast != 0 || saturation != 0)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                var pixel = destination[i];
                if ((pixel & unchecked((int)0xFF000000)) == 0) continue;

                var b = pixel & 0xFF;
                var g = (pixel >> 8) & 0xFF;
                var r = (pixel >> 16) & 0xFF;
                var a = (int)((uint)pixel >> 24);

                r = Clamp((int)((r + brightnessDelta - 128) * contrastFactor + 128));
                g = Clamp((int)((g + brightnessDelta - 128) * contrastFactor + 128));
                b = Clamp((int)((b + brightnessDelta - 128) * contrastFactor + 128));

                if (saturation != 0)
                {
                    var gray = (r * 77 + g * 150 + b * 29) >> 8;
                    r = Clamp((int)(gray + (r - gray) * saturationFactor));
                    g = Clamp((int)(gray + (g - gray) * saturationFactor));
                    b = Clamp((int)(gray + (b - gray) * saturationFactor));
                }

                destination[i] = (a << 24) | (r << 16) | (g << 8) | b;
            }
        }

        var sharpen = adjustments.Sharpen;
        if (sharpen <= 0) return;

        var blur = new int[destination.Length];
        GaussianBlur(destination, blur, width, height);
        var strength = sharpen / 100.0 * 0.85;

        for (var i = 0; i < destination.Length; i++)
        {
            var pixel = destination[i];
            if ((pixel & unchecked((int)0xFF000000)) == 0) continue;
            var blurred = blur[i];
            var a = (int)((uint)pixel >> 24);
            var r = Clamp((int)((pixel >> 16 & 0xFF) + ((pixel >> 16 & 0xFF) - (blurred >> 16 & 0xFF)) * strength));
            var g = Clamp((int)((pixel >> 8 & 0xFF) + ((pixel >> 8 & 0xFF) - (blurred >> 8 & 0xFF)) * strength));
            var b = Clamp((int)((pixel & 0xFF) + ((pixel & 0xFF) - (blurred & 0xFF)) * strength));
            destination[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }
    }

    /// <summary>Edge-aware 3x3 smoothing: noise is reduced without flattening real edges.</summary>
    private static void EdgeAwareDenoise(int[] pixels, int width, int height, int amount)
    {
        var blend = amount / 100.0 * 0.62;
        var threshold = 6 + amount / 8;
        var copy = (int[])pixels.Clone();

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var index = row + x;
                var center = copy[index];
                var cr = center >> 16 & 0xFF;
                var cg = center >> 8 & 0xFF;
                var cb = center & 0xFF;
                var sumR = 0;
                var sumG = 0;
                var sumB = 0;
                var count = 0;

                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= height) continue;
                    var nrow = ny * width;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        if (nx < 0 || nx >= width) continue;
                        var n = copy[nrow + nx];
                        var nr = n >> 16 & 0xFF;
                        var ng = n >> 8 & 0xFF;
                        var nb = n & 0xFF;
                        var distance = Math.Abs(nr - cr) + Math.Abs(ng - cg) + Math.Abs(nb - cb);
                        if (distance > threshold) continue;
                        sumR += nr;
                        sumG += ng;
                        sumB += nb;
                        count++;
                    }
                }

                if (count < 2) continue;
                var inv = 1.0 / count;
                var ar = sumR * inv;
                var ag = sumG * inv;
                var ab = sumB * inv;
                var alpha = (int)((uint)center >> 24);
                pixels[index] = (alpha << 24) |
                    (Clamp((int)(cr + (ar - cr) * blend)) << 16) |
                    (Clamp((int)(cg + (ag - cg) * blend)) << 8) |
                    Clamp((int)(cb + (ab - cb) * blend));
            }
        }
    }

    private static void GaussianBlur(int[] source, int[] destination, int width, int height)
    {
        const int stride1 = 1;
        const int stride2 = 2;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var sumR = 0;
                var sumG = 0;
                var sumB = 0;
                var total = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = Math.Clamp(y + dy, 0, height - 1);
                    var nrow = ny * width;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = Math.Clamp(x + dx, 0, width - 1);
                        var weight = (dx == 0 ? stride2 : stride1) * (dy == 0 ? stride2 : stride1);
                        var pixel = source[nrow + nx];
                        sumR += (pixel >> 16 & 0xFF) * weight;
                        sumG += (pixel >> 8 & 0xFF) * weight;
                        sumB += (pixel & 0xFF) * weight;
                        total += weight;
                    }
                }
                var index = row + x;
                var alpha = (int)((uint)source[index] >> 24);
                destination[index] = (alpha << 24) |
                    (sumR / total << 16) | (sumG / total << 8) | (sumB / total);
            }
        }
    }

    private static int Clamp(int value) => value < 0 ? 0 : value > 255 ? 255 : value;
}
