using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Legend.Contracts.Proto;
using Legend.Playable.Data;
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

    private static readonly MirSheet? Sheet = MirSheet.TryLoad(typeof(WorldCanvas).Assembly);

    public string WeaponLayer { get; set; } = "";
    public string ArmorLayer { get; set; } = "";
    public string HelmetLayer { get; set; } = "";
    public bool FashionOn { get; set; }

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
        (gx, gy) = IsoProjection.ToCell(screen.X, screen.Y, self.DrawX, self.DrawY, Bounds.Width, Bounds.Height);
        return world.Map.InBounds(gx, gy);
    }

    public string? HitTest(Point screen)
    {
        var world = World;
        var self = world?.Self;
        if (world == null || self == null)
            return null;
        string? best = null;
        double bestD = 36;
        foreach (var actor in world.Actors.Values)
        {
            var (sx, sy) = IsoProjection.ToScreen(actor.DrawX, actor.DrawY, self.DrawX, self.DrawY, Bounds.Width, Bounds.Height);
            double dx = sx - screen.X;
            double dy = (sy - 28) - screen.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            double limit = actor.Kind == EntityKind.EntityLoot ? 18 : 42;
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
        DrawSortedWorld(context, world, camX, camY, w, h);
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
        int minX = world.Map.Width, maxX = 0, minY = world.Map.Height, maxY = 0;
        void Grow(double sx, double sy)
        {
            var (gx, gy) = IsoProjection.ToGrid(sx, sy, camX, camY, w, h);
            minX = Math.Min(minX, (int)Math.Floor(gx) - 2);
            maxX = Math.Max(maxX, (int)Math.Ceiling(gx) + 2);
            minY = Math.Min(minY, (int)Math.Floor(gy) - 2);
            maxY = Math.Max(maxY, (int)Math.Ceiling(gy) + 2);
        }
        Grow(0, 0);
        Grow(w, 0);
        Grow(0, h);
        Grow(w, h);
        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(world.Map.Width - 1, maxX);
        maxY = Math.Min(world.Map.Height - 1, maxY);
        var cells = new List<(int x, int y)>();
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
            cells.Add((x, y));
        cells.Sort((a, b) => (b.x + b.y).CompareTo(a.x + a.y));
        foreach (var (x, y) in cells)
        {
            string name = TileName(world, x, y);
            var (sx, sy) = IsoProjection.ToScreen(x, y, camX, camY, w, h);
            string ground = name is "tree" or "wall" ? "dirt" : name;
            // Iso MirSheet diamonds only — pack cinematic map art must not replace ground tiles.
            DrawDiamond(context, sx, sy, ground);
            // Tall props (tree/wall) are drawn in DrawSortedWorld with actors for height occlusion.
            if (name == "portal")
                DrawFxAt(context, "portal", (int)(AnimTime * 8) % Math.Max(1, Sheet?.FxFrames ?? 4), x, y, camX, camY, w, h, 52);
        }
    }

    private void DrawDiamond(DrawingContext context, double sx, double sy, string tile)
    {
        double hw = IsoProjection.TileW / 2;
        double hh = IsoProjection.TileH / 2;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(sx, sy - hh), true);
            g.LineTo(new Point(sx + hw, sy));
            g.LineTo(new Point(sx, sy + hh));
            g.LineTo(new Point(sx - hw, sy));
            g.EndFigure(true);
        }
        if (Sheet != null && Sheet.TryTile(tile, out var src))
        {
            using (context.PushGeometryClip(geo))
                context.DrawImage(Sheet.Bitmap, src, new Rect(sx - hw, sy - hh, IsoProjection.TileW, IsoProjection.TileH));
        }
        else
            context.DrawGeometry(Brush("#3C342C"), null, geo);
        context.DrawGeometry(null, new Pen(Brush("#22000000"), 1), geo);
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


    private void DrawSortedWorld(DrawingContext context, WorldState world, double camX, double camY, double w, double h)
    {
        // Depth key = DrawX+DrawY (iso). Tall props interleave with actors so canopy covers people behind.
        var items = new List<(double depth, int tie, Action draw)>();
        int tie = 0;
        int minX = world.Map.Width, maxX = 0, minY = world.Map.Height, maxY = 0;
        void Grow(double sx, double sy)
        {
            var (gx, gy) = IsoProjection.ToGrid(sx, sy, camX, camY, w, h);
            minX = Math.Min(minX, (int)Math.Floor(gx) - 3);
            maxX = Math.Max(maxX, (int)Math.Ceiling(gx) + 3);
            minY = Math.Min(minY, (int)Math.Floor(gy) - 3);
            maxY = Math.Max(maxY, (int)Math.Ceiling(gy) + 3);
        }
        Grow(0, 0); Grow(w, 0); Grow(0, h); Grow(w, h);
        minX = Math.Max(0, minX);
        minY = Math.Max(0, minY);
        maxX = Math.Min(world.Map.Width - 1, maxX);
        maxY = Math.Min(world.Map.Height - 1, maxY);

        foreach (var actor in world.Actors.Values)
        {
            var a = actor;
            double depth = a.DrawX + a.DrawY;
            double opacity = ActorOcclusionOpacity(world, a);
            items.Add((depth, tie++, () => DrawActor(context, world, a, camX, camY, w, h, opacity)));
        }
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            string name = TileName(world, x, y);
            if (name is not ("tree" or "wall"))
                continue;
            int px = x, py = y;
            string prop = name;
            double depth = px + py + 0.15;
            items.Add((depth, tie++, () => DrawTallProp(context, prop, px, py, camX, camY, w, h)));
        }
        foreach (var item in items.OrderByDescending(i => i.depth).ThenBy(i => i.tie))
            item.draw();
    }

    private double ActorOcclusionOpacity(WorldState world, ActorView actor)
    {
        int ax = (int)Math.Round(actor.DrawX);
        int ay = (int)Math.Round(actor.DrawY);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int tx = ax + dx, ty = ay + dy;
            if (tx < 0 || ty < 0 || tx >= world.Map.Width || ty >= world.Map.Height)
                continue;
            string name = TileName(world, tx, ty);
            if (name is not ("tree" or "wall"))
                continue;
            double propDepth = tx + ty + 0.15;
            double actorDepth = actor.DrawX + actor.DrawY;
            if (actorDepth >= propDepth - 0.05 && Math.Abs(actor.DrawX - tx) < 1.15 && Math.Abs(actor.DrawY - ty) < 1.15)
                return 0.45;
        }
        return 1.0;
    }

    private void DrawTallProp(DrawingContext context, string name, int x, int y, double camX, double camY, double w, double h)
    {
        if (Sheet == null || !Sheet.TryTile(name, out var obj))
            return;
        var (sx, sy) = IsoProjection.ToScreen(x, y, camX, camY, w, h);
        double dw = name == "tree" ? 60 : 56;
        double dh = name == "tree" ? 72 : 64;
        context.DrawImage(Sheet.Bitmap, obj, new Rect(sx - dw / 2, sy - dh + 6, dw, dh));
    }

    private void DrawMarker(DrawingContext context, int gx, int gy, double camX, double camY, double w, double h)
    {
        var (sx, sy) = IsoProjection.ToScreen(gx, gy, camX, camY, w, h);
        double hw = IsoProjection.TileW / 2 - 4;
        double hh = IsoProjection.TileH / 2 - 3;
        var pen = new Pen(Brush("#D8FFB0"), 2);
        context.DrawLine(pen, new Point(sx, sy - hh), new Point(sx + hw, sy));
        context.DrawLine(pen, new Point(sx + hw, sy), new Point(sx, sy + hh));
        context.DrawLine(pen, new Point(sx, sy + hh), new Point(sx - hw, sy));
        context.DrawLine(pen, new Point(sx - hw, sy), new Point(sx, sy - hh));
    }

    private void DrawActor(DrawingContext context, WorldState world, ActorView actor, double camX, double camY, double w, double h, double opacity = 1.0)
    {
        var (sx, sy) = IsoProjection.ToScreen(actor.DrawX, actor.DrawY, camX, camY, w, h);
        if (actor.Kind == EntityKind.EntityLoot)
        {
            DrawLoot(context, sx, sy, actor.TemplateId);
            DrawName(context, actor, sx, sy - 18);
            return;
        }

        bool mounted = !string.IsNullOrEmpty(actor.MountId) && actor.Kind == EntityKind.EntityPlayer;
        if (mounted)
            DrawSprite(context, "horse", actor.Pose is "run" or "walk" ? actor.Pose : "idle", actor.Direction, actor.AnimPhase, actor.PoseLock, sx, sy - 6, 52, 40);
        double lift = mounted ? 18 : 0;
        string sprite = MirSheet.ActorId(actor);
        string action = string.IsNullOrEmpty(actor.Pose) ? "idle" : actor.Pose;
        bool isDragon = actor.TemplateId.Contains("dragon", StringComparison.OrdinalIgnoreCase);
        bool isPlayerLike = actor.Kind is EntityKind.EntityPlayer or EntityKind.EntityNpc;
        double dw = isDragon ? 88 : isPlayerLike ? 72 : 64;
        double dh = isDragon ? 96 : isPlayerLike ? 96 : 84;
        // Soft oval shadow under feet so sprites sit in the world instead of floating cards.
        context.DrawEllipse(Brush("#66000000"), null, new Point(sx, sy + 2), dw * 0.28, 7);
        // Prefer animated MirSheet (idle/walk/attack/hit). Pack "portrait" sprites are icons only —
        // drawing them as world entities made square cards stand on the ground with no combat poses.
        using (context.PushOpacity(Math.Clamp(opacity, 0.25, 1.0)))
        {
        bool drew = DrawSprite(context, sprite, action, actor.Direction, actor.AnimPhase, actor.PoseLock, sx, sy - lift, dw, dh);
        if (!drew && actor.Kind == EntityKind.EntityMonster)
        {
            var ov = PackAssetOverrides.Current?.MonsterSprite(actor.TemplateId);
            if (ov != null && LooksLikeWorldSprite(ov))
            {
                context.DrawImage(ov, new Rect(sx - dw / 2, sy - dh + 14 - lift, dw, dh));
                drew = true;
            }
        }
        if (!drew)
            context.DrawEllipse(Brush(ColorFor(actor)), null, new Point(sx, sy), 10, 14);
        // Paperdoll: self always; other players show weapon/armor when layers known (self-driven SyncGear today).
        if (actor.IsSelf && actor.Kind == EntityKind.EntityPlayer)
            DrawGear(context, action, actor.Direction, actor.AnimPhase, actor.PoseLock, sx, sy - lift, dw, dh);
        } // opacity
        if (string.Equals(action, "hit", StringComparison.OrdinalIgnoreCase))
            context.FillRectangle(Brush("#55FFFFFF"), new Rect(sx - dw / 2, sy - dh + 14 - lift, dw, dh));

        if (world.SelectedId == actor.EntityId)
            context.DrawEllipse(null, new Pen(Brush("#F0D060"), 2), new Point(sx, sy + 6), Math.Max(18, dw * 0.32), 8);
        bool showBar = actor.Kind == EntityKind.EntityMonster || world.SelectedId == actor.EntityId;
        if (showBar && actor.MaxHp > 0)
        {
            double ratio = Math.Clamp(actor.Hp / (double)actor.MaxHp, 0, 1);
            double barW = Math.Max(36, dw * 0.55);
            var bar = new Rect(sx - barW / 2, sy - dh + 4 - lift, barW, 5);
            context.FillRectangle(Brush("#1A0A0A"), bar);
            context.FillRectangle(Brush(actor.Hp > 0 ? "#D01818" : "#666666"), new Rect(bar.X, bar.Y, barW * ratio, 5));
        }
        if (world.Time < actor.BuffUntil && Sheet != null && Sheet.TryFx("buff", (int)(AnimTime * 6) % 4, out var buff))
            context.DrawImage(Sheet.Bitmap, buff, new Rect(sx + dw * 0.28, sy - dh + 2 - lift, 22, 22));
        DrawName(context, actor, sx, sy - dh + 10 - lift);
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

    private void DrawGear(DrawingContext context, string action, int direction, double phase, double poseLock, double sx, double sy, double dw, double dh)
    {
        string armor = FashionOn ? "arm_fashion" : ArmorLayer;
        if (string.IsNullOrEmpty(armor))
            armor = "arm_cloth"; // always show at least a clothes layer for visible paperdoll
        DrawSprite(context, armor, action, direction, phase, poseLock, sx, sy, dw, dh);
        if (!FashionOn && !string.IsNullOrEmpty(HelmetLayer))
            DrawSprite(context, HelmetLayer, action, direction, phase, poseLock, sx, sy, dw, dh);
        string weapon = string.IsNullOrEmpty(WeaponLayer) ? "wep_sword" : WeaponLayer;
        DrawSprite(context, weapon, action, direction, phase, poseLock, sx, sy, dw, dh);
    }

    private void DrawLoot(DrawingContext context, double sx, double sy, string? templateId = null)
    {
        var ov = PackAssetOverrides.Current?.ItemDrop(templateId) ?? PackAssetOverrides.Current?.ItemIcon(templateId);
        if (ov != null)
        {
            context.DrawImage(ov, new Rect(sx - 10, sy - 14, 20, 20));
            return;
        }
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
        "red" => "#FF1744",
        "yellow" => "#FDD835",
        "gray" or "grey" => "#9E9E9E",
        "green" => "#7DDE8A",
        _ => "#FFFFFF"
    };

    private void DrawFx(DrawingContext context, CastFx fx, double camX, double camY, double w, double h)
    {
        int frames = Math.Max(1, Sheet?.FxFrames ?? 4);
        int frame = Math.Clamp((int)(fx.Age * 12), 0, frames - 1);
        double size = string.Equals(fx.Kind, "slash", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(fx.Kind) ? 72 : 64;
        DrawFxAt(context, string.IsNullOrEmpty(fx.Kind) ? "slash" : fx.Kind, frame, fx.GridX, fx.GridY, camX, camY, w, h, size);
    }

    private void DrawFxAt(DrawingContext context, string name, int frame, double gx, double gy, double camX, double camY, double w, double h, double size)
    {
        var (sx, sy) = IsoProjection.ToScreen(gx, gy, camX, camY, w, h);
        if (Sheet != null && Sheet.TryFx(name, frame, out var src))
        {
            context.DrawImage(Sheet.Bitmap, src, new Rect(sx - size / 2, sy - size / 2, size, size));
            return;
        }
        context.DrawEllipse(null, new Pen(Brush("#F2E2A0"), 2), new Point(sx, sy), 12, 8);
    }

    private static void DrawFloat(DrawingContext context, FloatText floater, double camX, double camY, double w, double h)
    {
        var (sx, sy) = IsoProjection.ToScreen(floater.GridX, floater.GridY, camX, camY, w, h);
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
        // Compact radar only — never stretch cinematic city/minimap art into this slot.
        const double size = 128;
        double x0 = w - size - 18;
        double y0 = 36;
        context.FillRectangle(Brush("#CC120E0A"), new Rect(x0 - 6, y0 - 20, size + 12, size + 28));
        context.DrawRectangle(null, new Pen(Brush("#E8C872"), 1.5), new Rect(x0 - 6, y0 - 20, size + 12, size + 28));
        string title = string.IsNullOrWhiteSpace(world.Map.MapName) ? "小地图" : world.Map.MapName;
        if (title.Length > 10) title = title[..10];
        var caption = new FormattedText(title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, Brush("#E8C872"));
        context.DrawText(caption, new Point(x0, y0 - 16));
        double scale = size / Math.Max(1, Math.Max(world.Map.Width, world.Map.Height));
        context.FillRectangle(Brush("#1A1610"), new Rect(x0, y0, world.Map.Width * scale, world.Map.Height * scale));
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

    /// <summary>Pack icons/portraits are not combat sprites; only accept cut-out RGBA sheets.</summary>
    private static bool LooksLikeWorldSprite(Bitmap bmp)
    {
        try
        {
            var size = bmp.PixelSize;
            if (size.Width < 24 || size.Height < 24 || size.Width > 160 || size.Height > 160)
                return false;
            // Avalonia: Bgra8888/Rgba8888 imply alpha channel suitable for cut-outs.
            var fmt = bmp.Format;
            return fmt == PixelFormats.Bgra8888 || fmt == PixelFormats.Rgba8888;
        }
        catch { return false; }
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
