using System.Collections.Generic;
using System.Linq;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Snookering.Core.Rules;
using Xunit;

namespace Snookering.Core.Tests.Rules;

public class EightBallRulesTests
{
    private static readonly EightBallRules Rules = new();

    // ---- synthetic shot construction: no physics involved -------------------

    private static TableState StateWith(params byte[] onTable)
    {
        var balls = new List<BallState>();
        for (byte id = 0; id <= 15; id++)
        {
            var b = BallState.AtRest(id, new Vec2(0.1 * id - 0.8, 0.0));
            b.OnTable = onTable.Contains(id);
            balls.Add(b);
        }
        return new TableState(balls.ToArray());
    }

    private static byte[] AllBalls => Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();

    private sealed class ShotBuilder
    {
        private readonly List<SimEvent> _events = new() { new SimEvent(0.0, SimEventType.CueStrike, 0, 0, -1, 2.0) };
        private double _t = 0.1;

        public ShotBuilder Contact(byte ball, byte by = 0)
        {
            _events.Add(new SimEvent(_t += 0.1, SimEventType.BallBall, by, ball, -1, 1.0));
            return this;
        }

        public ShotBuilder Rail(byte ball = 0)
        {
            _events.Add(new SimEvent(_t += 0.1, SimEventType.Cushion, ball, ball, 100, 1.0));
            return this;
        }

        public ShotBuilder Pot(byte ball)
        {
            _events.Add(new SimEvent(_t += 0.1, SimEventType.Pocketed, ball, ball, 0, 1.0));
            return this;
        }

        public ShotResult Result(TableState final)
        {
            _events.Add(new SimEvent(_t += 0.1, SimEventType.RestReached, 0, 0, -1, 0.0));
            return new ShotResult
            {
                FinalState = final,
                Events = _events,
                Frames = new List<TrajectoryFrame>(),
                StateHash = 0,
                Duration = _t,
            };
        }
    }

    private static EightBallGame MidGame(int player = 0, BallGroup group = BallGroup.Solids)
    {
        var g = new EightBallGame { CurrentPlayer = player, OpenTable = false, BreakTaken = true };
        g.Groups[player] = group;
        g.Groups[1 - player] = group == BallGroup.Solids ? BallGroup.Stripes : BallGroup.Solids;
        return g;
    }

    // ---- fouls ---------------------------------------------------------------

    [Fact]
    public void Scratch_IsFoul_BallInHand_TurnPasses()
    {
        var game = MidGame();
        var shot = new ShotBuilder().Contact(1).Rail().Pot(0).Result(StateWith(AllBalls.Where(b => b != 0).ToArray()));
        var outcome = Rules.Apply(game, StateWith(AllBalls), shot);

        Assert.Equal(FoulReason.Scratch, outcome.Foul);
        Assert.True(outcome.BallInHand);
        Assert.Equal(1, game.CurrentPlayer);
        Assert.False(outcome.GameOver);
    }

    [Fact]
    public void NoContact_IsFoul()
    {
        var game = MidGame();
        var shot = new ShotBuilder().Rail().Result(StateWith(AllBalls));
        var outcome = Rules.Apply(game, StateWith(AllBalls), shot);

        Assert.Equal(FoulReason.NoContact, outcome.Foul);
        Assert.Equal(1, game.CurrentPlayer);
    }

    [Fact]
    public void WrongBallFirst_StripeWhenOnSolids_IsFoul()
    {
        var game = MidGame(group: BallGroup.Solids);
        var shot = new ShotBuilder().Contact(9).Rail().Result(StateWith(AllBalls));
        var outcome = Rules.Apply(game, StateWith(AllBalls), shot);

        Assert.Equal(FoulReason.WrongBallFirst, outcome.Foul);
    }

    [Fact]
    public void EightFirst_WithBallsRemaining_IsFoul()
    {
        var game = MidGame(group: BallGroup.Solids);
        var shot = new ShotBuilder().Contact(8).Rail().Result(StateWith(AllBalls));
        Assert.Equal(FoulReason.WrongBallFirst, Rules.Apply(game, StateWith(AllBalls), shot).Foul);
    }

    [Fact]
    public void NoPotNoRail_IsFoul_ButRailSavesIt()
    {
        var foulShot = new ShotBuilder().Contact(1).Result(StateWith(AllBalls));
        Assert.Equal(FoulReason.NoRailAfterContact, Rules.Apply(MidGame(), StateWith(AllBalls), foulShot).Foul);

        var legalShot = new ShotBuilder().Contact(1).Rail(1).Result(StateWith(AllBalls));
        Assert.Equal(FoulReason.None, Rules.Apply(MidGame(), StateWith(AllBalls), legalShot).Foul);
    }

    // ---- turn flow & groups ----------------------------------------------------

    [Fact]
    public void LegalPotOfOwnGroup_ContinuesTurn()
    {
        var game = MidGame(group: BallGroup.Solids);
        var shot = new ShotBuilder().Contact(1).Pot(1).Result(StateWith(AllBalls.Where(b => b != 1).ToArray()));
        var outcome = Rules.Apply(game, StateWith(AllBalls), shot);

        Assert.True(outcome.Legal);
        Assert.True(outcome.TurnContinues);
        Assert.Equal(0, game.CurrentPlayer);
    }

    [Fact]
    public void OpenTable_FirstLegalPot_AssignsGroups()
    {
        var game = new EightBallGame { CurrentPlayer = 1, OpenTable = true, BreakTaken = true };
        var shot = new ShotBuilder().Contact(12).Pot(12).Result(StateWith(AllBalls.Where(b => b != 12).ToArray()));
        var outcome = Rules.Apply(game, StateWith(AllBalls), shot);

        Assert.True(outcome.Legal);
        Assert.Equal(BallGroup.Stripes, game.Groups[1]);
        Assert.Equal(BallGroup.Solids, game.Groups[0]);
        Assert.False(game.OpenTable);
        Assert.True(outcome.TurnContinues);
    }

    [Fact]
    public void BreakPot_DoesNotAssignGroups_ButContinuesTurn()
    {
        var game = new EightBallGame(); // break not yet taken
        var shot = new ShotBuilder().Contact(1).Pot(3).Result(StateWith(AllBalls.Where(b => b != 3).ToArray()));
        var outcome = Rules.Apply(game, StateWith(AllBalls), shot);

        Assert.True(outcome.Legal);
        Assert.True(game.OpenTable);
        Assert.Equal(BallGroup.None, game.Groups[0]);
        Assert.True(outcome.TurnContinues);
    }

    [Fact]
    public void LegalNoPot_PassesTurn_NoBallInHand()
    {
        var game = MidGame();
        var shot = new ShotBuilder().Contact(1).Rail(1).Result(StateWith(AllBalls));
        var outcome = Rules.Apply(game, StateWith(AllBalls), shot);

        Assert.True(outcome.Legal);
        Assert.False(outcome.TurnContinues);
        Assert.False(outcome.BallInHand);
        Assert.Equal(1, game.CurrentPlayer);
    }

    // ---- the 8 ball -------------------------------------------------------------

    [Fact]
    public void EarlyEight_LosesGame()
    {
        var game = MidGame(player: 0, group: BallGroup.Solids); // solids still on table
        var shot = new ShotBuilder().Contact(1).Pot(8).Result(StateWith(AllBalls.Where(b => b != 8).ToArray()));
        var outcome = Rules.Apply(game, StateWith(AllBalls), shot);

        Assert.True(outcome.GameOver);
        Assert.Equal(1, outcome.Winner);
    }

    [Fact]
    public void LegalEight_AfterClearingGroup_WinsGame()
    {
        var game = MidGame(player: 0, group: BallGroup.Solids);
        var before = StateWith(0, 8, 9, 10, 11, 12, 13, 14, 15); // all solids already down
        var shot = new ShotBuilder().Contact(8).Pot(8).Result(StateWith(0, 9, 10, 11, 12, 13, 14, 15));
        var outcome = Rules.Apply(game, before, shot);

        Assert.True(outcome.GameOver);
        Assert.Equal(0, outcome.Winner);
    }

    [Fact]
    public void EightWithScratch_AfterClearing_LosesGame()
    {
        var game = MidGame(player: 0, group: BallGroup.Solids);
        var before = StateWith(0, 8, 9, 10, 11, 12, 13, 14, 15);
        var shot = new ShotBuilder().Contact(8).Pot(8).Pot(0).Result(StateWith(9, 10, 11, 12, 13, 14, 15));
        var outcome = Rules.Apply(game, before, shot);

        Assert.True(outcome.GameOver);
        Assert.Equal(1, outcome.Winner);
    }

    [Fact]
    public void EightOnBreak_WinsGame()
    {
        var game = new EightBallGame();
        var shot = new ShotBuilder().Contact(1).Pot(8).Result(StateWith(AllBalls.Where(b => b != 8).ToArray()));
        var outcome = Rules.Apply(game, StateWith(AllBalls), shot);

        Assert.True(outcome.GameOver);
        Assert.Equal(0, outcome.Winner);
    }

    [Fact]
    public void EightOnBreak_WithScratch_LosesGame()
    {
        var game = new EightBallGame();
        var shot = new ShotBuilder().Contact(1).Pot(8).Pot(0).Result(StateWith(AllBalls.Where(b => b != 8 && b != 0).ToArray()));
        var outcome = Rules.Apply(game, StateWith(AllBalls), shot);

        Assert.True(outcome.GameOver);
        Assert.Equal(1, outcome.Winner);
    }
}
