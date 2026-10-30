using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Legend.Playable.Ui;

/// <summary>Original class portrait drawn from class id and gender. Not a ripped character render.</summary>
public sealed class ClassPortrait : Control
{
    public string ClassId { get; set; } = "warrior";
    public string Gender { get; set; } = "m";

    public override void Render(DrawingContext context)
    {
        double w = Math.Max(32, Bounds.Width);
        double h = Math.Max(48, Bounds.Height);
        context.FillRectangle(Brush("#241910"), new Rect(0, 0, w, h));
        context.DrawRectangle(null, new Pen(Brush("#E8C872"), 1), new Rect(1, 1, w - 2, h - 2));

        bool female = string.Equals(Gender, "f", StringComparison.OrdinalIgnoreCase);
        string cls = (ClassId ?? "").ToLowerInvariant();
        var robe = cls switch
        {
            "mage" => Brush("#3A3E78"),
            "taoist" => Brush("#2E5A45"),
            _ => Brush("#7A3A32")
        };
        var hair = female ? Brush("#3A2418") : Brush("#1A120E");
        double cx = w / 2;
        context.FillRectangle(robe, new Rect(cx - 16, h * 0.48, 32, h * 0.42));
        context.DrawEllipse(Brush("#E6C2A0"), null, new Point(cx, h * 0.34), female ? 11 : 12, female ? 13 : 12);
        context.FillRectangle(hair, new Rect(cx - 12, h * 0.18, 24, female ? 16 : 10));
        if (female)
            context.FillRectangle(hair, new Rect(cx - 14, h * 0.28, 6, 22));

        if (cls == "mage")
        {
            context.DrawLine(new Pen(Brush("#D7C4FF"), 3), new Point(cx + 18, h * 0.42), new Point(cx + 18, h * 0.78));
            context.DrawEllipse(Brush("#F2D48A"), null, new Point(cx + 18, h * 0.38), 4, 4);
        }
        else if (cls == "taoist")
        {
            context.DrawEllipse(Brush("#E8C872"), null, new Point(cx, h * 0.62), 5, 5);
            context.DrawEllipse(null, new Pen(Brush("#E8C872"), 1), new Point(cx, h * 0.62), 9, 9);
        }
        else
        {
            context.DrawLine(new Pen(Brush("#D0D4DC"), 3), new Point(cx + 18, h * 0.40), new Point(cx + 22, h * 0.78));
            context.DrawLine(new Pen(Brush("#E8C872"), 2), new Point(cx + 14, h * 0.40), new Point(cx + 26, h * 0.40));
        }
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
