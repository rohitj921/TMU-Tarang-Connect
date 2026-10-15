using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TMUTarangConnect.Services;

public static class ResourceHelper
{
    private static readonly Dictionary<string, System.Drawing.Icon> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static BitmapImage? LoadBitmap(string iconName)
    {
        try
        {
            // 1. Try WPF pack URI from embedded assembly resource (100% self-contained)
            var packUri = new Uri($"pack://application:,,,/TMUTarangConnect;component/Resources/{iconName}", UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = packUri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { }

        try
        {
            // 2. Fallback to physical disk file if present
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", iconName);
            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }
        catch { }

        return null;
    }

    public static System.Drawing.Icon LoadDrawingIcon(string iconName)
    {
        if (_iconCache.TryGetValue(iconName, out var cached))
        {
            return cached;
        }

        try
        {
            // 1. Try loading stream from embedded WPF resource
            var packUri = new Uri($"pack://application:,,,/TMUTarangConnect;component/Resources/{iconName}", UriKind.Absolute);
            var resStream = System.Windows.Application.GetResourceStream(packUri);
            if (resStream != null)
            {
                using var stream = resStream.Stream;
                using var bmp = new System.Drawing.Bitmap(stream);
                using var resized = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(32, 32));
                var hIcon = resized.GetHicon();
                var ico = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
                _iconCache[iconName] = ico;
                return ico;
            }
        }
        catch { }

        try
        {
            // 2. Fallback to physical disk file
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", iconName);
            if (File.Exists(path))
            {
                using var bmp = new System.Drawing.Bitmap(path);
                using var resized = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(32, 32));
                var hIcon = resized.GetHicon();
                var ico = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
                _iconCache[iconName] = ico;
                return ico;
            }
        }
        catch { }

        try
        {
            // 3. Fallback to executable's embedded icon
            var appIco = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule?.FileName ?? "");
            if (appIco != null)
            {
                _iconCache[iconName] = appIco;
                return appIco;
            }
        }
        catch { }

        return System.Drawing.SystemIcons.Application;
    }
}
