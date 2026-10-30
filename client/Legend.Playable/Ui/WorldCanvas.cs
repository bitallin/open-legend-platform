using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Legend.Contracts.Proto;
using Legend.Playable.Session;
using Legend.Playable.World;

namespace Legend.Playable.Ui;

public sealed class WorldCanvas : Control
{
    private static readonly Typeface Face = new(new FontFamily(
        "Microsoft YaHei, Noto Sans CJK SC, WenQuanYi Micro Hei, sans-serif"));
    private static readonly IBrush Backdrop = new SolidColorBrush(Color.Parse("#1B1612"));

    public WorldState? World { get; set; }
    public string Toast { get; set; } = "";
    public double ToastAge { get; set; }
    public double AnimTime { get; set; }
    public event Action<Point, bool, bool, bool, bool, int>? WorldPressed;

    private static readonly Bitmap? Atlas = LoadAtlas();

    private static Bitmap? LoadAtlas()
    {
        var stream = typeof(WorldCanvas).Assembly.GetManifestResourceStream("assets.atlas.png");
        return stream == null ? null : new Bitmap(stream);
    }

    public WorldCanvas()
    {
        Focusable = true;
    }

    public bool TryScreenToGrid(Point screen, out int gx, out int gy)
    {
        gx = 0;
        gy = 0;
        var world = World;
        var self = world?.Self;
        if (world == null || self == null)
            return false;
        (gx, gy) = GridMath.ScreenToGrid(screen.X, screen.Y, self.DrawX, self.DrawY,
            Bounds.Width, Bounds.Height, PlayableTuning.CellPixels);
        return world.Map.InBounds(gx, gy);
    }

    public string? HitTest(Point screen)
    {
        var world = World;
        var self = world?.Self;
        if (world == null || self == null)
            return null;
        string? best = null;
        double bestD = 22;
        foreach (var actor in world.Actors.Values)
        {
            var (sx, sy) = GridMath.GridToScreen(actor.DrawX, actor.DrawY, self.DrawX, self.DrawY,
                Bounds.Width, Bounds.Height, PlayableTuning.CellPixels);
            double dx = sx - screen.X;
            double dy = sy - screen.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            double limit = actor.Kind == EntityKind.EntityLoot ? 16 : 20;
            if (d <= limit && d <= bestD)
            {
                bestD = d;
                best = actor.EntityId;
            }
        }
        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        bool right = props.IsRightButtonPressed;
        if (props.IsLeftButtonPressed || right)
        {
            var mods = e.KeyModifiers;
            WorldPressed?.Invoke(
                point,
                right,
                mods.HasFlag(KeyModifiers.Shift),
                mods.HasFlag(KeyModifiers.Control),
                mods.HasFlag(KeyModifiers.Alt),
                e.ClickCount);
            e.Handled = true;
        }
        base.OnPointerPressed(e);
    }

    public override void Render(DrawingContext context)
    {
        var world = World;
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 1 || h <= 1)
            return;
        context.FillRectangle(Backdrop, new Rect(0, 0, w, h));
        if (world == null)
            return;

        var self = world.Self;
        double camX = self?.DrawX ?? 10;
        double camY = self?.DrawY ?? 10;
        double cell = PlayableTuning.CellPixels;
        DrawTiles(context, world, camX, camY, w, h, cell);
        if (world.HasMoveGoal)
            DrawMarker(context, world.GoalX, world.GoalY, camX, camY, w, h, cell);
        foreach (var actor in world.Actors.Values)
            DrawActor(context, world, actor, camX, camY, w, h, cell);
        foreach (var fx in world.Effects)
            DrawFx(context, fx, camX, camY, w, h, cell);
        foreach (var patch in world.Grounds)
        {
            if (!string.Equals(patch.MapId, world.Map.MapId, StringComparison.OrdinalIgnoreCase))
                continue;
            for (int gy = patch.Y - patch.Radius; gy <= patch.Y + patch.Radius; gy++)
            for (int gx = patch.X - patch.Radius; gx <= patch.X + patch.Radius; gx++)
            {
                var (px, py) = GridMath.GridToScreen(gx, gy, camX, camY, w, h, cell);
                context.FillRectangle(Brush("#66E07020"), new Rect(px, py, cell, cell * 0.45));
            }
        }
        if (IsNight())
            context.FillRectangle(Brush("#33203358"), new Rect(0, 0, w, h));
        foreach (var floater in world.Floats)
            DrawFloat(context, floater, camX, camY, w, h, cell);
        DrawMinimap(context, world, w, h);
        if (!string.IsNullOrEmpty(Toast) && ToastAge < 2.4)
            DrawToast(context, w);
    }

    private static void DrawTiles(DrawingContext context, WorldState world, double camX, double camY, double w, double h, double cell)
    {
        int minX = (int)Math.Floor(camX - (w / 2) / cell) - 1;
        int maxX = (int)Math.Ceiling(camX + (w / 2) / cell) + 1;
        int minY = (int)Math.Floor(camY - (h / 2) / cell) - 1;
        int maxY = (int)Math.Ceiling(camY + (h / 2) / cell) + 1;
        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(world.Map.Width - 1, maxX);
        maxY = Math.Min(world.Map.Height - 1, maxY);
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                bool safe = world.Map.IsSafe(x, y);
                bool alt = ((x + y) & 1) == 0;
                string hex = world.Map.IsBlocked(x, y)
                    ? "#141414"
                    : world.Map.IsPortal(x, y)
                        ? "#1E7A8C"
                        : safe
                            ? (alt ? "#2F7A40" : "#276838")
                            : (alt ? "#3C342C" : "#2A241E");
                var (sx, sy) = GridMath.GridToScreen(x - 0.5, y + 0.5, camX, camY, w, h, cell);
                context.FillRectangle(Brush(hex), new Rect(sx, sy, cell + 0.5, cell + 0.5));
                int hash = (x * 17 + y * 31) & 7;
                if (!world.Map.IsBlocked(x, y) && hash == 0)
                    context.FillRectangle(Brush("#33000000"), new Rect(sx + 6, sy + cell - 10, 4, 3));
                if (world.Map.IsBlocked(x, y))
                    context.DrawLine(new Pen(Brush("#55FFFFFF"), 1), new Point(sx + 2, sy + 4), new Point(sx + cell - 4, sy + 4));
            }
        }
    }

    private static void DrawMarker(DrawingContext context, int gx, int gy, double camX, double camY, double w, double h, double cell)
    {
        var (sx, sy) = GridMath.GridToScreen(gx, gy, camX, camY, w, h, cell);
        context.DrawEllipse(Brush("#663DDC78"), new Pen(Brush("#B6F5C8"), 2), new Point(sx, sy), 10, 10);
    }

    private void DrawActor(DrawingContext context, WorldState world, ActorView actor, double camX, double camY, double w, double h, double cell)
    {
        var (sx, sy) = GridMath.GridToScreen(actor.DrawX, actor.DrawY, camX, camY, w, h, cell);
        var center = new Point(sx, sy);
        var fill = Brush(ColorFor(actor));
        double lift = string.IsNullOrEmpty(actor.MountId) ? 0 : 10;
        if (!string.IsNullOrEmpty(actor.FashionId) && actor.Kind == EntityKind.EntityPlayer)
        {
            var cloak = actor.FashionId.Contains("crimson", StringComparison.OrdinalIgnoreCase)
                ? Brush("#C23B4A")
                : Brush("#C4A46A");
            context.FillRectangle(cloak, new Rect(sx - 14, sy - 6 - lift, 28, 20));
        }
        if (!string.IsNullOrEmpty(actor.MountId) && actor.Kind == EntityKind.EntityPlayer)
        {
            context.DrawEllipse(Brush("#F7F3EA"), null, new Point(sx, sy + 8), 18, 7);
            context.DrawEllipse(Brush("#E4D2A2"), null, new Point(sx + 16, sy), 5, 5);
            context.DrawLine(new Pen(Brush("#8A7040"), 2), new Point(sx - 16, sy + 10), new Point(sx - 20, sy + 16));
            context.DrawLine(new Pen(Brush("#8A7040"), 2), new Point(sx + 8, sy + 10), new Point(sx + 12, sy + 16));
        }
        if (Atlas != null && actor.Kind is EntityKind.EntityPlayer or EntityKind.EntityMonster)
        {
            DrawAtlas(context, actor, sx, sy - lift);
        }
        else
        switch (actor.Kind)
        {
            case EntityKind.EntityLoot:
                DrawDiamond(context, center, fill);
                break;
            case EntityKind.EntityMonster:
                DrawMonsterFrame(context, actor, sx, sy, fill);
                break;
            case EntityKind.EntityNpc:
                context.FillRectangle(Brush("#F2E2B6"), new Rect(sx - 8, sy - 20, 16, 8));
                context.DrawEllipse(fill, null, center, 11, 11);
                break;
            default:
                DrawPaperdoll(context, actor, sx, sy - lift, fill);
                break;
        }

        var (nx, ny) = GridMath.FacingVector(actor.Direction);
        context.DrawLine(new Pen(Brush("#FFF6D8"), 2), center, new Point(sx + nx * 16, sy - ny * 16));

        if (world.SelectedId == actor.EntityId)
            context.DrawEllipse(null, new Pen(Brush("#FFE08A"), 2), center, 16, 16);

        if (actor.Kind != EntityKind.EntityLoot && actor.MaxHp > 0)
        {
            double ratio = Math.Clamp(actor.Hp / (double)actor.MaxHp, 0, 1);
            var bar = new Rect(sx - 14, sy - 28, 28, 4);
            context.FillRectangle(Brush("#2A1A1A"), bar);
            context.FillRectangle(Brush(actor.Hp > 0 ? "#E24B4B" : "#666666"), new Rect(bar.X, bar.Y, 28 * ratio, 4));
        }

        if (actor.Kind == EntityKind.EntityPlayer && !string.IsNullOrWhiteSpace(actor.Title))
        {
            var title = new FormattedText(actor.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, Brush("#E8C872"));
            context.DrawText(title, new Point(sx - title.Width / 2, sy - 58));
        }
        string label = string.IsNullOrWhiteSpace(actor.Name) ? actor.Kind.ToString() : actor.Name;
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 12, Brush(NameHex(actor.NameColor)));
        context.DrawText(text, new Point(sx - text.Width / 2, sy - 44));
    }

    private static void DrawDiamond(DrawingContext context, Point center, IBrush fill)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(center.X, center.Y - 10), true);
            g.LineTo(new Point(center.X + 8, center.Y));
            g.LineTo(new Point(center.X, center.Y + 10));
            g.LineTo(new Point(center.X - 8, center.Y));
            g.EndFigure(true);
        }
        context.DrawGeometry(fill, new Pen(Brush("#FFF1B8"), 1), geo);
    }

    private void DrawAtlas(DrawingContext context, ActorView actor, double sx, double sy)
    {
        if (Atlas == null)
            return;
        int frame = (int)(AnimTime * (actor.Pose == "run" ? 10 : 6)) % 4;
        int col = actor.Hp <= 0 ? 7 : frame switch { 0 => 0, 1 => 1, 2 => 2, _ => 3 };
        if (actor.Pose == "attack") col = 4;
        if (actor.Pose == "cast") col = 5;
        int row = actor.Kind == EntityKind.EntityMonster ? 3
            : string.Equals(actor.Gender, "f", StringComparison.OrdinalIgnoreCase) ? 2
            : actor.Direction is 2 or 6 ? 1 : 0;
        var src = new Rect(col * 32, row * 32, 32, 32);
        var dest = new Rect(sx - 16, sy - 24, 32, 32);
        if (actor.Direction == 6)
        {
            using (context.PushTransform(Matrix.CreateScale(-1, 1)))
                context.DrawImage(Atlas, src, new Rect(-(sx + 16), sy - 24, 32, 32));
        }
        else
            context.DrawImage(Atlas, src, dest);
    }

    private void DrawPaperdoll(DrawingContext context, ActorView actor, double sx, double sy, IBrush fill)
    {
        int frame = (int)(AnimTime * 6) % 4;
        double bob = frame is 1 or 3 ? 2 : 0;
        bool attack = frame == 2 && actor.Pose == "attack";
        double reach = attack ? 8 : 0;
        var (fx, fy) = GridMath.FacingVector(actor.Direction);
        bool female = string.Equals(actor.Gender, "f", StringComparison.OrdinalIgnoreCase);
        double shoulder = female ? 5 : 8;
        context.FillRectangle(Brush(female ? "#6A3058" : "#3A2A22"), new Rect(sx - shoulder, sy - 1 + bob, shoulder * 2, 8));
        context.DrawEllipse(fill, null, new Point(sx, sy + 6 + bob), female ? 6 : 7, 9);
        context.DrawEllipse(Brush("#F4E2C4"), null, new Point(sx, sy - 12 + bob), 6, 6);
        context.DrawLine(new Pen(Brush("#E8C872"), 2),
            new Point(sx, sy),
            new Point(sx + fx * (10 + reach), sy - fy * (10 + reach)));
    }

    private void DrawMonsterFrame(DrawingContext context, ActorView actor, double sx, double sy, IBrush fill)
    {
        int frame = (int)(AnimTime * 4 + actor.GridX) % 3;
        double w = 20 + frame;
        context.FillRectangle(fill, new Rect(sx - w / 2, sy - 10 - frame, w, 18));
        context.FillRectangle(Brush("#2A120E"), new Rect(sx - 4, sy - 16 - frame, 3, 4));
        context.FillRectangle(Brush("#2A120E"), new Rect(sx + 2, sy - 16 - frame, 3, 4));
    }

    private static bool IsNight()
    {
        int hour = DateTime.UtcNow.Hour;
        return hour < 6 || hour >= 18;
    }

    private static string NameHex(string? color) => color switch
    {
        "red" => "#FF5A5A",
        "yellow" => "#F0D060",
        "green" => "#7DDE8A",
        _ => "#FFFFFF"
    };

    private static void DrawFx(DrawingContext context, CastFx fx, double camX, double camY, double w, double h, double cell)
    {
        var (sx, sy) = GridMath.GridToScreen(fx.GridX, fx.GridY, camX, camY, w, h, cell);
        string hex = fx.School switch
        {
            "mage" => "#66F08A3C",
            "taoist" => "#6688E0C8",
            _ => "#66F2E2A0"
        };
        double radius = 8 + fx.Age * 36;
        context.DrawEllipse(null, new Pen(Brush(hex.Length == 9 ? "#" + hex[3..] : hex), 2), new Point(sx, sy), radius, radius * 0.6);
    }

    private static void DrawFloat(DrawingContext context, FloatText floater, double camX, double camY, double w, double h, double cell)
    {
        var (sx, sy) = GridMath.GridToScreen(floater.GridX, floater.GridY, camX, camY, w, h, cell);
        string hex = floater.Kind switch
        {
            FloatKind.Miss => "#C8C8C8",
            FloatKind.Heal => "#7EE0E0",
            FloatKind.Kill => "#E070E8",
            _ => "#FFE36A"
        };
        var text = new FormattedText(floater.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 16,
            Brush(hex));
        context.DrawText(text, new Point(sx - text.Width / 2, sy - 36 - floater.Age * 28));
    }

    private static void DrawMinimap(DrawingContext context, WorldState world, double w, double h)
    {
        const double size = 128;
        double x0 = w - size - 16;
        double y0 = h - size - 16;
        context.FillRectangle(Brush("#CC120E0C"), new Rect(x0 - 4, y0 - 18, size + 8, size + 22));
        var caption = new FormattedText("小地图", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, Brush("#E8C872"));
        context.DrawText(caption, new Point(x0, y0 - 16));
        double scale = size / Math.Max(world.Map.Width, world.Map.Height);
        context.FillRectangle(Brush("#2A241C"), new Rect(x0, y0, world.Map.Width * scale, world.Map.Height * scale));
        foreach (var zone in world.Map.SafeZones)
        {
            double zx = x0 + zone.X1 * scale;
            double zy = y0 + (world.Map.Height - 1 - zone.Y2) * scale;
            double zw = Math.Max(2, (zone.X2 - zone.X1 + 1) * scale);
            double zh = Math.Max(2, (zone.Y2 - zone.Y1 + 1) * scale);
            context.FillRectangle(Brush("#3357A36A"), new Rect(zx, zy, zw, zh));
        }
        foreach (var actor in world.Actors.Values)
        {
            double px = x0 + actor.GridX * scale;
            double py = y0 + (world.Map.Height - 1 - actor.GridY) * scale;
            context.FillRectangle(Brush(actor.IsSelf ? "#FFFFFF" : ColorFor(actor)), new Rect(px - 1.5, py - 1.5, 3, 3));
        }
    }

    private void DrawToast(DrawingContext context, double w)
    {
        var text = new FormattedText(Toast, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 16, Brushes.White);
        double width = Math.Max(160, text.Width + 28);
        var rect = new Rect((w - width) / 2, 72, width, 32);
        context.FillRectangle(Brush("#E0221814"), rect);
        context.DrawText(text, new Point(rect.X + 14, rect.Y + 6));
    }

    private static string ColorFor(ActorView actor)
    {
        if (actor.IsSelf)
            return "#F0D78C";
        return actor.Kind switch
        {
            EntityKind.EntityMonster => "#E25B4A",
            EntityKind.EntityLoot => "#F2C14E",
            EntityKind.EntityNpc => "#5FD0C0",
            EntityKind.EntityPet => "#8BD17C",
            _ => "#7EB6FF"
        };
    }

    private static readonly Dictionary<string, IBrush> Cache = new(StringComparer.Ordinal);

    private static IBrush Brush(string hex)
    {
        if (!Cache.TryGetValue(hex, out var brush))
        {
            brush = new SolidColorBrush(Color.Parse(hex));
            Cache[hex] = brush;
        }
        return brush;
    }
}
