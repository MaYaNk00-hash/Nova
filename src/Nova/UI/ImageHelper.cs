using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace Nova.UI;

/// <summary>
/// Lightweight helper for safe WPF BitmapSource creation and resource management.
/// </summary>
public static class ImageHelper
{
    /// <summary>
    /// Creates a frozen, thread-safe BitmapSource from raw image byte array.
    /// Returns null if bytes are invalid or null.
    /// </summary>
    public static BitmapSource? CreateBitmapSourceFromBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze(); // Freezes to allow cross-thread access and release memory
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}

