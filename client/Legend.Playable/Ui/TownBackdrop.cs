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
        if (Sheet != null && Sheet.TryTile("grass", out var grass))
        {
            for (double x = 0; x < bounds.Width; x += 48)
                context.DrawImage(Sheet.Bitmap, grass, new Rect(x, bounds.Height * 0.62, 48, 32));
            if (Sheet.TryTile("safe", out var stone))
            {
                for (double x = bounds.Width * 0.28; x < bounds.Width * 0.72; x += 48)
                    context.DrawImage(Sheet.Bitmap, stone, new Rect(x, bounds.Height * 0.62, 48, 32));
            }
            if (Sheet.TrySprite("war_m", "idle", 4, 0, out var hero, out _))
                context.DrawImage(Sheet.Bitmap, hero, new Rect(bounds.Width * 0.46, bounds.Height * 0.28, 48, 72));
            if (Sheet.TrySprite("gate", "idle", 4, 0, out var gate, out _))
                context.DrawImage(Sheet.Bitmap, gate, new Rect(bounds.Width * 0.62, bounds.Height * 0.22, 56, 84));
            if (Sheet.TryTile("tree", out var tree))
            {
                context.DrawImage(Sheet.Bitmap, tree, new Rect(18, bounds.Height * 0.34, 48, 48));
                context.DrawImage(Sheet.Bitmap, tree, new Rect(bounds.Width - 64, bounds.Height * 0.34, 48, 48));
            }
        }
        context.FillRectangle(Brush("#31425A"), new Rect(0, bounds.Height * 0.34, bounds.Width, bounds.Height * 0.22));
        context.FillRectangle(Brush("#243246"), new Rect(0, bounds.Height * 0.48, bounds.Width, bounds.Height * 0.18));

        double groundY = bounds.Height * 0.72;
        context.FillRectangle(Brush("#3A2A1C"), new Rect(0, groundY, bounds.Width, bounds.Height - groundY));
        context.FillRectangle(Brush("#5A4030"), new Rect(bounds.Width * 0.38, groundY, bounds.Width * 0.24, bounds.Height - groundY));

        DrawHouse(context, bounds.Width * 0.08, groundY, 70, "#6E4A32");
        DrawHouse(context, bounds.Width * 0.74, groundY, 78, "#5C4030");

        double wallTop = bounds.Height * 0.40;
        context.FillRectangle(Brush("#6E5844"), new Rect(0, wallTop, bounds.Width, groundY - wallTop));
        for (double x = 4; x < bounds.Width; x += 18)
            context.FillRectangle(Brush("#8A7058"), new Rect(x, wallTop - 8, 10, 8));

        double gateX = bounds.Width * 0.5;
        context.FillRectangle(Brush("#2A1C14"), new Rect(gateX - 28, wallTop + 8, 56, groundY - wallTop - 8));
        context.DrawEllipse(null, new Pen(Brush("#E8C872"), 3), new Point(gateX, wallTop + 28), 26, 18);
        context.DrawEllipse(Brush("#E8C872"), null, new Point(gateX - 46, wallTop + 18), 4, 4);
        context.DrawEllipse(Brush("#E8C872"), null, new Point(gateX + 46, wallTop + 18), 4, 4);
        context.DrawEllipse(Brush("#F2D48A"), null, new Point(bounds.Width * 0.18, bounds.Height * 0.16), 7, 7);

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
