#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
// Draws the app icon: a battery on a dark rounded square, in the widget's palette.
// Run: dotnet run installer/make_icon.cs [preview.png]  →  src/BattaryChecker/Assets/app.ico
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var root = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", ".."));
var icoPath = Path.Combine(root, "src", "BattaryChecker", "Assets", "app.ico");

var frames = sizes.Select(s => (Size: s, Png: Png(Draw(s)))).ToList();
using (var ico = new BinaryWriter(File.Create(icoPath)))
{
    ico.Write((short)0); ico.Write((short)1); ico.Write((short)frames.Count);
    var offset = 6 + 16 * frames.Count;
    foreach (var (s, png) in frames)
    {
        ico.Write((byte)(s >= 256 ? 0 : s)); ico.Write((byte)(s >= 256 ? 0 : s));
        ico.Write((byte)0); ico.Write((byte)0);
        ico.Write((short)1); ico.Write((short)32);
        ico.Write(png.Length); ico.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in frames)
        ico.Write(png);
}
Console.WriteLine($"{icoPath}: {string.Join(", ", sizes)} px");

if (args.Length > 0) // preview strip of every size on light and dark backgrounds
{
    var w = sizes.Sum() + 10 * (sizes.Length + 1);
    using var strip = new Bitmap(w, 2 * 256 + 30);
    using var g = Graphics.FromImage(strip);
    g.FillRectangle(Brushes.White, 0, 0, w, 256 + 15);
    g.FillRectangle(new SolidBrush(Color.FromArgb(0x20, 0x20, 0x20)), 0, 256 + 15, w, 256 + 15);
    var x = 10;
    foreach (var s in sizes)
    {
        using var bmp = Draw(s);
        g.DrawImage(bmp, x, 5); g.DrawImage(bmp, x, 256 + 20);
        x += s + 10;
    }
    strip.Save(args[0], ImageFormat.Png);
}

static Bitmap Draw(int s)
{
    var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    float u = s / 32f; // design grid: 32 units

    using (var bg = Rounded(0.5f * u, 0.5f * u, 31 * u, 31 * u, 7 * u))
    {
        g.FillPath(new SolidBrush(Color.FromArgb(0x1C, 0x1C, 0x21)), bg);
        if (s >= 32)
            g.DrawPath(new Pen(Color.FromArgb(0x3F, 0x3F, 0x46), Math.Max(1, u)), bg);
    }

    // Battery body, terminal and a ~70 % green fill.
    float stroke = Math.Max(1.5f, 2.2f * u);
    float bx = 5 * u, by = 10 * u, bw = 19.5f * u, bh = 12 * u;
    var light = Color.FromArgb(0xF4, 0xF4, 0xF5);
    using (var body = Rounded(bx, by, bw, bh, 2.5f * u))
        g.DrawPath(new Pen(light, stroke), body);
    using (var cap = Rounded(bx + bw + 0.8f * u, by + 3.5f * u, 2.7f * u, bh - 7 * u, 1 * u))
        g.FillPath(new SolidBrush(light), cap);
    float pad = stroke / 2 + Math.Max(1, 1.3f * u);
    using (var fill = Rounded(bx + pad, by + pad, (bw - 2 * pad) * 0.7f, bh - 2 * pad, 1 * u))
        g.FillPath(new SolidBrush(Color.FromArgb(0x10, 0xB9, 0x81)), fill);
    return bmp;
}

static GraphicsPath Rounded(float x, float y, float w, float h, float r)
{
    var p = new GraphicsPath();
    var d = Math.Min(2 * r, Math.Min(w, h));
    p.AddArc(x, y, d, d, 180, 90);
    p.AddArc(x + w - d, y, d, d, 270, 90);
    p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
    p.AddArc(x, y + h - d, d, d, 90, 90);
    p.CloseFigure();
    return p;
}

static byte[] Png(Bitmap bmp)
{
    using (bmp)
    using (var ms = new MemoryStream())
    {
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
