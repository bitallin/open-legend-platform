using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Legend.Playable.Ui;

/// <summary>Original login scene: gate, wall, houses, and lanterns. Not a ripped Mir backdrop.</summary>
public sealed class TownBackdrop : Control
{
    private static readonly MirSheet? Sheet = MirSheet.TryLoad(typeof(TownBackdrop).Assembly);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height));
        context.FillRectangle(Brush("#1C2838"), bounds);
        context.DrawEllipse(Brush("#F2D48A"), null, new Point(bounds.Width * 0.18, bounds.Height * 0.16), 7, 7);
        if (Sheet != null && Sheet.TryTile("grass", out var grass))
        {
            var order = new List<(int x, int y)>();
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++)
                order.Add((x, y));
            order.Sort((a, b) => (b.x + b.y).CompareTo(a.x + a.y));
            foreach (var (x, y) in order)
            {
                var (sx, sy) = Legend.Playable.World.IsoProjection.ToScreen(x, y, 3.5, 1.2, bounds.Width, bounds.Height);
                sy += 18;
                string tile = x is >= 2 and <= 5 && y is 1 or 2 ? "safe" : "grass";
                if (!Sheet.TryTile(tile, out var src))
                    src = grass;
                double hw = 32, hh = 16;
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(sx, sy - hh), true);
                    g.LineTo(new Point(sx + hw, sy));
                    g.LineTo(new Point(sx, sy + hh));
                    g.LineTo(new Point(sx - hw, sy));
                    g.EndFigure(true);
                }
                using (context.PushGeometryClip(geo))
                    context.DrawImage(Sheet.Bitmap, src, new Rect(sx - hw, sy - hh, hw * 2, hh * 2));
            }
            if (Sheet.TrySprite("gate", "idle", 4, 0, out var gateSprite, out _))
                context.DrawImage(Sheet.Bitmap, gateSprite, new Rect(bounds.Width * 0.58, bounds.Height * 0.22, 56, 84));
            if (Sheet.TrySprite("war_m", "idle", 4, 0, out var hero, out _))
                context.DrawImage(Sheet.Bitmap, hero, new Rect(bounds.Width * 0.42, bounds.Height * 0.34, 48, 72));
            if (Sheet.TryTile("tree", out var tree))
            {
                context.DrawImage(Sheet.Bitmap, tree, new Rect(12, bounds.Height * 0.36, 40, 44));
                context.DrawImage(Sheet.Bitmap, tree, new Rect(bounds.Width - 52, bounds.Height * 0.36, 40, 44));
            }
        }

        var caption = new FormattedText(
            "城门  ·  原野  ·  灯火",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei, Noto Sans CJK SC, sans-serif")),
            16,
            Brush("#E8C872"));
        context.DrawText(caption, new Point((bounds.Width - caption.Width) / 2, bounds.Height - 24));
    }

    private static void DrawHouse(DrawingContext context, double x, double groundY, double width, string wall)
    {
        double h = 46;
        context.FillRectangle(Brush(wall), new Rect(x, groundY - h, width, h));
        var roof = new StreamGeometry();
        using (var g = roof.Open())
        {
            g.BeginFigure(new Point(x - 6, groundY - h), true);
            g.LineTo(new Point(x + width / 2, groundY - h - 22));
            g.LineTo(new Point(x + width + 6, groundY - h));
            g.EndFigure(true);
        }
        context.DrawGeometry(Brush("#7A3030"), null, roof);
        context.FillRectangle(Brush("#F2D48A"), new Rect(x + 12, groundY - 28, 10, 12));
        context.FillRectangle(Brush("#F2D48A"), new Rect(x + width - 24, groundY - 28, 10, 12));
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
