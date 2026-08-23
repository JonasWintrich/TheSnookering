using System;
using System.Linq;
using Snookering.Core.Ai;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Snookering.Core.Rules;
using Snookering.Core.Tables;
using Xunit;

namespace Snookering.Core.Tests.Ai;

public class ShotPlannerTests
{
    private static readonly TableSpec Table = TableSpec.Pool9ft();

    [Fact]
    public void StraightShot_IsFound_AndPots()
    {
        // Ball 1 dead in line with the top-right corner pocket; cue straight behind.
        var pocket = Table.Pockets.First(p => p.FallCenter.X > 0 && p.FallCenter.Y > 0);
        var diag = new Vec2(0.7071067811865476, 0.7071067811865476);
        var target = pocket.FallCenter - diag * 0.45;
        var cue = pocket.FallCenter - diag * 1.35;

        var state = new TableState(new[]
        {
            BallState.AtRest(0, cue),
            BallState.AtRest(1, target),
        });

        var shot = ShotPlanner.Plan(state, Table, new byte[] { 1 }, AiDifficulty.Hard, seed: 7);
        var result = Simulator.Run(state, shot.Input, Table);
        var facts = ShotFacts.Extract(result.Events);

        Assert.Contains((byte)1, facts.Potted);
        Assert.False(facts.CuePotted);
    }

    [Fact]
    public void Placement_InD_StaysInsideD()
    {
        var snooker = TableSpec.Snooker12ft();
        var state = Racks.Snooker(snooker);
        state.Ball(0).OnTable = false;

        var pos = ShotPlanner.PlanPlacement(state, snooker,
            SnookerRules.LegalTargets(new SnookerGame(), state), restrictToD: true);

        var d = snooker.Snooker!;
        Assert.True(pos.X <= d.BaulkX + 1e-9, "placement must stay behind the baulk line");
        Assert.True((pos - d.DCenter).Length <= d.DRadiusValue + 1e-9, "placement must stay inside the D");
    }

    [Fact]
    public void TwoAis_FinishAnEightBallRack()
    {
        var table = Table;
        var state = Racks.EightBall(table);
        var game = new EightBallGame();
        var rules = new EightBallRules();
        var rng = new DeterministicRng(42);

        var shots = 0;
        while (!game.GameOver && shots < 150)
        {
            shots++;

            if (game.BallInHand)
            {
                ref var cue = ref state.Ball(0);
                var targets = EightBallRules.LegalTargets(game, state);
                cue = BallState.AtRest(0, ShotPlanner.PlanPlacement(state, table, targets, restrictToD: false));
                game.BallInHand = false;
            }

            ShotInput input;
            if (!game.BreakTaken)
            {
                var cuePos = state.Ball(0).Pos;
                var dir = (Racks.FootSpot(table) - cuePos).Normalized();
                input = new ShotInput
                {
                    AimAngleMicroRad = (int)Math.Round(Math.Atan2(dir.Y, dir.X) * 1e6),
                    SpeedMmPerSec = 6800,
                    OffsetSide1e4 = 0,
                    OffsetVert1e4 = 0,
                    ElevationCentiDeg = 0,
                };
            }
            else
            {
                var targets = EightBallRules.LegalTargets(game, state);
                input = ShotPlanner.Plan(state, table, targets, AiDifficulty.Medium, rng.NextU64()).Input;
            }

            var result = Simulator.Run(state, input, table);
            var before = state;
            state = result.FinalState;
            rules.Apply(game, before, result);

            // A pocketed cue ball must come back before the next shot (ball in hand).
            if (!state.Ball(0).OnTable && !game.GameOver)
            {
                Assert.True(game.BallInHand, "cue off table must imply ball in hand");
            }
        }

        Assert.True(game.GameOver, $"AI vs AI did not finish the rack in {shots} shots");
        Assert.InRange(game.Winner, 0, 1);
    }
}
