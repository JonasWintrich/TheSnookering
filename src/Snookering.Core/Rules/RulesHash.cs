namespace Snookering.Core.Rules;

/// <summary>
/// Fingerprints the match state that the physics hash cannot see.
///
/// <see cref="Physics.ShotResult.StateHash"/> covers ball positions and motion
/// only. Two peers could therefore agree perfectly on where every ball lies while
/// disagreeing about whose turn it is, which group each player owns, or the score —
/// a far nastier failure than a visible desync, because nothing looks wrong until
/// the frame ends differently for each player. This closes that gap.
/// </summary>
public static class RulesHash
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private static ulong Add(ulong hash, long value)
    {
        for (var i = 0; i < 8; i++)
        {
            hash ^= (ulong)((value >> (i * 8)) & 0xFF);
            hash *= Prime;
        }
        return hash;
    }

    public static ulong Of(EightBallGame game)
    {
        var h = Add(Offset, game.CurrentPlayer);
        h = Add(h, (long)game.Groups[0]);
        h = Add(h, (long)game.Groups[1]);
        h = Add(h, game.OpenTable ? 1 : 0);
        h = Add(h, game.BreakTaken ? 1 : 0);
        h = Add(h, game.BallInHand ? 1 : 0);
        h = Add(h, game.GameOver ? 1 : 0);
        return Add(h, game.Winner);
    }

    public static ulong Of(SnookerGame game)
    {
        var h = Add(Offset, game.CurrentPlayer);
        h = Add(h, game.Scores[0]);
        h = Add(h, game.Scores[1]);
        h = Add(h, game.ColorsPhase ? 1 : 0);
        h = Add(h, game.ColorBallOn ? 1 : 0);
        h = Add(h, game.NextColorOn);
        h = Add(h, game.BallInHandInD ? 1 : 0);
        h = Add(h, game.FrameOver ? 1 : 0);
        return Add(h, game.Winner);
    }
}
