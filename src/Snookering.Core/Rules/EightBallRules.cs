using System.Linq;
using Snookering.Core.Physics;

namespace Snookering.Core.Rules;

public enum BallGroup : byte { None, Solids, Stripes }

public enum FoulReason : byte
{
    None,
    Scratch,
    NoContact,
    WrongBallFirst,
    NoRailAfterContact,
}

/// <summary>Match-logic state for one 8-ball rack (the physics state lives in TableState).</summary>
public sealed class EightBallGame
{
    public int CurrentPlayer;                       // 0 or 1
    public BallGroup[] Groups = { BallGroup.None, BallGroup.None };
    public bool OpenTable = true;
    public bool BreakTaken;
    public bool BallInHand;                          // pending for the CURRENT player's shot
    public bool GameOver;
    public int Winner = -1;

    public BallGroup GroupOf(int player) => Groups[player];
}

/// <summary>What one shot meant for the match. Consumed by UI and (later) AI.</summary>
public sealed record ShotOutcome
{
    public required bool Legal { get; init; }
    public required FoulReason Foul { get; init; }
    public required bool TurnContinues { get; init; }
    public required bool BallInHand { get; init; }
    public required bool GameOver { get; init; }
    public required int Winner { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Recreational/bar 8-ball, call-nothing (matching the Steam reference):
/// open table until the first legal pot assigns groups; fouls (scratch, no contact,
/// wrong ball first, no rail after contact) give the opponent ball-in-hand anywhere;
/// the 8 wins the game after your group is cleared, loses it early or with a foul;
/// 8 on the break wins (8 + scratch on break loses).
/// </summary>
public sealed class EightBallRules
{
    private static bool IsSolid(byte id) => id >= 1 && id <= 7;
    private static bool IsStripe(byte id) => id >= 9 && id <= 15;

    /// <summary>Balls the current player may legally strike first / pot (consumed by UI and AI).</summary>
    public static System.Collections.Generic.List<byte> LegalTargets(EightBallGame game, TableState state)
    {
        var targets = new System.Collections.Generic.List<byte>();
        var group = game.GroupOf(game.CurrentPlayer);

        foreach (var b in state.Balls)
        {
            if (!b.OnTable || b.Id == 0 || b.Id == 8)
                continue;
            if (game.OpenTable || InGroup(b.Id, group))
                targets.Add(b.Id);
        }

        if (targets.Count == 0 && !game.OpenTable)
            targets.Add(8); // group cleared: the 8 is the ball on
        return targets;
    }

    private static bool InGroup(byte id, BallGroup g) =>
        g == BallGroup.Solids ? IsSolid(id) : g == BallGroup.Stripes && IsStripe(id);

    private static int GroupCountOnTable(TableState s, BallGroup g)
    {
        var n = 0;
        foreach (var b in s.Balls)
            if (b.OnTable && InGroup(b.Id, g))
                n++;
        return n;
    }

    /// <summary>Adjudicate one shot. before = table state the shot started from.</summary>
    public ShotOutcome Apply(EightBallGame game, TableState before, ShotResult result)
    {
        var facts = ShotFacts.Extract(result.Events);
        var shooter = game.CurrentPlayer;
        var opponent = 1 - shooter;
        var isBreak = !game.BreakTaken;
        game.BreakTaken = true;
        game.BallInHand = false;

        var group = game.GroupOf(shooter);
        var clearedBefore = group != BallGroup.None && GroupCountOnTable(before, group) == 0;
        var eightPotted = facts.Potted.Contains((byte)8);

        // ---- foul detection ----
        var foul = FoulReason.None;
        if (facts.CuePotted)
            foul = FoulReason.Scratch;
        else if (facts.FirstContact is null)
            foul = FoulReason.NoContact;
        else if (!isBreak && WrongFirstContact(facts.FirstContact.Value, game, group, clearedBefore))
            foul = FoulReason.WrongBallFirst;
        else if (facts.Potted.Count == 0 && !facts.CuePotted && !facts.RailAfterContact)
            foul = FoulReason.NoRailAfterContact;

        // ---- the 8 ball decides games ----
        if (eightPotted)
        {
            bool shooterWins;
            if (isBreak)
                shooterWins = !facts.CuePotted;          // 8 on the break wins, unless scratched
            else if (!clearedBefore)
                shooterWins = false;                     // early 8 = loss
            else
                shooterWins = foul == FoulReason.None;   // legal clearing shot wins; foul loses

            game.GameOver = true;
            game.Winner = shooterWins ? shooter : opponent;
            return Finish(game, foul, turnContinues: false, shooterWins
                ? $"Player {game.Winner + 1} wins!"
                : $"8-ball {(isBreak ? "with a scratch on the break" : !clearedBefore ? "potted early" : "potted with a foul")} — Player {game.Winner + 1} wins!");
        }

        // ---- group assignment (first legal pot after the break) ----
        if (game.OpenTable && !isBreak && foul == FoulReason.None && facts.Potted.Count > 0)
        {
            var first = facts.Potted[0];
            var assigned = IsSolid(first) ? BallGroup.Solids : BallGroup.Stripes;
            game.Groups[shooter] = assigned;
            game.Groups[opponent] = assigned == BallGroup.Solids ? BallGroup.Stripes : BallGroup.Solids;
            game.OpenTable = false;
            group = assigned;
        }

        // ---- turn continuation ----
        var pottedOwn = facts.Potted.Any(id =>
            game.OpenTable || isBreak ? id != 8 : InGroup(id, group));
        var continues = foul == FoulReason.None && pottedOwn;

        if (!continues)
            game.CurrentPlayer = opponent;
        game.BallInHand = foul != FoulReason.None;

        var msg = foul != FoulReason.None
            ? $"FOUL: {Describe(foul)} — Player {game.CurrentPlayer + 1} has ball in hand"
            : continues
                ? $"Player {shooter + 1} continues"
                : $"Player {game.CurrentPlayer + 1} to play";
        if (game.OpenTable && game.BreakTaken && !game.GameOver)
            msg += " (open table)";

        return Finish(game, foul, continues, msg);
    }

    private static bool WrongFirstContact(byte first, EightBallGame game, BallGroup group, bool clearedBefore)
    {
        if (game.OpenTable)
            return first == 8;      // on an open table anything but the 8 may be struck first
        if (clearedBefore)
            return first != 8;      // group cleared: the 8 is the only legal first contact
        return !InGroup(first, group);
    }

    private static string Describe(FoulReason foul) => foul switch
    {
        FoulReason.Scratch => "scratch",
        FoulReason.NoContact => "no ball contacted",
        FoulReason.WrongBallFirst => "wrong ball struck first",
        FoulReason.NoRailAfterContact => "no rail after contact",
        _ => "?",
    };

    private static ShotOutcome Finish(EightBallGame game, FoulReason foul, bool turnContinues, string message) => new()
    {
        Legal = foul == FoulReason.None,
        Foul = foul,
        TurnContinues = turnContinues && !game.GameOver,
        BallInHand = game.BallInHand,
        GameOver = game.GameOver,
        Winner = game.Winner,
        Message = message,
    };
}
