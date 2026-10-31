using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Legend.Playable.Ui;

/// <summary>
/// Stone-gold chrome from Assets/chrome (prototype kit R1). Original crops / authored tiles — not an official Mir dump.
/// </summary>
public static class MirChrome
{
    public static Bitmap? LegacySheet { get; } = LoadBmp("assets.ui_chrome.png");

    public static Bitmap? Orb { get; } = LoadBmp("assets.chrome.orb-hpmp-full.png");
    public static Bitmap? ChatFrame { get; } = LoadBmp("assets.chrome.chat-frame.png");
    public static Bitmap? BrCluster { get; } = LoadBmp("assets.chrome.br-cluster.png");
    public static Bitmap? MinimapFrame { get; } = LoadBmp("assets.chrome.minimap-frame.png");
    public static Bitmap? BagWindow { get; } = LoadBmp("assets.chrome.bag-window-full.png");
    public static Bitmap? EquipWindow { get; } = LoadBmp("assets.chrome.equip-window-full.png");
    public static Bitmap? SlotEmpty { get; } = LoadBmp("assets.chrome.slot-empty-32.png");
    public static Bitmap? SlotSelected { get; } = LoadBmp("assets.chrome.slot-selected.png");
    public static Bitmap? HotbarSlot { get; } = LoadBmp("assets.chrome.hotbar-slot.png");
    public static Bitmap? InputField { get; } = LoadBmp("assets.chrome.input-field.png");
    public static Bitmap? CurrencyRow { get; } = LoadBmp("assets.chrome.currency-row.png");
    public static Bitmap? PanelBg { get; } = LoadBmp("assets.chrome.panel-bg-tile.png");
    public static Bitmap? Titlebar { get; } = LoadBmp("assets.chrome.frame-titlebar-tile.png");
    public static Bitmap? EdgeH { get; } = LoadBmp("assets.chrome.frame-edge-h.png");
    public static Bitmap? EdgeV { get; } = LoadBmp("assets.chrome.frame-edge-v.png");
    public static Bitmap? CornerTl { get; } = LoadBmp("assets.chrome.frame-corner-tl.png");
    public static Bitmap? CornerTr { get; } = LoadBmp("assets.chrome.frame-corner-tr.png");
    public static Bitmap? CornerBl { get; } = LoadBmp("assets.chrome.frame-corner-bl.png");
    public static Bitmap? CornerBr { get; } = LoadBmp("assets.chrome.frame-corner-br.png");
    public static Bitmap? BtnPrimary { get; } = LoadBmp("assets.chrome.btn-primary-default.png");
    public static Bitmap? BtnNormal { get; } = LoadBmp("assets.chrome.btn-normal-default.png");
    public static Bitmap? BtnDanger { get; } = LoadBmp("assets.chrome.btn-danger-default.png");
    public static Bitmap? BtnPrimaryHover { get; } = LoadBmp("assets.chrome.btn-primary-hover.png");
    public static Bitmap? BtnNormalHover { get; } = LoadBmp("assets.chrome.btn-normal-hover.png");
    public static Bitmap? TabActive { get; } = LoadBmp("assets.chrome.tab-active.png");
    public static Bitmap? TabInactive { get; } = LoadBmp("assets.chrome.tab-inactive.png");
    public static Bitmap? MenuTextBtn { get; } = LoadBmp("assets.chrome.menu-text-btn.png");
    public static Bitmap? CheckboxOn { get; } = LoadBmp("assets.chrome.checkbox-on.png");
    public static Bitmap? CheckboxOff { get; } = LoadBmp("assets.chrome.checkbox-off.png");
    public static Bitmap? BtnClose { get; } = LoadBmp("assets.chrome.btn-close.png");

    // Legacy ImageBrush slices kept so older callers still compile.
    public static IBrush? Slot { get; } = BrushOf(HotbarSlot) ?? LegacyBrush(0, 0, 80, 44);
    public static IBrush? HpTrack { get; } = LegacyBrush(0, 48, 180, 18);
    public static IBrush? MpTrack { get; } = LegacyBrush(0, 70, 180, 18);
    public static IBrush? BottomBar { get; } = BrushOf(BrCluster) ?? LegacyBrush(0, 96, 512, 110);
    public static IBrush? MapFrame { get; } = BrushOf(MinimapFrame) ?? LegacyBrush(200, 0, 150, 150);
    public static IBrush? BagFrame { get; } = BrushOf(BagWindow) ?? LegacyBrush(360, 160, 148, 190);

    public static IBrush? PanelBgBrush { get; } = TileBrush(PanelBg);
    public static IBrush? OrbBrush { get; } = BrushOf(Orb);
    public static IBrush? ChatBrush { get; } = BrushOf(ChatFrame);
    public static IBrush? EquipBrush { get; } = BrushOf(EquipWindow);
    public static IBrush? InputBrush { get; } = BrushOf(InputField);

    public static Image? Img(Bitmap? bmp, double? w = null, double? h = null)
    {
        if (bmp == null) return null;
        var image = new Image { Source = bmp, Stretch = Stretch.Fill };
        if (w is double ww) image.Width = ww;
        if (h is double hh) image.Height = hh;
        return image;
    }

    public static void StyleField(TextBox box)
    {
        box.Background = InputBrush ?? new SolidColorBrush(Color.Parse("#2A1C14"));
        box.Foreground = new SolidColorBrush(Color.Parse("#F6E6C0"));
        box.BorderBrush = new SolidColorBrush(Color.Parse("#8A6A38"));
        box.BorderThickness = new Thickness(1);
        box.CaretBrush = new SolidColorBrush(Color.Parse("#E8C872"));
        box.MinHeight = 28;
        box.Padding = new Thickness(8, 4);
    }

    public static void StylePrimary(Button button) => StyleButton(button, BtnPrimary, "#1A1208", "#F6E6C0");
    public static void StyleNormal(Button button) => StyleButton(button, BtnNormal, "#1A1208", "#F0D8A8");
    public static void StyleDanger(Button button) => StyleButton(button, BtnDanger, "#2A0C0C", "#F6D0C0");

    public static void StyleButton(Button button, Bitmap? skin, string fallbackBg, string fg)
    {
        button.Background = BrushOf(skin) ?? new SolidColorBrush(Color.Parse(fallbackBg));
        button.Foreground = new SolidColorBrush(Color.Parse(fg));
        button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(12, 4);
        button.MinHeight = 28;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        if (button.FontSize < 12)
            button.FontSize = 13;
    }

    public static void StyleSys(Button button)
    {
        button.Background = BrushOf(MenuTextBtn) ?? new SolidColorBrush(Color.Parse("#AA1A140F"));
        button.Foreground = new SolidColorBrush(Color.Parse("#F6E6C0"));
        button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(8, 2);
        button.FontSize = 12;
    }

    /// <summary>Nine-slice stone-gold frame around content.</summary>
    public static Border Framed(Control child, double contentWidth, double? maxContentHeight = null, Thickness? padding = null)
    {
        const double corner = 28;
        var pad = padding ?? new Thickness(14, 18, 14, 14);
        Control body = child;
        if (maxContentHeight is double mh)
            body = new ScrollViewer { Content = child, MaxHeight = mh };

        var inner = new Border
        {
            Background = PanelBgBrush ?? new SolidColorBrush(Color.Parse("#CC1C1612")),
            Padding = pad,
            Child = body,
            Width = contentWidth
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(corner, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(corner, GridUnitType.Pixel));
        grid.RowDefinitions.Add(new RowDefinition(corner, GridUnitType.Pixel));
        // Auto (not Star): overlays/panels must hug content; Star stretched EdgeV to full HUD height.
        grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Auto));
        grid.RowDefinitions.Add(new RowDefinition(corner, GridUnitType.Pixel));

        void Cell(int c, int r, Control? ctrl)
        {
            if (ctrl == null) return;
            Grid.SetColumn(ctrl, c);
            Grid.SetRow(ctrl, r);
            grid.Children.Add(ctrl);
        }

        Cell(0, 0, StretchImg(CornerTl));
        Cell(1, 0, StretchImg(Titlebar ?? EdgeH));
        Cell(2, 0, StretchImg(CornerTr));
        Cell(0, 1, StretchImg(EdgeV));
        Cell(1, 1, inner);
        Cell(2, 1, StretchImg(EdgeV));
        Cell(0, 2, StretchImg(CornerBl));
        Cell(1, 2, StretchImg(EdgeH));
        Cell(2, 2, StretchImg(CornerBr));

        return new Border
        {
            Child = grid,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>Full-art window chrome (bag/equip) with content overlaid in the interior.</summary>
    public static Border ArtWindow(Bitmap? art, Control content, double width, double height, Thickness contentMargin)
    {
        var root = new Grid { Width = width, Height = height };
        if (art != null)
        {
            root.Children.Add(new Image
            {
                Source = art,
                Stretch = Stretch.Fill,
                Width = width,
                Height = height
            });
        }
        else
        {
            root.Children.Add(new Border
            {
                Background = PanelBgBrush ?? new SolidColorBrush(Color.Parse("#EE1C1612")),
                BorderBrush = new SolidColorBrush(Color.Parse("#E8C872")),
                BorderThickness = new Thickness(2)
            });
        }
        var host = new Border
        {
            Margin = contentMargin,
            Child = content,
            Background = Brushes.Transparent
        };
        root.Children.Add(host);
        return new Border { Child = root, Background = Brushes.Transparent };
    }

    public static Bitmap? ItemIcon(int index1To16)
    {
        int i = Math.Clamp(index1To16, 1, 16);
        return LoadBmp($"assets.chrome.items.item-{i:00}.png");
    }

    private static Image? StretchImg(Bitmap? bmp)
    {
        if (bmp == null) return null;
        return new Image { Source = bmp, Stretch = Stretch.Fill };
    }

    private static IBrush? BrushOf(Bitmap? bmp)
    {
        if (bmp == null) return null;
        return new ImageBrush(bmp) { Stretch = Stretch.Fill };
    }

    private static IBrush? TileBrush(Bitmap? bmp)
    {
        if (bmp == null) return null;
        return new ImageBrush(bmp)
        {
            Stretch = Stretch.None,
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height, RelativeUnit.Absolute)
        };
    }

    private static IBrush? LegacyBrush(double x, double y, double w, double h)
    {
        if (LegacySheet == null) return null;
        return new ImageBrush(LegacySheet)
        {
            Stretch = Stretch.Fill,
            SourceRect = new RelativeRect(x, y, w, h, RelativeUnit.Absolute)
        };
    }


    public static Button MakeTab(string text, bool active)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 64,
            MinHeight = 26,
            FontSize = 12,
            Padding = new Thickness(10, 2),
            Foreground = new SolidColorBrush(Color.Parse(active ? "#1A1208" : "#F0D8A8")),
            Background = BrushOf(active ? TabActive : TabInactive)
                ?? new SolidColorBrush(Color.Parse(active ? "#E8C872" : "#2A1C14")),
            BorderThickness = new Thickness(0)
        };
        return button;
    }

    public static Border ListRow(Control content, bool selected = false)
    {
        return new Border
        {
            Background = selected
                ? new SolidColorBrush(Color.Parse("#AA6A4A18"))
                : new SolidColorBrush(Color.Parse("#6614100C")),
            BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#E8C872" : "#5A4030")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4),
            Margin = new Thickness(0, 0, 0, 3),
            Child = content
        };
    }

    public static Border SlotCell(Control? inner = null, double size = 40)
    {
        var grid = new Grid { Width = size, Height = size };
        if (SlotEmpty != null)
            grid.Children.Add(new Image { Source = SlotEmpty, Stretch = Stretch.Fill, Width = size, Height = size });
        if (inner != null)
            grid.Children.Add(inner);
        return new Border
        {
            Width = size + 2,
            Height = size + 2,
            Margin = new Thickness(1),
            Child = grid
        };
    }

    public static ProgressBar ThinBar(string hex, double value, double max)
    {
        return new ProgressBar
        {
            Minimum = 0,
            Maximum = Math.Max(1, max),
            Value = Math.Clamp(value, 0, Math.Max(1, max)),
            Height = 8,
            Foreground = new SolidColorBrush(Color.Parse(hex)),
            Background = new SolidColorBrush(Color.Parse("#140C0A"))
        };
    }

    private static Bitmap? LoadBmp(string logical)
    {
        var stream = typeof(MirChrome).Assembly.GetManifestResourceStream(logical);
        return stream == null ? null : new Bitmap(stream);
    }
}
