namespace Legend.Playable.World;

public static class GridMath
{
    public static int Chebyshev(int x1, int y1, int x2, int y2)
        => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

    /// <summary>Grid +Y is north (up on screen). 0=N … 7=NW, matching the Unity MVP.</summary>
    public static int DirFromDelta(int dx, int dy)
    {
        if (dx == 0 && dy > 0) return 0;
        if (dx > 0 && dy > 0) return 1;
        if (dx > 0 && dy == 0) return 2;
        if (dx > 0 && dy < 0) return 3;
        if (dx == 0 && dy < 0) return 4;
        if (dx < 0 && dy < 0) return 5;
        if (dx < 0 && dy == 0) return 6;
        if (dx < 0 && dy > 0) return 7;
        return 4;
    }

    public static (double nx, double ny) FacingVector(int dir) => dir switch
    {
        0 => (0, 1),
        1 => (0.707, 0.707),
        2 => (1, 0),
        3 => (0.707, -0.707),
        4 => (0, -1),
        5 => (-0.707, -0.707),
        6 => (-1, 0),
        7 => (-0.707, 0.707),
        _ => (0, -1)
    };

    /// <summary>
    /// One step toward the goal. Diagonal when both axes differ.
    /// If that cell is blocked, slide along an open axis. (0,0) means no legal step.
    /// </summary>
    public static (int dx, int dy) NextStep(int sx, int sy, int tx, int ty, Func<int, int, bool> blocked)
    {
        int dx = Math.Sign(tx - sx);
        int dy = Math.Sign(ty - sy);
        if (dx == 0 && dy == 0)
            return (0, 0);
        if (!blocked(sx + dx, sy + dy))
            return (dx, dy);
        if (Math.Abs(tx - sx) >= Math.Abs(ty - sy))
        {
            if (dx != 0 && !blocked(sx + dx, sy))
                return (dx, 0);
            if (dy != 0 && !blocked(sx, sy + dy))
                return (0, dy);
        }
        else
        {
            if (dy != 0 && !blocked(sx, sy + dy))
                return (0, dy);
            if (dx != 0 && !blocked(sx + dx, sy))
                return (dx, 0);
        }
        return (0, 0);
    }

    public static (double sx, double sy) GridToScreen(
        double gx, double gy, double camX, double camY, double viewW, double viewH, double cell)
    {
        double sx = viewW / 2 + (gx - camX) * cell;
        double sy = viewH / 2 - (gy - camY) * cell;
        return (sx, sy);
    }

    public static (int gx, int gy) ScreenToGrid(
        double sx, double sy, double camX, double camY, double viewW, double viewH, double cell)
    {
        if (cell <= 0)
            return (0, 0);
        double gx = camX + (sx - viewW / 2) / cell;
        double gy = camY - (sy - viewH / 2) / cell;
        return ((int)Math.Round(gx), (int)Math.Round(gy));
    }
}
