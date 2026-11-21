# =============================================================================
#  VisionPlatform app icon - raster + .ico generator
#
#  Draws the mark with GDI+ in a 1000 x 1000 unit space, supersamples up to
#  2048 px, box-filters down in premultiplied alpha, then writes into
#  <repo>/assets/icons :
#     png/app-{16,24,32,48,64,128,256}.png
#     png/splash-mark.png       transparent mark for SplashForm
#     app.ico                   9 sizes (BMP <= 48, PNG >= 64)
#
#  Zero dependencies. ASCII only: PowerShell 5.1 reads .ps1 as ANSI and would
#  mangle non-ASCII literals.
#
#  Usage (from repo root):
#     powershell -ExecutionPolicy Bypass -File tools\icon-design\generate-icons.ps1
#
#  Keep the geometry in sync with make-svg.py - both must describe one design.
# =============================================================================

Add-Type -AssemblyName System.Drawing

$code = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class VisionIcon
{
    // ------------------------- design spec (unit space) -------------------------
    const float U = 1000f;
    const float TileRadius = 216f;
    const float Cx = 500f, Cy = 500f;
    const float ROut = 318f, RIn = 184f;
    const float BladeGapDeg = 0f;              // seams come from the stroke, not from a gap
    const float BracketInset = 120f, BracketArm = 136f, BracketWidth = 20f;

    static readonly Color TileTop    = Color.FromArgb(60, 60, 74);
    static readonly Color TileMid    = Color.FromArgb(38, 38, 50);
    static readonly Color TileBottom = Color.FromArgb(21, 21, 28);

    static readonly Color BladeA = Color.FromArgb(40, 168, 222);
    static readonly Color BladeB = Color.FromArgb(56, 192, 240);
    static readonly Color Hole   = Color.FromArgb(9, 9, 15);

    public static void Generate(string outDir)
    {
        string pngDir = Path.Combine(outDir, "png");
        Directory.CreateDirectory(pngDir);

        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        for (int i = 0; i < sizes.Length; i++)
        {
            using (Bitmap b = Render(sizes[i], false))
                b.Save(Path.Combine(pngDir, "app-" + sizes[i] + ".png"), ImageFormat.Png);
        }

        using (Bitmap b = Render(512, true))
            b.Save(Path.Combine(pngDir, "splash-mark.png"), ImageFormat.Png);

        WriteIco(Path.Combine(outDir, "app.ico"), new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 });
        Console.WriteLine("icons written to " + outDir);
    }

    static Bitmap Render(int size, bool markOnly)
    {
        int ss = (int)Math.Floor(2048f / size);
        if (ss < 1) ss = 1;
        if (ss > 8) ss = 8;
        int work = size * ss;

        Bitmap big = new Bitmap(work, work, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(big))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);

            float k = work / U;
            g.ScaleTransform(k, k);

            if (!markOnly) DrawTile(g);
            DrawMark(g, size, markOnly);
        }

        if (ss == 1) return big;
        Bitmap small = Downsample(big, size);
        big.Dispose();
        return small;
    }

    // dark tile: vertical gradient + accent glow + inner rim
    static void DrawTile(Graphics g)
    {
        RectangleF rect = new RectangleF(0, 0, U, U);
        using (GraphicsPath tile = RoundedRect(rect, TileRadius))
        {
            using (LinearGradientBrush lg = new LinearGradientBrush(
                new RectangleF(0, 0, U, U + 1), TileTop, TileBottom, 90f))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] { TileTop, TileMid, TileBottom };
                cb.Positions = new float[] { 0f, 0.44f, 1f };
                lg.InterpolationColors = cb;
                g.FillPath(lg, tile);
            }

            GraphicsState st = g.Save();
            g.SetClip(tile, CombineMode.Replace);
            FillRadial(g, 500f, 440f, 500f,
                Color.FromArgb(56, 0, 160, 230), Color.FromArgb(0, 0, 160, 230));
            g.Restore(st);

            using (GraphicsPath rim = RoundedRect(new RectangleF(4, 4, U - 8, U - 8), TileRadius - 5))
            using (Pen p = new Pen(Color.FromArgb(30, 255, 255, 255), 3f))
                g.DrawPath(p, rim);

            using (Pen p = new Pen(Color.FromArgb(70, 0, 0, 0), 2f))
                g.DrawPath(p, tile);
        }
    }

    static void DrawMark(Graphics g, int size, bool markOnly)
    {
        bool simple = size < 32;                   // optical size switch
        bool brackets = (size >= 32 || markOnly);
        bool reticle = (size >= 96 || (markOnly && size >= 96));

        if (brackets) DrawBrackets(g);

        if (simple) DrawSimpleLens(g);
        else DrawIris(g, reticle, markOnly);
    }

    static void DrawBrackets(Graphics g)
    {
        float a = BracketInset, b = BracketInset + BracketArm;
        PointF[][] paths = new PointF[][]
        {
            new PointF[] { new PointF(a, b), new PointF(a, a), new PointF(b, a) },
            new PointF[] { new PointF(U - b, a), new PointF(U - a, a), new PointF(U - a, b) },
            new PointF[] { new PointF(U - a, U - b), new PointF(U - a, U - a), new PointF(U - b, U - a) },
            new PointF[] { new PointF(b, U - a), new PointF(a, U - a), new PointF(a, U - b) }
        };

        using (Pen p = new Pen(Color.FromArgb(205, 79, 200, 245), BracketWidth))
        {
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            for (int i = 0; i < paths.Length; i++)
                g.DrawLines(p, paths[i]);
        }
    }

    static void DrawIris(Graphics g, bool reticle, bool markOnly)
    {
        if (!markOnly)
        {
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                g.FillEllipse(sb, Cx - ROut - 5, Cy - ROut - 5, (ROut + 5) * 2, (ROut + 5) * 2);
        }

        float gap = BladeGapDeg;
        for (int i = 0; i < 6; i++)
        {
            float a0 = i * 60f + gap * 0.5f;
            float a1 = (i + 1) * 60f - gap * 0.5f;

            using (GraphicsPath bp = new GraphicsPath())
            {
                bp.AddArc(Cx - ROut, Cy - ROut, ROut * 2, ROut * 2, a0, a1 - a0);
                bp.AddLine(Pt(ROut, a1), Pt(RIn, a1));
                bp.AddLine(Pt(RIn, a1), Pt(RIn, a0));
                bp.CloseFigure();

                Color c = (i % 2 == 0) ? BladeA : BladeB;
                using (SolidBrush sb = new SolidBrush(c))
                    g.FillPath(sb, bp);

                // seam stroke: keeps blade edges dark on any background.
                // A real angular gap would leak the backdrop as a white sliver.
                using (Pen sp = new Pen(Color.FromArgb(235, 11, 16, 24), 7f))
                {
                    sp.LineJoin = LineJoin.Round;
                    g.DrawPath(sp, bp);
                }
            }
        }

        // lighting pass over the blades: top highlight -> bottom shade
        RectangleF bounds = new RectangleF(Cx - ROut, Cy - ROut, ROut * 2, ROut * 2);
        using (GraphicsPath ring = new GraphicsPath())
        {
            ring.AddEllipse(bounds);
            GraphicsState st = g.Save();
            g.SetClip(ring, CombineMode.Replace);
            using (LinearGradientBrush lg = new LinearGradientBrush(bounds, Color.White, Color.Black, 90f))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] { Color.FromArgb(34, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(78, 0, 0, 0) };
                cb.Positions = new float[] { 0f, 0.46f, 1f };
                lg.InterpolationColors = cb;
                g.FillPath(lg, ring);
            }
            g.Restore(st);
        }

        // aperture bore (hexagon) + lit rim
        PointF[] hex = new PointF[6];
        for (int i = 0; i < 6; i++) hex[i] = Pt(RIn, i * 60f);

        using (GraphicsPath hp = new GraphicsPath())
        {
            hp.AddPolygon(hex);
            using (SolidBrush sb = new SolidBrush(Hole)) g.FillPath(sb, hp);

            GraphicsState st = g.Save();
            g.SetClip(hp, CombineMode.Replace);
            FillRadial(g, Cx, Cy, RIn,
                Color.FromArgb(0, 70, 200, 250), Color.FromArgb(96, 40, 170, 230));
            g.Restore(st);

            using (Pen p = new Pen(Color.FromArgb(120, 140, 230, 255), 4f))
                g.DrawPolygon(p, hex);
        }

        if (reticle)
        {
            using (Pen p = new Pen(Color.FromArgb(135, 208, 240, 255), 7f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                g.DrawLine(p, Cx - 122, Cy, Cx - 38, Cy);
                g.DrawLine(p, Cx + 38, Cy, Cx + 122, Cy);
                g.DrawLine(p, Cx, Cy - 122, Cx, Cy - 38);
                g.DrawLine(p, Cx, Cy + 38, Cx, Cy + 122);
            }
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(235, 160, 235, 255)))
                g.FillEllipse(sb, Cx - 15, Cy - 15, 30, 30);
        }

        // glass highlight on the upper-left of the ring
        using (Pen p = new Pen(Color.FromArgb(80, 255, 255, 255), 11f))
        {
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            g.DrawArc(p, Cx - ROut + 7, Cy - ROut + 7, (ROut - 7) * 2, (ROut - 7) * 2, 197, 44);
        }
    }

    // bold lens for 16 / 24 px: thick ring, no small details
    static void DrawSimpleLens(Graphics g)
    {
        const float rO = 372f, rI = 206f;
        RectangleF bo = new RectangleF(Cx - rO, Cy - rO, rO * 2, rO * 2);

        using (GraphicsPath dp = new GraphicsPath())
        {
            dp.FillMode = FillMode.Alternate;
            dp.AddEllipse(bo);
            dp.AddEllipse(Cx - rI, Cy - rI, rI * 2, rI * 2);
            using (LinearGradientBrush lg = new LinearGradientBrush(bo, Color.FromArgb(86, 214, 252), Color.FromArgb(0, 140, 205), 90f))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] { Color.FromArgb(96, 216, 252), Color.FromArgb(0, 168, 228), Color.FromArgb(0, 120, 186) };
                cb.Positions = new float[] { 0f, 0.5f, 1f };
                lg.InterpolationColors = cb;
                g.FillPath(lg, dp);
            }
        }
    }

    static PointF Pt(float r, float deg)
    {
        double a = deg * Math.PI / 180.0;
        return new PointF(Cx + (float)(r * Math.Cos(a)), Cy + (float)(r * Math.Sin(a)));
    }

    static GraphicsPath RoundedRect(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static void FillRadial(Graphics g, float cx, float cy, float r, Color inner, Color outer)
    {
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddEllipse(cx - r, cy - r, r * 2, r * 2);
            using (PathGradientBrush b = new PathGradientBrush(p))
            {
                b.CenterPoint = new PointF(cx, cy);
                b.CenterColor = inner;
                b.SurroundColors = new Color[] { outer };
                g.FillPath(b, p);
            }
        }
    }

    // exact area-average downscale in premultiplied space (no dark fringes)
    static Bitmap Downsample(Bitmap src, int size)
    {
        int sw = src.Width, sh = src.Height;
        int f = sw / size;
        Bitmap dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        BitmapData sd = src.LockBits(new Rectangle(0, 0, sw, sh), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] buf = new byte[sd.Stride * sh];
        Marshal.Copy(sd.Scan0, buf, 0, buf.Length);
        int stride = sd.Stride;
        src.UnlockBits(sd);

        BitmapData dd = dst.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] outBuf = new byte[dd.Stride * size];
        int ostride = dd.Stride;

        int n = f * f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                long sa = 0, sr = 0, sg = 0, sb = 0;
                for (int yy = 0; yy < f; yy++)
                {
                    int row = (y * f + yy) * stride;
                    for (int xx = 0; xx < f; xx++)
                    {
                        int o = row + (x * f + xx) * 4;
                        int a = buf[o + 3];
                        sa += a;
                        sr += (long)buf[o + 2] * a;
                        sg += (long)buf[o + 1] * a;
                        sb += (long)buf[o + 0] * a;
                    }
                }
                int oa = (int)(sa / n);
                int or_ = sa == 0 ? 0 : (int)(sr / sa);
                int og = sa == 0 ? 0 : (int)(sg / sa);
                int ob = sa == 0 ? 0 : (int)(sb / sa);

                int idx = y * ostride + x * 4;
                outBuf[idx + 0] = (byte)ob;
                outBuf[idx + 1] = (byte)og;
                outBuf[idx + 2] = (byte)or_;
                outBuf[idx + 3] = (byte)oa;
            }
        }

        Marshal.Copy(outBuf, 0, dd.Scan0, outBuf.Length);
        dst.UnlockBits(dd);
        return dst;
    }

    // -------------------------------- .ico ------------------------------------
    static void WriteIco(string path, int[] sizes)
    {
        byte[][] payload = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using (Bitmap b = Render(sizes[i], false))
                payload[i] = sizes[i] >= 64 ? ToPng(b) : ToBmp(b);
        }

        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((short)0);              // reserved
            w.Write((short)1);              // type: icon
            w.Write((short)sizes.Length);

            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);           // palette
                w.Write((byte)0);           // reserved
                w.Write((short)1);          // planes
                w.Write((short)32);         // bpp
                w.Write(payload[i].Length);
                w.Write(offset);
                offset += payload[i].Length;
            }
            for (int i = 0; i < sizes.Length; i++) w.Write(payload[i]);
            w.Flush();
        }
    }

    static byte[] ToPng(Bitmap b)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            b.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // 32bpp DIB payload (bottom-up BGRA + AND mask) for legacy small sizes
    static byte[] ToBmp(Bitmap b)
    {
        int w = b.Width, h = b.Height;
        int maskStride = ((w + 31) / 32) * 4;

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter bw = new BinaryWriter(ms))
        {
            bw.Write(40);
            bw.Write(w);
            bw.Write(h * 2);
            bw.Write((short)1);
            bw.Write((short)32);
            bw.Write(0);
            bw.Write(w * 4 * h + maskStride * h);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    Color c = b.GetPixel(x, y);
                    bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                }

            byte[] row = new byte[maskStride];
            for (int y = h - 1; y >= 0; y--)
            {
                Array.Clear(row, 0, row.Length);
                for (int x = 0; x < w; x++)
                {
                    if (b.GetPixel(x, y).A < 128) row[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
                bw.Write(row);
            }
            bw.Flush();
            return ms.ToArray();
        }
    }
}
'@

Add-Type -TypeDefinition $code -ReferencedAssemblies "System.Drawing.dll"

# tools\icon-design\ -> repo root is two levels up
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $here)
$outDir = Join-Path $root "assets\icons"

[VisionIcon]::Generate($outDir)
