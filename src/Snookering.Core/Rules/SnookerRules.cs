using System.Collections.Generic;
using System.Linq;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Snookering.Core.Tables;

namespace Snookering.Core.Rules;

/// <summary>Match-logic state for one snooker frame.</summary>
public sealed class SnookerGame
{
    public int CurrentPlayer;
    public int[] Scores = { 0, 0 };

    /// <summary>True once all reds are gone and their final color was resolved: colors in ascending order.</summary>
    public bool ColorsPhase;

    /// <summary>Reds phase only: the shooter potted a red and is now on a color (auto-nominated by first contact).</summary>
    public bool ColorBallOn;

    /// <summary>Colors phase: the color currently on (16 yellow … 21 black).</summary>
    public byte NextColorOn = SnookerBalls.Yellow;

    public bool BallInHandInD;
    public bool FrameOver;
    public int Winner = -1;
}

public sealed record SnookerOutcome
{
    public required bool Legal { get; init; }
    public required int FoulPoints { get; init; }
    public required bool TurnContinues { get; init; }
    public required bool BallInHandInD { get; init; }
    public required bool FrameOver { get; init; }
    public required int Winner { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Standard snooker, v1 scope: reds/colors alternation with the nominated color
/// auto-detected from the first color contacted (lenient casual convention),
/// colors respotted while reds remain, ascending clearance, foul penalties
/// max(4, ball on, ball involved) capped at 7, respot-black tie-break.
/// Deliberately excluded (per plan): the miss rule, free ball, touching ball.
/// </summary>
public sealed class SnookerRules
{
    private readonly TableSpec _table;

    public SnookerRules(TableSpec table) => _table = table;

    public SnookerOutcome Apply(SnookerGame game, TableState before, ShotResult result)
    {
        var facts = ShotFacts.Extract(result.Events);
        var shooter = game.CurrentPlayer;
        var opponent = 1 - shooter;
        game.BallInHandInD = false;

        var onReds = !game.ColorsPhase && !game.ColorBallOn;
        var ballOnValue = game.ColorsPhase ? SnookerBalls.Value(game.NextColorOn) : onReds ? 1 : 4;

        // Auto-nomination: in a color-after-red situation the first color contacted is the nominated ball.
        byte nominated = 0;
        if (game.ColorBallOn && facts.FirstContact is { } fc && SnookerBalls.IsColor(fc))
            nominated = fc;

        // ---- foul detection & penalty -----------------------------------------
        var foulPoints = 0;
        void Foul(params int[] values)
        {
            var v = values.Concat(new[] { 4, foulPoints }).Max();
            foulPoints = System.Math.Min(v, 7);
        }

        if (facts.FirstContact is null)
            Foul(ballOnValue);
        else
        {
            var first = facts.FirstContact.Value;
            if (onReds && !SnookerBalls.IsRed(first))
                Foul(SnookerBalls.Value(first));
            else if (game.ColorBallOn && SnookerBalls.IsRed(first))
                Foul(ballOnValue);
            else if (game.ColorsPhase && first != game.NextColorOn)
                Foul(SnookerBalls.Value(first), ballOnValue);
        }

        if (facts.CuePotted)
            Foul(ballOnValue, facts.FirstContact is { } f2 ? SnookerBalls.Value(f2) : 0);

        // Pot legality + scoring.
        var score = 0;
        foreach (var id in facts.Potted)
        {
            if (onReds && SnookerBalls.IsRed(id))
                score += 1;
            else if (game.ColorBallOn && id == nominated && nominated != 0 && facts.Potted.Count(p => p == id) == 1
                     && facts.Potted.All(p => p == nominated))
                score += SnookerBalls.Value(id);
            else if (game.ColorsPhase && id == game.NextColorOn && facts.Potted.Count == 1)
                score += SnookerBalls.Value(id);
            else
                Foul(SnookerBalls.Value(id), ballOnValue);
        }
        if (foulPoints > 0)
            score = 0;

        // ---- respots -----------------------------------------------------------
        // Colors return to their spots until the colors phase; in the colors phase a
        // legally potted on-color stays down, anything else (foul pots) respots.
        var spots = _table.Snooker!;
        foreach (var id in facts.Potted.Where(SnookerBalls.IsColor).Distinct())
        {
            var legallyDown = game.ColorsPhase && foulPoints == 0 && id == game.NextColorOn;
            if (!legallyDown)
                Respot(result.FinalState, id, spots);
        }

        // ---- state transitions ---------------------------------------------------
        var redsLeft = result.FinalState.Balls.Count(b => b.OnTable && SnookerBalls.IsRed(b.Id));
        var legalPot = foulPoints == 0 && score > 0;

        string msg;
        if (foulPoints > 0)
        {
            game.Scores[opponent] += foulPoints;
            game.CurrentPlayer = opponent;
            game.ColorBallOn = false;
            game.BallInHandInD = facts.CuePotted;
            msg = $"FOUL — {foulPoints} points to Player {opponent + 1}" + (facts.CuePotted ? " (cue ball in hand in the D)" : "");
        }
        else if (legalPot)
        {
            game.Scores[shooter] += score;
            if (onReds)
            {
                game.ColorBallOn = true;
                msg = $"+{score} — Player {shooter + 1} on a color";
            }
            else if (game.ColorBallOn)
            {
                game.ColorBallOn = false;
                msg = $"+{score} — Player {shooter + 1} continues";
            }
            else // colors phase
            {
                if (game.NextColorOn == SnookerBalls.Black)
                    return EndFrame(game, result.FinalState, spots);
                game.NextColorOn++;
                msg = $"+{score} — Player {shooter + 1} on the {ColorName(game.NextColorOn)}";
            }
        }
        else
        {
            game.CurrentPlayer = opponent;
            game.ColorBallOn = false;
            msg = $"Player {opponent + 1} to play";
        }

        // Colors phase begins once no reds remain and no color-after-red is pending.
        if (!game.ColorsPhase && redsLeft == 0 && !game.ColorBallOn)
        {
            game.ColorsPhase = true;
            var lowest = LowestColorOnTable(result.FinalState);
            game.NextColorOn = lowest;
        }

        return new SnookerOutcome
        {
            Legal = foulPoints == 0,
            FoulPoints = foulPoints,
            TurnContinues = legalPot,
            BallInHandInD = game.BallInHandInD,
            FrameOver = false,
            Winner = -1,
            Message = msg,
        };
    }

    private SnookerOutcome EndFrame(SnookerGame game, TableState final, SnookerSpots spots)
    {
        // The black's 7 points were already added by the scoring loop.
        if (game.Scores[0] == game.Scores[1])
        {
            // Respot-black tie-break: black returns, incoming player from the D.
            Respot(final, SnookerBalls.Black, spots);
            game.CurrentPlayer = 1 - game.CurrentPlayer;
            game.BallInHandInD = true;
            return new SnookerOutcome
            {
                Legal = true,
                FoulPoints = 0,
                TurnContinues = false,
                BallInHandInD = true,
                FrameOver = false,
                Winner = -1,
                Message = "Tie! Black respotted — next score wins",
            };
        }

        game.FrameOver = true;
        game.Winner = game.Scores[0] > game.Scores[1] ? 0 : 1;
        return new SnookerOutcome
        {
            Legal = true,
            FoulPoints = 0,
            TurnContinues = false,
            BallInHandInD = false,
            FrameOver = true,
            Winner = game.Winner,
            Message = $"Frame over — Player {game.Winner + 1} wins {game.Scores[game.Winner]}:{game.Scores[1 - game.Winner]}",
        };
    }

    private static byte LowestColorOnTable(TableState s)
    {
        for (var id = SnookerBalls.Yellow; id <= SnookerBalls.Black; id++)
            if (s.Balls.Any(b => b.Id == id && b.OnTable))
                return id;
        return SnookerBalls.Black;
    }

    /// <summary>Own spot → highest free spot → nudged toward the top cushion.</summary>
    private void Respot(TableState state, byte colorId, SnookerSpots spots)
    {
        var r = _table.Physics.R;

        bool Free(Vec2 pos) => !state.Balls.Any(b =>
            b.OnTable && b.Id != colorId && (b.Pos - pos).Length < 2.0 * r + 1e-9);

        var candidates = new List<Vec2> { spots.SpotOf(colorId) };
        for (var id = SnookerBalls.Black; id >= SnookerBalls.Yellow; id--)
            candidates.Add(spots.SpotOf(id));

        var target = candidates.FirstOrDefault(Free, default);
        if (target == default && !Free(target))
        {
            // All spots blocked: walk from own spot toward the top cushion.
            target = spots.SpotOf(colorId);
            while (!Free(target) && target.X < _table.HalfLength - r)
                target = new Vec2(target.X + 2.0 * r + 1e-6, target.Y);
        }

        ref var ball = ref state.Ball(colorId);
        ball = BallState.AtRest(colorId, target);
    }

    public static string ColorName(byte id) => id switch
    {
        SnookerBalls.Yellow => "yellow",
        SnookerBalls.Green => "green",
        SnookerBalls.Brown => "brown",
        SnookerBalls.Blue => "blue",
        SnookerBalls.Pink => "pink",
        SnookerBalls.Black => "black",
        _ => "red",
    };
}
