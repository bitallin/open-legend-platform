namespace Legend.ClientCore;

/// <summary>
/// Narrow client move-prediction port. Stores grid cells only; isometric projection stays in Playable.
/// </summary>
public interface IMovePrediction
{
    bool HasPending { get; }
    int AnchorX { get; }
    int AnchorY { get; }
    int PredictedX { get; }
    int PredictedY { get; }

    /// <summary>Begin a local prediction from the current server-known cell to a target cell.</summary>
    void Begin(int fromX, int fromY, int toX, int toY);

    /// <summary>
    /// Resolve against a server MoveSync. Accepted keeps the server cell; rejected rolls back to
    /// the server cell (typically FromX/FromY). Clears pending either way.
    /// </summary>
    MovePredictionResult Resolve(bool accepted, int serverX, int serverY);

    void Clear();
}

public readonly record struct MovePredictionResult(int GridX, int GridY, bool RolledBack, bool HadPending);

/// <summary>Default grid-only move prediction used by Playable.</summary>
public sealed class MovePrediction : IMovePrediction
{
    public bool HasPending { get; private set; }
    public int AnchorX { get; private set; }
    public int AnchorY { get; private set; }
    public int PredictedX { get; private set; }
    public int PredictedY { get; private set; }

    public void Begin(int fromX, int fromY, int toX, int toY)
    {
        AnchorX = fromX;
        AnchorY = fromY;
        PredictedX = toX;
        PredictedY = toY;
        HasPending = true;
    }

    public MovePredictionResult Resolve(bool accepted, int serverX, int serverY)
    {
        bool had = HasPending;
        HasPending = false;
        if (accepted)
            return new MovePredictionResult(serverX, serverY, RolledBack: false, HadPending: had);

        return new MovePredictionResult(serverX, serverY, RolledBack: true, HadPending: had);
    }

    public void Clear()
    {
        HasPending = false;
    }
}
