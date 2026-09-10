using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ObsidianTaskbarIcons;

/// <summary>Turns any reasonable icon source (.ico, .png, .jpg, .bmp, .exe, .dll) into a multi-size .ico file.</summary>
internal static class IconConverter
{
    public static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    private static readonly string[] ModuleExtensions = { ".exe", ".dll" };
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public static string FileFilter =>
        "Icons and images|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|" +
        "Icon files (*.ico)|*.ico|Images (*.png;*.jpg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|" +
        "Programs and libraries (*.exe;*.dll)|*.exe;*.dll|All files|*.*";

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".ico" || ModuleExtensions.Contains(ext) || ImageExtensions.Contains(ext);
    }

    public static void ToIcoFile(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext == ".ico")
        {
            File.Copy(source, dest, overwrite: true);
            return;
        }

        var frames = ModuleExtensions.Contains(ext) ? FramesFromModule(source) : FramesFromImage(source);
        WriteIco(dest, frames);
    }

    /// <summary>Best-effort preview bitmap for the UI; null if the source cannot be read.</summary>
    public static Bitmap? Preview(string source, int size)
    {
        try
        {
            var ext = Path.GetExtension(source).ToLowerInvariant();
            if (ext == ".ico")
            {
                using var icon = new Icon(source, size, size);
                return icon.ToBitmap();
            }

            if (ModuleExtensions.Contains(ext))
            {
                return ExtractFromModule(source, size);
            }

            using var img = Image.FromFile(source);
            return Fit(img, size);
        }
        catch
        {
            return null;
        }
    }

    private static List<(int Size, byte[] Png)> FramesFromImage(string path)
    {
        using var img = Image.FromFile(path);
        var frames = new List<(int, byte[])>();
        foreach (var size in Sizes)
        {
            using var bmp = Fit(img, size);
            frames.Add((size, PngBytes(bmp)));
        }

        return frames;
    }

    private static List<(int Size, byte[] Png)> FramesFromModule(string path)
    {
        var frames = new List<(int, byte[])>();
        foreach (var size in Sizes)
        {
            using var bmp = ExtractFromModule(path, size);
            if (bmp is null) continue;
            frames.Add((size, PngBytes(bmp)));
        }

        if (frames.Count == 0)
        {
            throw new InvalidOperationException($"No icon resource found in {path}");
        }

        return frames;
    }

    private static Bitmap? ExtractFromModule(string path, int size)
    {
        var handles = new IntPtr[1];
        var ids = new uint[1];
        var count = PrivateExtractIconsW(path, 0, size, size, handles, ids, 1, 0);
        if (count == 0 || handles[0] == IntPtr.Zero) return null;
        try
        {
            using var icon = Icon.FromHandle(handles[0]);
            return icon.ToBitmap();
        }
        finally
        {
            DestroyIcon(handles[0]);
        }
    }

    private static Bitmap Fit(Image src, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        var scale = Math.Min((double)size / src.Width, (double)size / src.Height);
        var w = Math.Max(1, (int)Math.Round(src.Width * scale));
        var h = Math.Max(1, (int)Math.Round(src.Height * scale));
        g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
        return bmp;
    }

    private static byte[] PngBytes(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Writes an ICONDIR with PNG-compressed entries (supported by Windows Vista and later).</summary>
    private static void WriteIco(string dest, List<(int Size, byte[] Png)> frames)
    {
        using var fs = File.Create(dest);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0);            // reserved
        bw.Write((ushort)1);            // type: icon
        bw.Write((ushort)frames.Count);

        var offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            var dim = (byte)(size >= 256 ? 0 : size);
            bw.Write(dim);              // width
            bw.Write(dim);              // height
            bw.Write((byte)0);          // color count
            bw.Write((byte)0);          // reserved
            bw.Write((ushort)1);        // planes
            bw.Write((ushort)32);       // bit count
            bw.Write(png.Length);       // bytes in resource
            bw.Write(offset);           // image offset
            offset += png.Length;
        }

        foreach (var (_, png) in frames)
        {
            bw.Write(png);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PrivateExtractIconsW(string lpszFile, int nIconIndex, int cxIcon, int cyIcon,
        IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
