# CLAUDE.md

Guidance for Claude (and any AI coding agent) working in this repository.

## Project

A 3D basketball video game built in **Unity 6 LTS with C#**.
Owner: Jimmy (GitHub: `milinazzojames`). Repo: https://github.com/milinazzojames/BasketballVideoGame

## Current focus: controls and physics

We are building slowly and incrementally. **Right now the only priorities are how the game feels to control and how the ball and players behave physically.**

In scope now:
- Player movement driven by the gamepad (analog speed, acceleration, turning, sprint)
- Ball physics: bounce, spin, rim and backboard collisions, shot arcs
- Dribbling, shooting, and (later) passing as mechanics
- Tuning values exposed in the Inspector or ScriptableObjects so they can be adjusted while playtesting

Out of scope until Jimmy says otherwise:
- Visual polish, models, materials, lighting, VFX
- Character animation (use capsules and primitives as stand-ins)
- UI beyond a simple debug overlay, menus, audio
- Rosters, game modes, AI teammates/opponents, scoring rules, online play

If a task tempts you toward out-of-scope work, stop and ask first.

## How to work

- **Small steps.** One mechanic or one tuning pass per branch/PR. Prefer a working rough version over a big unfinished one.
- **Explain before building.** For anything non-trivial, describe the plan in a few sentences and wait for Jimmy's OK.
- **Jimmy tests in Unity.** After each change, give clear steps for what to try in Play mode and what "good" should feel like. Don't claim something works until Jimmy has tested it.
- **Don't edit Unity-generated files by hand** (`.meta`, `.unity` scene and `.prefab` YAML) unless there is no other way. Prefer describing editor steps for Jimmy to do, or creating objects from code.
- **Keep it beginner-friendly.** Short comments explaining *why*, especially for physics math.

## Git workflow

- `main` is protected (ruleset "Protect Main"): no direct pushes, no force pushes. Changes land through pull requests.
- `develop` is the integration branch. Feature branches come off `develop` and merge back into it via PR; `develop` merges into `main` via PR at milestones.
- Branch names: `feature/<short-name>`, `fix/<short-name>`, `tune/<short-name>`, `docs/<short-name>`.
- Commit messages: short imperative summary (e.g. `Add analog player movement`), body if needed.
- Claude may create branches, commit, push, and open and merge PRs into `develop`. Never push to `main`.
- Large binary assets go through **Git LFS** (see `.gitattributes`). Never commit paid Asset Store assets (this repo is public).
- Never commit `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, or build output.

## Tech decisions

- **Engine:** Unity 6 LTS, Universal 3D (URP) template. Unity Personal license.
- **Editor:** VS Code with the Microsoft Unity extension.
- **Target platform:** Windows PC first.
- **Input:** Unity **Input System** package only (no legacy `Input.GetAxis`).
  - One Input Actions asset with action maps: `Offense`, `Defense`, `UI`.
  - Control schemes: `Gamepad` (primary) and `Keyboard&Mouse` (fallback).
  - Gameplay code reads **actions** (Move, Sprint, Shoot, Pass, DribbleStick...), never specific buttons. Bind to generic gamepad paths so Xbox and other controllers work.
  - Use analog values (stick magnitude, trigger pressure) wherever it adds feel.
  - Test controller: PS5 DualSense, wired USB. Close Steam / DS4Windows while testing, since they can remap the controller to a virtual Xbox pad.

### Starter control layout (PlayStation labels; will change through playtesting)

| Input | Offense | Defense |
|---|---|---|
| Left stick | Move | Move |
| Right stick | Dribble moves / aimed shot | Hands up / contest |
| R2 | Sprint | Sprint |
| L2 | Post up / protect ball | Defensive stance (slide) |
| Square | Shoot (hold & release) | Steal |
| Cross (X) | Pass | Switch player |
| Triangle | Lob / alley-oop | Block / rebound |
| Circle | Bounce pass / flashy pass | Take charge |
| L1 / R1 | Call screen / call play | Double team / call play |
| Options | Pause | Pause |

## Physics conventions

- **Units:** 1 Unity unit = 1 meter. Kilograms, seconds.
- Physics logic goes in `FixedUpdate`; read input in `Update` (or via action callbacks) and hand it to `FixedUpdate`.
- Consider lowering `Fixed Timestep` (e.g. 0.01 s) if the ball tunnels through the rim; use **Continuous Dynamic** collision detection on the ball.
- Reference real-world numbers, then tune for fun:

| Thing | Real value |
|---|---|
| Ball (size 7) | diameter ~0.24 m, mass ~0.62 kg |
| Ball bounce | dropped from 1.83 m, rebounds ~1.25–1.37 m |
| Rim height | 3.05 m |
| Rim inner diameter | 0.457 m |
| Backboard | 1.83 m × 1.07 m; rim's nearest edge 0.15 m from board |
| Free-throw line | 4.57 m from the backboard face |
| NBA 3-point line | 7.24 m (6.71 m in the corners) |
| NBA court | 28.65 m × 15.24 m |

- Put tunable numbers (speeds, accelerations, bounciness, shot arc) in serialized fields or ScriptableObject configs, never as magic numbers buried in code.

## Code conventions

- C#: PascalCase for types, methods, and public members; `_camelCase` for private fields; `[SerializeField] private` instead of public fields.
- One MonoBehaviour per file, file name matches class name.
- Folder layout under `Assets/`:
  - `Scripts/Player/`, `Scripts/Ball/`, `Scripts/Input/`, `Scripts/Core/`, `Scripts/Debug/`
  - `Settings/` (ScriptableObject configs, Input Actions asset)
  - `Scenes/` (start with a single `Sandbox` half-court scene)
  - `Prefabs/`, `Materials/`
- Keep systems loosely coupled: the ball shouldn't know about input, and input shouldn't know about the ball.

## Roadmap (controls and physics first)

Each step is meant to be one or a few small PRs. Check off as they land.

0. [x] Repo setup: Unity `.gitignore`, `.gitattributes` for LFS, this `CLAUDE.md`
1. [ ] Create the Unity project (Universal 3D) and install the Input System package
2. [ ] Sandbox scene: flat floor, one hoop (rim, backboard, pole) at real dimensions, simple camera
3. [ ] Input Actions asset with the starter layout (Move and Sprint wired up first)
4. [ ] Player movement: capsule moves with the left stick, analog speed, acceleration/deceleration, sprint on R2
5. [ ] Ball physics: a ball that bounces and rolls realistically and collides correctly with rim and backboard
6. [ ] Dribbling: ball attached to the player with a simple dribble loop, then right-stick dribble moves
7. [ ] Shooting: hold-and-release Square, shot meter/timing, a computed arc toward the rim with a make/miss window
8. [ ] Debug overlay: show speed, shot timing, release quality, and let values be tweaked live
9. [ ] Passing to a static target
10. [ ] Defense basics: stance/slide, hands up, steal attempt (against a dummy)

After this, revisit the plan: 1v1, animation, visuals, and game logic.

## Open questions

- No LICENSE yet (all rights reserved by default). Decide before inviting contributors.
- Camera style (broadcast side view vs. behind-the-player) — decide during step 2.
