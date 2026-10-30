using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Legend.Playable.Ui;

/// <summary>Character-select portrait taken from the original 8-direction sheet.</summary>
public sealed class ClassPortrait : Control
{
    private static readonly MirSheet? Sheet = MirSheet.TryLoad(typeof(ClassPortrait).Assembly);

    public string ClassId { get; set; } = "warrior";
    public string Gender { get; set; } = "m";

    public override void Render(DrawingContext context)
    {
        double w = Math.Max(32, Bounds.Width);
        double h = Math.Max(48, Bounds.Height);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#1A120C")), new Rect(0, 0, w, h));
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#E8C872")), 1), new Rect(1, 1, w - 2, h - 2));
        if (Sheet == null)
            return;
        string gender = string.Equals(Gender, "f", StringComparison.OrdinalIgnoreCase) ? "f" : "m";
        string actor = (ClassId ?? "").ToLowerInvariant() switch
        {
            "mage" => "mage_" + gender,
            "taoist" => "tao_" + gender,
            _ => "war_" + gender
        };
        if (!Sheet.TrySprite(actor, "idle", 4, 0, out var src, out _))
            return;
        context.DrawImage(Sheet.Bitmap, src, new Rect(8, 6, w - 16, h - 12));
    }
}
