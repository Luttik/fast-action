using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FastAction.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace FastAction.Services;

public sealed class IconResolver
{
    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _configDirectory;

    public IconResolver(string configDirectory)
    {
        _configDirectory = configDirectory;
    }

    public async Task<ImageSource?> ResolveAsync(IconConfig? icon)
    {
        if (icon is null)
        {
            return await ResolveLucideAsync("circle");
        }

        var type = string.IsNullOrWhiteSpace(icon.Type) ? "lucide" : icon.Type;
        var cacheKey = $"{type}|{icon.Path}|{icon.Name}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        ImageSource? source = type.ToLowerInvariant() switch
        {
            "app" => await ResolveAppIconAsync(icon.Path),
            "lucide" => await ResolveLucideAsync(icon.Name),
            "svg" => await ResolveSvgAsync(icon.Path),
            _ => await ResolveLucideAsync("circle"),
        };

        if (source is not null)
        {
            _cache[cacheKey] = source;
        }

        return source;
    }

    public void ClearCache() => _cache.Clear();

    private static async Task<ImageSource?> ResolveLucideAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "circle";
        }

        var fileName = name.Trim().ToLowerInvariant();
        if (!fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".svg";
        }

        // Unpackaged WinUI: prefer a real file path over ms-appx (more reliable).
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "lucide", fileName);
        if (!File.Exists(path))
        {
            Debug.WriteLine($"Lucide icon missing: {path}");
            return null;
        }

        return await OpenSvgAsync(path);
    }

    private async Task<ImageSource?> ResolveSvgAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(_configDirectory, path);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return await OpenSvgAsync(fullPath);
    }

    private static async Task<ImageSource?> OpenSvgAsync(string fullPath)
    {
        try
        {
            var svg = new SvgImageSource
            {
                RasterizePixelWidth = 96,
                RasterizePixelHeight = 96,
            };

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(fullPath);
            using var stream = await file.OpenReadAsync();
            var status = await svg.SetSourceAsync(stream);
            if (status == SvgImageSourceLoadStatus.Success)
            {
                return svg;
            }

            Debug.WriteLine($"SVG SetSource status {status} for {fullPath}; trying UriSource.");
            return new SvgImageSource(new Uri(fullPath, UriKind.Absolute))
            {
                RasterizePixelWidth = 96,
                RasterizePixelHeight = 96,
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SVG open failed for {fullPath}: {ex.Message}");
            try
            {
                return new SvgImageSource(new Uri(fullPath, UriKind.Absolute))
                {
                    RasterizePixelWidth = 96,
                    RasterizePixelHeight = 96,
                };
            }
            catch
            {
                return null;
            }
        }
    }

    private static async Task<ImageSource?> ResolveAppIconAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var resolved = ResolveExecutablePath(path) ?? path;

        try
        {
            var pngBytes = ExtractIconPng(resolved);
            if (pngBytes is null || pngBytes.Length == 0)
            {
                return null;
            }

            var ras = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(pngBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            ras.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(ras);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveExecutablePath(string path)
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            return path;
        }

        if (File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), path);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemCandidate = Path.Combine(windir, "System32", path);
        return File.Exists(systemCandidate) ? systemCandidate : null;
    }

    private static byte[]? ExtractIconPng(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
            {
                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
        catch
        {
            // Fall through to shell API.
        }

        return ExtractViaShell(path);
    }

    private static byte[]? ExtractViaShell(string path)
    {
        var shinfo = new ShFileInfo();
        var result = SHGetFileInfo(
            path,
            0,
            ref shinfo,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon);

        if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var icon = Icon.FromHandle(shinfo.hIcon);
            using var bitmap = icon.ToBitmap();
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref ShFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
