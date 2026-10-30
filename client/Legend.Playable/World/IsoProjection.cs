namespace Legend.Playable.World;

/// <summary>
/// Classic diamond projection. Logical grid coordinates stay the same as the server.
/// East is up-right, north is up-left. Tile is 64×32.
/// </summary>
public static class IsoProjection
{
    public const double TileW = 64;
    public const double TileH = 32;

    public static (double sx, double sy) ToScreen(double gx, double gy, double camX, double camY, double viewW, double viewH)
    {
        double dx = gx - camX;
        double dy = gy - camY;
        double sx = viewW / 2 + (dx - dy) * (TileW / 2);
        double sy = viewH / 2 - (dx + dy) * (TileH / 2);
        return (sx, sy);
    }

    public static (double gx, double gy) ToGrid(double sx, double sy, double camX, double camY, double viewW, double viewH)
    {
        double a = (sx - viewW / 2) / (TileW / 2);
        double b = -(sy - viewH / 2) / (TileH / 2);
        double dx = (a + b) / 2.0;
        double dy = (b - a) / 2.0;
        return (camX + dx, camY + dy);
    }

    public static (int gx, int gy) ToCell(double sx, double sy, double camX, double camY, double viewW, double viewH)
    {
        var (gx, gy) = ToGrid(sx, sy, camX, camY, viewW, viewH);
        return ((int)Math.Round(gx), (int)Math.Round(gy));
    }
}
