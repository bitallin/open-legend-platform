using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Legend.Playable.Ui;

/// <summary>Self-authored main-interface panels from ui_chrome.png. Not an official Mir UI dump.</summary>
public static class MirChrome
{
    public static Bitmap? Bitmap { get; } = Load();

    public static IBrush? Slot { get; } = Brush(0, 0, 80, 44);
    public static IBrush? HpTrack { get; } = Brush(0, 48, 180, 18);
    public static IBrush? MpTrack { get; } = Brush(0, 70, 180, 18);
    public static IBrush? BottomBar { get; } = Brush(0, 96, 512, 110);
    public static IBrush? MapFrame { get; } = Brush(200, 0, 150, 150);
    public static IBrush? BagFrame { get; } = Brush(360, 160, 148, 190);

    private static Bitmap? Load()
    {
        var stream = typeof(MirChrome).Assembly.GetManifestResourceStream("assets.ui_chrome.png");
        return stream == null ? null : new Bitmap(stream);
    }

    private static IBrush? Brush(double x, double y, double w, double h)
    {
        if (Bitmap == null)
            return null;
        return new ImageBrush(Bitmap)
        {
            Stretch = Stretch.Fill,
            SourceRect = new RelativeRect(x, y, w, h, RelativeUnit.Absolute)
        };
    }
}
