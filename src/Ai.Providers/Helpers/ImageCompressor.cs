using SkiaSharp;

namespace FieldCure.Ai.Providers.Helpers;

/// <summary>
/// Compresses images to fit within API size limits.
/// Uses progressive strategy: quality reduction first, then resolution scaling.
/// </summary>
public static class ImageCompressor
{
    /// <summary>Default maximum size in bytes (4 MB, leaving margin for base64 overhead).</summary>
    private const int DefaultMaxBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Compresses an image to fit within the API size limit.
    /// Converts large PNG/BMP/TIFF to JPEG and progressively reduces quality then resolution.
    /// Returns the original bytes unchanged if already under the limit.
    /// </summary>
    /// <param name="imageBytes">Original image bytes.</param>
    /// <param name="maxBytes">Maximum allowed size in bytes.</param>
    /// <returns>Compressed image bytes and the resulting MIME type.</returns>
    public static (byte[] Data, string MimeType) CompressForApi(
        byte[] imageBytes, int maxBytes = DefaultMaxBytes)
    {
        if (imageBytes.Length <= maxBytes)
            return (imageBytes, DetectMimeType(imageBytes));

        using var original = SKBitmap.Decode(imageBytes);
        if (original is null)
            return (imageBytes, DetectMimeType(imageBytes));

        // Flatten alpha channel onto white background for JPEG conversion
        using var flattened = FlattenAlpha(original);
        var source = flattened ?? original;

        // Strategy: quality reduction first (less information loss), then resize
        // Step 1: JPEG quality 85
        var result = EncodeAsJpeg(source, 85);
        if (result.Length <= maxBytes)
            return (result, "image/jpeg");

        // Step 2: JPEG quality 70
        result = EncodeAsJpeg(source, 70);
        if (result.Length <= maxBytes)
            return (result, "image/jpeg");

        // Step 3-5: Progressive resize from original bitmap (no cumulative degradation)
        foreach (var scale in new[] { 0.75f, 0.50f, 0.25f })
        {
            var w = (int)(source.Width * scale);
            var h = (int)(source.Height * scale);
            if (w < 1 || h < 1) continue;

            using var resized = source.Resize(new SKImageInfo(w, h), SKSamplingOptions.Default);
            if (resized is null) continue;

            result = EncodeAsJpeg(resized, 70);
            if (result.Length <= maxBytes)
                return (result, "image/jpeg");
        }

        // Fallback: return the smallest we could produce
        return (result, "image/jpeg");
    }

    #region Private Methods

    /// <summary>
    /// Flattens an image with alpha channel onto a white background.
    /// Returns null if the image has no alpha channel (no flattening needed).
    /// </summary>
    private static SKBitmap? FlattenAlpha(SKBitmap bitmap)
    {
        if (bitmap.AlphaType == SKAlphaType.Opaque)
            return null;

        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var flattened = new SKBitmap(info);

        using var canvas = new SKCanvas(flattened);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(bitmap, 0, 0);

        return flattened;
    }

    /// <summary>
    /// Encodes a bitmap as JPEG with the specified quality.
    /// </summary>
    private static byte[] EncodeAsJpeg(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    /// <summary>
    /// Detects the MIME type from the image file header bytes.
    /// </summary>
    private static string DetectMimeType(byte[] data)
    {
        if (data.Length >= 8)
        {
            // PNG: 89 50 4E 47
            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
                return "image/png";

            // JPEG: FF D8 FF
            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
                return "image/jpeg";

            // GIF: 47 49 46
            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46)
                return "image/gif";

            // WebP: RIFF....WEBP
            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
                && data.Length >= 12 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
                return "image/webp";

            // BMP: 42 4D
            if (data[0] == 0x42 && data[1] == 0x4D)
                return "image/bmp";
        }

        return "image/png"; // default fallback
    }

    #endregion
}
