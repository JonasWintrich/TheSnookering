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

Two-instance multiplayer test (the peers must agree on every shot hash):

```bash
"$GODOT" --path game -- --host --break 0.9 &     # peer A, fires once the guest joins
"$GODOT" --path game -- --join 127.0.0.1         # peer B
# both logs must show identical "[net] shot N physics=... rules=..." lines
```

Windows build. Two prerequisites, both easy to trip over:
export templates must be installed in `%APPDATA%/Godot/export_templates/4.7.2.stable.mono/`
(the `.tpz` from the same godot-builds release, unzipped), and **`game/Snookering.Game.sln`
must exist** — Godot's .NET exporter looks for a classic solution named after the
assembly beside the project, and does not read the root `Snookering.slnx`. Without it
the export still produces an .exe, but with no managed assemblies, so the game silently
does nothing:

```bash
"$GODOT" --path game --headless --export-release "Windows Desktop" ../out/build/Snookering.exe
```

Two-machine networking can be exercised locally. `--host` / `--join <addr>` drive
two instances, and `tools/udp_relay.py` stands in for a tunnel service (playit.gg),
including its address rewriting:

```bash
python tools/udp_relay.py 25999 127.0.0.1 24555 --delay 90 --loss 4   # 180 ms RTT, 4% loss
"$GODOT" --path game -- --host --break 0.9 --quit-after 620
"$GODOT" --path game -- --join 127.0.0.1:25999 --quit-after 560
```

Both logs must print the same `[net] shot N physics=... rules=...` and the host
`shot N agreed`.

Harness flags (parsed by `game/scripts/debug/DebugAutoload.cs`, always after `--`): `--screenshot <path>`, `--dump-state <path>` (positions of nodes in group `balls`), `--frame <N>` (frames to wait; give TAA/GI ~45 to settle), `--quit-after <N>`. Paths must be absolute — Godot's cwd differs.

If Godot is missing from `tools/`, re-download: `Godot_v4.7.2-stable_mono_win64.zip` from github.com/godotengine/godot-builds releases, extract into `tools/godot/`.

Asset regeneration (all procedural, deterministic):

```bash
python tools/gen_ball_textures.py        # ball albedos (pool + snooker) -> game/assets/balls/
python tools/gen_audio.py                # synthesized WAVs -> game/assets/audio/ (~11 s, 67 files)
python tools/analyze_audio.py            # objective checks on the generated audio
dotnet run --project src/Snookering.Tools -- dump-tables tools/tables.json   # physics geometry as JSON
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background \
  --python tools/make_table.py -- tools/tables.json game/assets/models      # hero table GLBs
```

Audio is synthesized from physics, not sampled: impacts use the Hertzian contact
pulse (dF/dt), so contact time — and therefore brightness — follows impact speed.
Each family is baked at three reference speeds ("tiers") with several variants;
`AudioManager` crossfades neighbouring tiers and picks variants by a hash of the
sim event, so a replayed shot sounds identical. `analyze_audio.py` asserts the
claims that cannot be checked by ear here (brightness rises with speed, the cue
is darker than a ball click, cushions have no click attack, loops do not drone).

Run `--headless --import` after regenerating any asset. Hero GLB material names
(Cloth/CushionCloth/Wood/DarkWood/Leather/Hole) are a contract: TableBuilder
remaps them to runtime materials by name; the procedural table remains as the
fallback when the GLBs are absent.

## Multiplayer (implemented)

Deterministic lockstep over ENet: peers exchange one `ShotInput` per shot and
re-simulate. `ShotInput` carries the ball-in-hand placement, so **one turn is one
message** and a shot is fully described by its input. Both peers cross-check two
hashes per shot — `ShotResult.StateHash` (physics) and `RulesHash` (turn, groups,
scores); the physics hash alone cannot catch a rules divergence. Comparison is
keyed by shot index, because the guest can finish a shot before the host does.
The AI never runs online: `DeterministicRng.NextGaussian` uses libm
transcendentals, the only place two machines could legitimately differ.
Player-facing setup instructions are in `MULTIPLAYER.md`.

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
