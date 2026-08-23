using System;
using System.Collections.Generic;
using System.Linq;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Snookering.Core.Tables;

namespace Snookering.Core.Ai;

public enum AiDifficulty { Easy, Medium, Hard }

public sealed record AiShot(ShotInput Input, string Description);

/// <summary>
/// Shot selection: enumerate (legal target × pocket) candidates via ghost-ball
/// aiming, reject blocked or too-thin cuts, score by cut angle and distances,
/// then execute with difficulty-scaled Gaussian noise. Hard additionally runs
/// Monte-Carlo rollouts through the real deterministic simulator for its top
/// candidates and picks the highest observed pot rate.
///
/// Trig is allowed here (AI layer, not the sim core) — the output is a quantized
/// ShotInput, which is what replays and multiplayer exchange anyway.
/// </summary>
public static class ShotPlanner
{
    private sealed record Candidate(
        byte TargetId,
        short PocketId,
        double AimAngle,
        double Speed,
        double Score,
        double CutCos);

    private sealed record Profile(double AngleSigmaRad, double SpeedSigma, int McCandidates, int McRollouts);

    private static Profile For(AiDifficulty d) => d switch
    {
        AiDifficulty.Easy => new Profile(0.020, 0.09, 0, 0),
        AiDifficulty.Medium => new Profile(0.009, 0.045, 0, 0),
        _ => new Profile(0.004, 0.02, 3, 3),
    };

    // ------------------------------------------------------------------ public API

    public static AiShot Plan(
        TableState state, TableSpec table, IReadOnlyList<byte> legalTargets,
        AiDifficulty difficulty, ulong seed)
    {
        var rng = new DeterministicRng(seed);
        var profile = For(difficulty);
        var candidates = Candidates(state, table, legalTargets)
            .OrderByDescending(c => c.Score)
            .ToList();

        if (candidates.Count == 0)
            return Safety(state, table, legalTargets, ref rng, profile);

        var chosen = candidates[0];
        if (profile.McCandidates > 0 && candidates.Count > 1)
            chosen = MonteCarlo(state, table, legalTargets, candidates.Take(profile.McCandidates).ToList(),
                profile, ref rng);

        var input = Execute(chosen.AimAngle, chosen.Speed, profile, ref rng, seed);
        return new AiShot(input, $"pot ball {chosen.TargetId} → pocket {chosen.PocketId}");
    }

    /// <summary>Choose a ball-in-hand position: the spot giving the best straight-ish candidate.</summary>
    public static Vec2 PlanPlacement(
        TableState state, TableSpec table, IReadOnlyList<byte> legalTargets, bool restrictToD)
    {
        var r = table.Physics.R;
        var best = FallbackPlacement(table, restrictToD);
        var bestScore = double.NegativeInfinity;

        foreach (var targetId in legalTargets)
        {
            var target = Find(state, targetId);
            if (target is null)
                continue;

            foreach (var pocket in table.Pockets)
            {
                var d = (pocket.FallCenter - target.Value.Pos).Normalized();
                if (d == Vec2.Zero)
                    continue;
                // Straight line: cue behind the ghost ball, at a comfortable distance.
                var ghost = target.Value.Pos - d * (2.0 * r);
                foreach (var back in new[] { 0.28, 0.45 })
                {
                    var pos = ghost - d * back;
                    if (!InBounds(pos, table, r) || Occupied(state, pos, r))
                        continue;
                    if (restrictToD && !InsideD(pos, table))
                        continue;
                    if (Blocked(state, pos, ghost, r, targetId) || Blocked(state, target.Value.Pos, pocket.FallCenter, r, targetId))
                        continue;

                    var score = 1.0 / (0.3 + back) + PocketBonus(pocket, table);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = pos;
                    }
                }
            }
        }

        return best;
    }

    // ------------------------------------------------------------------ candidates

    private static List<Candidate> Candidates(TableState state, TableSpec table, IReadOnlyList<byte> targets)
    {
        var r = table.Physics.R;
        var cue = Find(state, 0);
        var result = new List<Candidate>();
        if (cue is null)
            return result;

        foreach (var targetId in targets)
        {
            var target = Find(state, targetId);
            if (target is null)
                continue;

            foreach (var pocket in table.Pockets)
            {
                var toPocket = pocket.FallCenter - target.Value.Pos;
                var pocketDist = toPocket.Length;
                if (pocketDist < 1e-6)
                    continue;
                var d = toPocket / pocketDist;

                var ghost = target.Value.Pos - d * (2.0 * r);
                var toGhost = ghost - cue.Value.Pos;
                var cueDist = toGhost.Length;
                if (cueDist < 4.0 * r)
                    continue; // touching/behind: no clean stroke
                var a = toGhost / cueDist;

                var cutCos = a.Dot(d);
                if (cutCos < 0.25)
                    continue; // > ~75° cut: not a realistic pot

                if (Blocked(state, cue.Value.Pos, ghost, r, targetId) ||
                    Blocked(state, target.Value.Pos, pocket.FallCenter, r, targetId))
                    continue;

                var speed = Math.Clamp(1.1 + 1.3 * (cueDist + pocketDist / Math.Max(cutCos, 0.35)), 1.2, 5.8);
                var score = cutCos * cutCos / (0.6 + cueDist + pocketDist) + PocketBonus(pocket, table);
                result.Add(new Candidate(targetId, pocket.Id, Math.Atan2(a.Y, a.X), speed, score, cutCos));
            }
        }

        return result;
    }

    private static double PocketBonus(Pocket pocket, TableSpec table) =>
        Math.Abs(pocket.FallCenter.X) < 0.2 ? 0.0 : 0.01; // corners are slightly friendlier than side pockets

    /// <summary>Any OTHER ball's center within 2R of the segment blocks the path.</summary>
    private static bool Blocked(TableState state, Vec2 from, Vec2 to, double r, byte ignoreId)
    {
        var seg = to - from;
        var lenSq = seg.LengthSquared;
        if (lenSq < 1e-12)
            return false;

        foreach (var b in state.Balls)
        {
            if (!b.OnTable || b.Id == 0 || b.Id == ignoreId)
                continue;
            var t = Math.Clamp((b.Pos - from).Dot(seg) / lenSq, 0.0, 1.0);
            var closest = from + seg * t;
            if ((b.Pos - closest).Length < 2.0 * r - 0.002)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ Monte-Carlo (Hard)

    private static Candidate MonteCarlo(
        TableState state, TableSpec table, IReadOnlyList<byte> legalTargets,
        List<Candidate> top, Profile profile, ref DeterministicRng rng)
    {
        var best = top[0];
        var bestRate = -1.0;

        foreach (var cand in top)
        {
            var pots = 0;
            for (var k = 0; k < profile.McRollouts; k++)
            {
                var input = Execute(cand.AimAngle, cand.Speed, profile, ref rng, rng.NextU64());
                var result = Simulator.Run(state, input, table);
                var facts = Rules.ShotFacts.Extract(result.Events);
                if (!facts.CuePotted && facts.Potted.Contains(cand.TargetId))
                    pots++;
            }

            var rate = (double)pots / profile.McRollouts;
            if (rate > bestRate || (rate == bestRate && cand.Score > best.Score))
            {
                bestRate = rate;
                best = cand;
            }
        }

        return best;
    }

    // ------------------------------------------------------------------ safety fallback

    private static AiShot Safety(
        TableState state, TableSpec table, IReadOnlyList<byte> legalTargets,
        ref DeterministicRng rng, Profile profile)
    {
        var cue = Find(state, 0);
        var angle = 0.0;
        byte nearest = 0;

        if (cue is not null)
        {
            var bestDist = double.PositiveInfinity;
            foreach (var id in legalTargets)
            {
                var b = Find(state, id);
                if (b is null)
                    continue;
                var dist = (b.Value.Pos - cue.Value.Pos).Length;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    nearest = id;
                    var dir = (b.Value.Pos - cue.Value.Pos).Normalized();
                    angle = Math.Atan2(dir.Y, dir.X);
                }
            }
        }

        var input = Execute(angle, 1.6, profile, ref rng, rng.NextU64());
        return new AiShot(input, $"safety: roll onto ball {nearest}");
    }

    // ------------------------------------------------------------------ execution with noise

    private static ShotInput Execute(double aimAngle, double speed, Profile profile, ref DeterministicRng rng, ulong seed)
    {
        var angle = aimAngle + rng.NextGaussian() * profile.AngleSigmaRad;
        var v = Math.Clamp(speed * (1.0 + rng.NextGaussian() * profile.SpeedSigma), 0.5, 7.0);

        return new ShotInput
        {
            AimAngleMicroRad = (int)Math.Round(angle * 1e6),
            SpeedMmPerSec = (int)Math.Round(v * 1e3),
            OffsetSide1e4 = 0,
            OffsetVert1e4 = 0,
            ElevationCentiDeg = 0,
            Seed = seed,
        };
    }

    // ------------------------------------------------------------------ helpers

    private static BallState? Find(TableState state, byte id)
    {
        foreach (var b in state.Balls)
            if (b.Id == id && b.OnTable)
                return b;
        return null;
    }

    private static bool InBounds(Vec2 pos, TableSpec table, double r) =>
        Math.Abs(pos.X) <= table.HalfLength - r && Math.Abs(pos.Y) <= table.HalfWidth - r;

    private static bool Occupied(TableState state, Vec2 pos, double r)
    {
        foreach (var b in state.Balls)
            if (b.OnTable && b.Id != 0 && (b.Pos - pos).Length < 2.0 * r + 1e-6)
                return true;
        return false;
    }

    private static bool InsideD(Vec2 pos, TableSpec table)
    {
        var d = table.Snooker;
        if (d is null)
            return true;
        return pos.X <= d.BaulkX + 1e-9 && (pos - d.DCenter).Length <= d.DRadiusValue - 1e-3;
    }

    private static Vec2 FallbackPlacement(TableSpec table, bool restrictToD) =>
        restrictToD && table.Snooker is { } s
            ? new Vec2(s.BaulkX - 0.12, 0.0)
            : new Vec2(-table.HalfLength / 2.0, 0.0);
}
