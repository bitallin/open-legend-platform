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

    private const double TileW = 48;
    private const double TileH = 32;
    private static readonly MirSheet? Sheet = MirSheet.TryLoad(typeof(WorldCanvas).Assembly);

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
        (gx, gy) = ScreenToGrid(screen.X, screen.Y, self.DrawX, self.DrawY, Bounds.Width, Bounds.Height);
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
            var (sx, sy) = GridToScreen(actor.DrawX, actor.DrawY, self.DrawX, self.DrawY, Bounds.Width, Bounds.Height);
            double dx = sx - screen.X;
            double dy = (sy - 12) - screen.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            double limit = actor.Kind == EntityKind.EntityLoot ? 18 : 28;
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
        DrawTiles(context, world, camX, camY, w, h);
        if (world.HasMoveGoal)
            DrawMarker(context, world.GoalX, world.GoalY, camX, camY, w, h);
        foreach (var patch in world.Grounds)
        {
            if (!string.Equals(patch.MapId, world.Map.MapId, StringComparison.OrdinalIgnoreCase))
                continue;
            for (int gy = patch.Y - patch.Radius; gy <= patch.Y + patch.Radius; gy++)
            for (int gx = patch.X - patch.Radius; gx <= patch.X + patch.Radius; gx++)
                DrawFxAt(context, "fire", (int)(world.Time * 8) % 4, gx, gy, camX, camY, w, h, 40);
        }
        foreach (var actor in world.Actors.Values.OrderByDescending(a => a.DrawY).ThenBy(a => a.DrawX))
            DrawActor(context, world, actor, camX, camY, w, h);
        foreach (var fx in world.Effects)
            DrawFx(context, fx, camX, camY, w, h);
        if (IsNight())
            context.FillRectangle(Brush("#33203358"), new Rect(0, 0, w, h));
        foreach (var floater in world.Floats)
            DrawFloat(context, floater, camX, camY, w, h);
        DrawMinimap(context, world, w, h);
        if (!string.IsNullOrEmpty(Toast) && ToastAge < 2.4)
            DrawToast(context, w);
    }

    private void DrawTiles(DrawingContext context, WorldState world, double camX, double camY, double w, double h)
    {
        int minX = (int)Math.Floor(camX - (w / 2) / TileW) - 1;
        int maxX = (int)Math.Ceiling(camX + (w / 2) / TileW) + 1;
        int minY = (int)Math.Floor(camY - (h / 2) / TileH) - 1;
        int maxY = (int)Math.Ceiling(camY + (h / 2) / TileH) + 1;
        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(world.Map.Width - 1, maxX);
        maxY = Math.Min(world.Map.Height - 1, maxY);
        for (int y = maxY; y >= minY; y--)
        {
            for (int x = minX; x <= maxX; x++)
            {
                string name = TileName(world, x, y);
                var (sx, sy) = GridToScreen(x - 0.5, y + 0.5, camX, camY, w, h);
                var dest = new Rect(sx, sy, TileW + 0.5, TileH + 0.5);
                if (Sheet != null && Sheet.TryTile(name == "tree" || name == "wall" ? "dirt" : name, out var src))
                    context.DrawImage(Sheet.Bitmap, src, dest);
                else
                    context.FillRectangle(Brush("#3C342C"), dest);
                if ((name == "tree" || name == "wall") && Sheet != null && Sheet.TryTile(name, out var obj))
                    context.DrawImage(Sheet.Bitmap, obj, new Rect(sx + 4, sy - 20, TileW - 8, TileH + 18));
                if (name == "portal")
                    DrawFxAt(context, "portal", (int)(AnimTime * 6) % 4, x, y, camX, camY, w, h, 28);
            }
        }
    }

    private static string TileName(WorldState world, int x, int y)
    {
        if (world.Map.IsBlocked(x, y))
            return ((x * 3 + y) & 1) == 0 ? "wall" : "tree";
        if (world.Map.IsPortal(x, y))
            return "portal";
        if (world.Map.IsSafe(x, y))
            return ((x + y) % 5) == 0 ? "stone" : "safe";
        int n = (x * 13 + y * 7) & 15;
        if (n == 0) return "path";
        if (n == 1) return "dirt";
        return ((x + y) & 1) == 0 ? "grass" : "grass_b";
    }

    private void DrawMarker(DrawingContext context, int gx, int gy, double camX, double camY, double w, double h)
    {
        var (sx, sy) = GridToScreen(gx, gy, camX, camY, w, h);
        double pulse = 4 + (AnimTime * 6 % 1) * 6;
        var pen = new Pen(Brush("#D8FFB0"), 2);
        context.DrawLine(pen, new Point(sx - pulse, sy), new Point(sx + pulse, sy));
        context.DrawLine(pen, new Point(sx, sy - pulse * 0.6), new Point(sx, sy + pulse * 0.6));
    }

    private void DrawActor(DrawingContext context, WorldState world, ActorView actor, double camX, double camY, double w, double h)
    {
        var (sx, sy) = GridToScreen(actor.DrawX, actor.DrawY, camX, camY, w, h);
        if (actor.Kind == EntityKind.EntityLoot)
        {
            DrawLoot(context, sx, sy);
            DrawName(context, actor, sx, sy - 18);
            return;
        }

        bool mounted = !string.IsNullOrEmpty(actor.MountId) && actor.Kind == EntityKind.EntityPlayer;
        if (mounted)
            DrawSprite(context, "horse", actor.Pose is "run" or "walk" ? actor.Pose : "idle", actor.Direction, actor.AnimPhase, actor.PoseLock, sx, sy - 6, 52, 40);
        double lift = mounted ? 16 : 0;
        string sprite = MirSheet.ActorId(actor);
        string action = string.IsNullOrEmpty(actor.Pose) ? "idle" : actor.Pose;
        if (!DrawSprite(context, sprite, action, actor.Direction, actor.AnimPhase, actor.PoseLock, sx, sy - lift, actor.TemplateId.Contains("dragon", StringComparison.OrdinalIgnoreCase) ? 64 : 48, actor.TemplateId.Contains("dragon", StringComparison.OrdinalIgnoreCase) ? 72 : 64))
            context.DrawEllipse(Brush(ColorFor(actor)), null, new Point(sx, sy), 10, 14);

        if (world.SelectedId == actor.EntityId)
            context.DrawEllipse(null, new Pen(Brush("#F0D060"), 2), new Point(sx, sy + 8), 16, 7);
        bool showBar = actor.Kind == EntityKind.EntityMonster || world.SelectedId == actor.EntityId;
        if (showBar && actor.MaxHp > 0)
        {
            double ratio = Math.Clamp(actor.Hp / (double)actor.MaxHp, 0, 1);
            var bar = new Rect(sx - 16, sy - 52 - lift, 32, 4);
            context.FillRectangle(Brush("#1A0A0A"), bar);
            context.FillRectangle(Brush(actor.Hp > 0 ? "#D01818" : "#666666"), new Rect(bar.X, bar.Y, 32 * ratio, 4));
        }
        if (world.Time < actor.BuffUntil && Sheet != null && Sheet.TryFx("buff", (int)(AnimTime * 6) % 4, out var buff))
            context.DrawImage(Sheet.Bitmap, buff, new Rect(sx + 10, sy - 58 - lift, 16, 16));
        DrawName(context, actor, sx, sy - 46 - lift);
    }

    private bool DrawSprite(DrawingContext context, string actor, string action, int direction, double phase, double poseLock, double sx, double sy, double destW, double destH)
    {
        if (Sheet == null)
            return false;
        int frame = MirSheet.FrameIndex(action, phase, poseLock);
        if (!Sheet.TrySprite(actor, action, direction, frame, out var src, out bool flip) &&
            !Sheet.TrySprite(actor, "idle", direction, 0, out src, out flip))
            return false;
        var dest = new Rect(sx - destW / 2, sy - destH + 14, destW, destH);
        if (!flip)
        {
            context.DrawImage(Sheet.Bitmap, src, dest);
            return true;
        }
        using (context.PushTransform(Matrix.CreateScale(-1, 1)))
            context.DrawImage(Sheet.Bitmap, src, new Rect(-(sx + destW / 2), dest.Y, destW, destH));
        return true;
    }

    private void DrawLoot(DrawingContext context, double sx, double sy)
    {
        if (Sheet != null && Sheet.TryTile("path", out var src))
            context.DrawImage(Sheet.Bitmap, src, new Rect(sx - 8, sy - 8, 16, 12));
        context.DrawEllipse(Brush("#F2C14E"), null, new Point(sx, sy - 2), 5, 4);
    }

    private void DrawName(DrawingContext context, ActorView actor, double sx, double sy)
    {
        if (actor.Kind == EntityKind.EntityPlayer && !string.IsNullOrWhiteSpace(actor.Title))
        {
            var title = new FormattedText(actor.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, Brush("#E8C872"));
            context.DrawText(title, new Point(sx - title.Width / 2 + 1, sy - 14 + 1));
            context.DrawText(title, new Point(sx - title.Width / 2, sy - 14));
        }
        string label = string.IsNullOrWhiteSpace(actor.Name) ? actor.Kind.ToString() : actor.Name;
        var shadow = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 13, Brush("#1A100C"));
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 13, Brush(NameHex(actor.NameColor)));
        context.DrawText(shadow, new Point(sx - text.Width / 2 + 1, sy + 1));
        context.DrawText(text, new Point(sx - text.Width / 2, sy));
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

    private void DrawFx(DrawingContext context, CastFx fx, double camX, double camY, double w, double h)
    {
        int frame = Math.Clamp((int)(fx.Age * 8), 0, 3);
        DrawFxAt(context, string.IsNullOrEmpty(fx.Kind) ? "slash" : fx.Kind, frame, fx.GridX, fx.GridY, camX, camY, w, h, 56);
    }

    private void DrawFxAt(DrawingContext context, string name, int frame, double gx, double gy, double camX, double camY, double w, double h, double size)
    {
        var (sx, sy) = GridToScreen(gx, gy, camX, camY, w, h);
        if (Sheet != null && Sheet.TryFx(name, frame, out var src))
        {
            context.DrawImage(Sheet.Bitmap, src, new Rect(sx - size / 2, sy - size / 2, size, size));
            return;
        }
        context.DrawEllipse(null, new Pen(Brush("#F2E2A0"), 2), new Point(sx, sy), 12, 8);
    }

    private static void DrawFloat(DrawingContext context, FloatText floater, double camX, double camY, double w, double h)
    {
        var (sx, sy) = GridToScreen(floater.GridX, floater.GridY, camX, camY, w, h);
        string hex = floater.Kind switch
        {
            FloatKind.Miss => "#C8C8C8",
            FloatKind.Heal => "#7EE0E0",
            FloatKind.Kill => "#E070E8",
            _ => "#FFE36A"
        };
        var text = new FormattedText(floater.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 18,
            Brush(hex));
        var shadow = new FormattedText(floater.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 18, Brush("#1A100C"));
        context.DrawText(shadow, new Point(sx - text.Width / 2 + 1, sy - 40 - floater.Age * 36 + 1));
        context.DrawText(text, new Point(sx - text.Width / 2, sy - 40 - floater.Age * 36));
    }

    private static void DrawMinimap(DrawingContext context, WorldState world, double w, double h)
    {
        const double size = 132;
        double x0 = w - size - 14;
        double y0 = 28;
        context.FillRectangle(Brush("#E8C872"), new Rect(x0 - 4, y0 - 18, size + 8, size + 22));
        context.FillRectangle(Brush("#1A120C"), new Rect(x0 - 2, y0 - 16, size + 4, size + 18));
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
        context.FillRectangle(Brush("#E01A120C"), rect);
        context.DrawRectangle(null, new Pen(Brush("#E8C872"), 1), rect);
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

    private static (double sx, double sy) GridToScreen(double gx, double gy, double camX, double camY, double viewW, double viewH)
    {
        double sx = viewW / 2 + (gx - camX) * TileW;
        double sy = viewH / 2 - (gy - camY) * TileH;
        return (sx, sy);
    }

    private static (int gx, int gy) ScreenToGrid(double sx, double sy, double camX, double camY, double viewW, double viewH)
    {
        double gx = camX + (sx - viewW / 2) / TileW;
        double gy = camY - (sy - viewH / 2) / TileH;
        return ((int)Math.Round(gx), (int)Math.Round(gy));
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
