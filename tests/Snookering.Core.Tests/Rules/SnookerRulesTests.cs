using System.Linq;
using Snookering.Core.Physics;
using Snookering.Core.Rules;
using Snookering.Core.Tables;
using Xunit;

namespace Snookering.Core.Tests.Rules;

public class SnookerRulesTests
{
    private static readonly TableSpec Table = TableSpec.Snooker12ft();
    private static readonly SnookerRules Rules = new(Table);

    private static TableState Full() => Racks.Snooker(Table);

    private static TableState After(params byte[] offTable)
    {
        var s = Racks.Snooker(Table);
        foreach (var id in offTable)
            s.Ball(id).OnTable = false;
        return s;
    }

    [Fact]
    public void PottingRed_ScoresOne_AndPutsShooterOnAColor()
    {
        var game = new SnookerGame();
        var shot = new SyntheticShot().Contact(1).Pot(1).Result(After(1));
        var outcome = Rules.Apply(game, Full(), shot);

        Assert.True(outcome.Legal);
        Assert.Equal(1, game.Scores[0]);
        Assert.True(game.ColorBallOn);
        Assert.True(outcome.TurnContinues);
    }

    [Fact]
    public void PottingNominatedColor_Scores_AndRespotsIt()
    {
        var game = new SnookerGame { ColorBallOn = true };
        var final = After(1, SnookerBalls.Black); // a red already down; black potted
        var shot = new SyntheticShot().Contact(SnookerBalls.Black).Pot(SnookerBalls.Black).Result(final);
        var outcome = Rules.Apply(game, After(1), shot);

        Assert.True(outcome.Legal);
        Assert.Equal(7, game.Scores[0]);
        Assert.False(game.ColorBallOn);
        // Black must be back on the table (reds remain).
        Assert.True(final.Ball(SnookerBalls.Black).OnTable);
        Assert.Equal(Table.Snooker!.Black, final.Ball(SnookerBalls.Black).Pos);
    }

    [Fact]
    public void HittingColorFirst_WhenOnReds_IsFoul_MinFourOrBallValue()
    {
        var game = new SnookerGame();
        var shot = new SyntheticShot().Contact(SnookerBalls.Blue).Result(Full());
        var outcome = Rules.Apply(game, Full(), shot);

        Assert.False(outcome.Legal);
        Assert.Equal(5, outcome.FoulPoints); // blue involved → 5
        Assert.Equal(5, game.Scores[1]);
        Assert.Equal(1, game.CurrentPlayer);
    }

    [Fact]
    public void Scratch_IsFoul_BallInHandInD()
    {
        var game = new SnookerGame();
        var shot = new SyntheticShot().Contact(1).Pot(0).Result(After(0));
        var outcome = Rules.Apply(game, Full(), shot);

        Assert.False(outcome.Legal);
        Assert.Equal(4, outcome.FoulPoints);
        Assert.True(outcome.BallInHandInD);
    }

    [Fact]
    public void PottingColor_WhenOnReds_IsFoul_AndColorRespots()
    {
        var game = new SnookerGame();
        var final = After(SnookerBalls.Pink);
        var shot = new SyntheticShot().Contact(1).Pot(SnookerBalls.Pink).Result(final);
        var outcome = Rules.Apply(game, Full(), shot);

        Assert.False(outcome.Legal);
        Assert.Equal(6, outcome.FoulPoints);
        Assert.True(final.Ball(SnookerBalls.Pink).OnTable);
    }

    [Fact]
    public void MissingEverything_IsFoulFour()
    {
        var game = new SnookerGame();
        var outcome = Rules.Apply(game, Full(), new SyntheticShot().Rail().Result(Full()));
        Assert.Equal(4, outcome.FoulPoints);
    }

    [Fact]
    public void AfterLastRedAndItsColor_ColorsPhaseBegins_AtYellow()
    {
        // Only one red left; shooter pots it, then pots a color; colors phase starts.
        var game = new SnookerGame();
        var onlyRedLeft = After(2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
        var afterRed = After(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
        Rules.Apply(game, onlyRedLeft, new SyntheticShot().Contact(1).Pot(1).Result(afterRed));
        Assert.True(game.ColorBallOn);
        Assert.False(game.ColorsPhase);

        var afterColor = After(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, SnookerBalls.Green);
        var outcome = Rules.Apply(game, afterRed,
            new SyntheticShot().Contact(SnookerBalls.Green).Pot(SnookerBalls.Green).Result(afterColor));

        Assert.True(outcome.Legal);
        Assert.Equal(1 + 3, game.Scores[0]);
        Assert.True(game.ColorsPhase);
        Assert.Equal(SnookerBalls.Yellow, game.NextColorOn);
        Assert.True(afterColor.Ball(SnookerBalls.Green).OnTable); // respotted: reds phase logic still applied
    }

    [Fact]
    public void ColorsPhase_MustPotAscending_LegalPotStaysDown()
    {
        var noReds = After(Enumerable.Range(1, 15).Select(i => (byte)i).ToArray());
        var game = new SnookerGame { ColorsPhase = true, NextColorOn = SnookerBalls.Yellow };

        var afterYellow = After(Enumerable.Range(1, 15).Select(i => (byte)i).Append(SnookerBalls.Yellow).ToArray());
        var outcome = Rules.Apply(game, noReds,
            new SyntheticShot().Contact(SnookerBalls.Yellow).Pot(SnookerBalls.Yellow).Result(afterYellow));

        Assert.True(outcome.Legal);
        Assert.Equal(2, game.Scores[0]);
        Assert.Equal(SnookerBalls.Green, game.NextColorOn);
        Assert.False(afterYellow.Ball(SnookerBalls.Yellow).OnTable); // stays down in colors phase
    }

    [Fact]
    public void ColorsPhase_WrongColorFirst_IsFoul()
    {
        var noReds = After(Enumerable.Range(1, 15).Select(i => (byte)i).ToArray());
        var game = new SnookerGame { ColorsPhase = true, NextColorOn = SnookerBalls.Yellow };
        var outcome = Rules.Apply(game, noReds,
            new SyntheticShot().Contact(SnookerBalls.Black).Result(noReds));

        Assert.False(outcome.Legal);
        Assert.Equal(7, outcome.FoulPoints); // black involved
    }

    [Fact]
    public void PottingBlack_EndsFrame_HigherScoreWins()
    {
        var beforeIds = Enumerable.Range(1, 15).Select(i => (byte)i)
            .Concat(new[] { SnookerBalls.Yellow, SnookerBalls.Green, SnookerBalls.Brown, SnookerBalls.Blue, SnookerBalls.Pink })
            .ToArray();
        var before = After(beforeIds);
        var final = After(beforeIds.Append(SnookerBalls.Black).ToArray());
        var game = new SnookerGame { ColorsPhase = true, NextColorOn = SnookerBalls.Black };
        game.Scores[0] = 40;
        game.Scores[1] = 20;

        var outcome = Rules.Apply(game, before,
            new SyntheticShot().Contact(SnookerBalls.Black).Pot(SnookerBalls.Black).Result(final));

        Assert.True(outcome.FrameOver);
        Assert.Equal(0, outcome.Winner);
        Assert.Equal(47, game.Scores[0]);
    }

    [Fact]
    public void PottingBlack_WithTiedScores_RespotsBlack()
    {
        var beforeIds = Enumerable.Range(1, 15).Select(i => (byte)i)
            .Concat(new[] { SnookerBalls.Yellow, SnookerBalls.Green, SnookerBalls.Brown, SnookerBalls.Blue, SnookerBalls.Pink })
            .ToArray();
        var before = After(beforeIds);
        var final = After(beforeIds.Append(SnookerBalls.Black).ToArray());
        var game = new SnookerGame { ColorsPhase = true, NextColorOn = SnookerBalls.Black, CurrentPlayer = 0 };
        game.Scores[0] = 30;
        game.Scores[1] = 37; // potting black (+7) ties it

        var outcome = Rules.Apply(game, before,
            new SyntheticShot().Contact(SnookerBalls.Black).Pot(SnookerBalls.Black).Result(final));

        Assert.False(outcome.FrameOver);
        Assert.True(final.Ball(SnookerBalls.Black).OnTable);
        Assert.True(outcome.BallInHandInD);
        Assert.Equal(1, game.CurrentPlayer);
    }

    [Fact]
    public void RespotOnOccupiedSpot_FallsBackCorrectly()
    {
        // Park a red on the pink spot. Every color still sits on its own spot, so
        // all spots are occupied → the rule walks the pink toward the top cushion.
        var game = new SnookerGame { ColorBallOn = true };
        var final = After(SnookerBalls.Pink);
        final.Ball(1).Pos = Table.Snooker!.Pink;
        var shot = new SyntheticShot().Contact(SnookerBalls.Pink).Pot(SnookerBalls.Pink).Result(final);
        Rules.Apply(game, Full(), shot);

        var pink = final.Ball(SnookerBalls.Pink);
        Assert.True(pink.OnTable);
        Assert.True(pink.Pos.X > Table.Snooker!.Pink.X, "pink must move toward the top cushion");
        Assert.Equal(0.0, pink.Pos.Y, 9);

        // And with the black off the table, a blocked pink takes the highest FREE spot.
        var game2 = new SnookerGame { ColorBallOn = true };
        var final2 = After(SnookerBalls.Pink, SnookerBalls.Black);
        final2.Ball(2).Pos = Table.Snooker!.Pink; // block pink's own spot
        var shot2 = new SyntheticShot().Contact(SnookerBalls.Pink).Pot(SnookerBalls.Pink).Result(final2);
        Rules.Apply(game2, After(SnookerBalls.Black), shot2);

        Assert.Equal(Table.Snooker!.Black, final2.Ball(SnookerBalls.Pink).Pos);
    }
}
