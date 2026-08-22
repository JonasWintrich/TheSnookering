# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A desktop 3D billiards game (standard 8-ball pool + standard snooker) built with **Godot 4.7.2 (.NET) + C# on net8.0**. Visual target: a moody billiards lounge, distinctly better-looking than the Steam reference "Billard 3D: Pool". Long-term (not built yet): online multiplayer and an arcade powerup mode — the architecture below exists to keep those cheap.

The full approved design lives at `C:\Users\JonasWintrich\.claude\plans\this-is-a-blank-jiggly-ladybug.md` (stack rationale, physics constants, rules details, milestones M0–M5).

## Commands

The pinned Godot binary is in-repo (gitignored). In Git Bash:

```bash
GODOT="tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"

dotnet test tests/Snookering.Core.Tests           # the inner loop — seconds, no Godot needed
dotnet test tests/Snookering.Core.Tests --filter "FullyQualifiedName~Vec2Tests"   # single class
dotnet build Snookering.slnx                      # builds Core + game assembly (NOTE: .slnx, not .sln)

"$GODOT" --path game --headless --import          # re-import after adding/changing assets — ALWAYS before running
"$GODOT" --path game                              # run the game windowed (main.tscn)
"$GODOT" --path game res://scenes/<scene>.tscn    # run a specific scene

# Visual verification (Claude reads the PNG afterwards). Windowed — rendering needs the GPU:
"$GODOT" --path game -- --screenshot "<ABSOLUTE path>.png" --frame 45
# Logic verification without pixels (works with --headless):
"$GODOT" --path game --headless -- --dump-state "<ABSOLUTE path>.json" --frame 5
```

Harness flags (parsed by `game/scripts/debug/DebugAutoload.cs`, always after `--`): `--screenshot <path>`, `--dump-state <path>` (positions of nodes in group `balls`), `--frame <N>` (frames to wait; give TAA/GI ~45 to settle), `--quit-after <N>`. Paths must be absolute — Godot's cwd differs.

If Godot is missing from `tools/`, re-download: `Godot_v4.7.2-stable_mono_win64.zip` from github.com/godotengine/godot-builds releases, extract into `tools/godot/`.

## Architecture — the one rule that matters

**`src/Snookering.Core/` is a pure .NET library with ZERO Godot references. `game/scripts/` is presentation only and contains ZERO physics or rules logic.** Never break this boundary in either direction. It is what makes the sim unit-testable in seconds, deterministic for future multiplayer (clients exchange tiny quantized `ShotInput` structs and re-simulate identically), and reusable on a future authoritative server.

- Core layout: `Math/` `Physics/` `Tables/` `Rules/` `Ai/`. The sim is a pure function: `(TableState, ShotInput, TableSpec) → ShotResult { FinalState, SimEvent[], TrajectoryFrame[], StateHash }`.
- Godot's `MatchController` feeds `ShotInput` in and plays back the returned trajectory frames; the rules engines consume the event list; audio/VFX trigger off events. Rendering never influences simulation.
- Table/pocket collision geometry is **analytic data in Core** (`TableSpec`: cushion segments + jaw arcs), never a mesh. Gray-box visuals are generated from the same spec so visuals and physics cannot drift.

## Determinism rules (Core sim code)

Enforced by golden-trajectory-hash tests; violations = test failures, not style nits:
- `double` scalar math only — no `System.Numerics` SIMD vector types, no `float` in sim state.
- No trig (`Sin/Cos/Atan2/Pow`) inside the sim loop; directions are unit vectors. Trig is allowed only at the `ShotInput → initial velocities` conversion. `Math.Sqrt` is fine. No `FusedMultiplyAdd`.
- No clock, no engine access, no randomness except the seeded RNG carried in `ShotInput`.
- Fixed iteration order everywhere (arrays, not dictionaries); simultaneous events tie-broken by `(time, ball id, event type)`.

## Godot conventions (AI-workflow specific)

- Namespaces: `Snookering.Core.*` (note: math lives in `Snookering.Core.Mathematics`, not `.Math`, to avoid clashing with `System.Math`); game side is `Snookering.Game.*`. Every node script must be `partial`.
- Keep `.tscn` files small, shallow composition roots; build dynamic content (tables, ball spawning) in C# code. Reference resources by `res://` path in code, never by uid string. Commit `.uid` sidecar files.
- `dotnet build` must succeed **before** running any scene that references C# scripts, and `--headless --import` must run after any asset addition — stale imports and missing assemblies produce confusing scene-load errors.
- No editor-only bake steps (no LightmapGI, no occlusion baking) — lighting is SDFGI + one static ReflectionProbe so everything works from the CLI.
- Every downloaded asset gets a line in `game/assets/ATTRIBUTION.md` (CC0 preferred, CC-BY allowed with attribution) in the same session it lands.
