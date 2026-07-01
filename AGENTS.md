# AGENTS.md — GTA V SHVDN script mods

House style for any GTA V script mod built on **ScriptHookV .NET (SHVDN3)**: a C#
class that subclasses `GTA.Script`, loaded by the SHVDN runtime, reacting to the
game loop. Generic — applies to every mod of this shape in the workspace.

## Core principles
- **Simplicity first.** Best modern C# for .NET Framework 4.8; maintainable over
  clever. Optimize only where it pays — `OnTick` is the one hot path that earns it.
- **Abstract at the seams only** (config, menu, persistence, one method per
  feature) — never mid-code. Three similar lines beat a premature helper.
- **Minimal thing first.** Don't generalize for hypothetical features; add the
  table/strategy when a second concrete case actually appears.
- **Reversible features.** Each is a self-contained unit (one method, one flag, one
  menu item) — deleting it should mean removing exactly those, nothing more.
- Breaking changes are fine when they make the code better. After changing code,
  revisit: simpler now? something unused? delete it.

## Project shape
- **`*.cs`** — a `Script` subclass wires `Tick`/`KeyUp`/`KeyDown` and loads config in
  its constructor. One file is fine for a small mod (organize by region); split by
  concern as it grows — one `Script` per independent script, plain classes for menu,
  config, logger, persistence, each feature group. SHVDN loads every `Script` it
  finds, so a mod can be several cooperating scripts — don't cram unrelated behaviour
  into one giant tick loop. Each `Compile Include` in the csproj is one file.
- **`*.csproj`** — MSBuild, `OutputType=Library` (the DLL *is* the mod),
  `Platform=x64`, `v4.8`. Legacy (non-SDK) projects ignore the csproj
  `<AssemblyVersion>`/`<FileVersion>` — the DLL version comes from
  `Properties/AssemblyInfo.cs`; keep it and the csproj `<Version>` in sync (the git
  tag is the source of truth, stamped by `set-version.ps1`).
- **Deps** — SHVDN, LemonUI, and other runtime libs are referenced from
  `..\packages\` via `<HintPath>` with `SpecificVersion=False` (game-runtime
  assemblies, not NuGet-restored). `bin/`/`obj/` are disposable, never committed.
- **Artifact** — the built `*.dll` (+ its `.ini`), dropped into the game's
  `scripts/`. Any non-SHVDN dependency DLL ships alongside; keep the layout flat and
  mirror it in packaging.

## Paths & I/O
- **Never write to the game root or a relative path** (the game's CWD is where
  GTA5.exe lives). Route the log, `.ini`, and data through a `ScriptPaths` helper. On
  the **Enhanced** host, files under the game tree are LOCKED at launch — write
  runtime data to `%APPDATA%\GTA V Mods\KernelPryanic\<ModName>\` instead.
- **Persistence tolerates first run and corruption:** a missing file is the valid
  empty state, a corrupt one logs and starts empty — reads never throw. Track an
  explicit "loaded" flag so a genuinely-empty store doesn't re-hit disk every call.

## Style
- Follow `.editorconfig`: braces on the same line, no newline before
  `else`/`catch`/`finally`, 4-space indent.
- `readonly` for anything set once in the constructor (config flags, keys); mutable
  per-tick state stays plain fields.
- `PascalCase` methods/fields/properties, `camelCase` locals/params, `UPPER` only
  where the game API already uses it. Match the surrounding file.
- **Each feature is a self-contained method** — a `bool Is…()` predicate (pure of
  side effects where possible) or a `void …()` action, so the tick loop just
  composes them. Group many into a feature class; keep the tick loop a thin
  dispatcher and the `Script` subclass from sprawling.
- One mod namespace; helpers are `internal`/`static` plain classes. Keep unit
  conversions in named helpers (`ToKPH`, `DegreesToAngle`), never inline factors.

## The tick loop
- `OnTick` runs every frame. Process the menu, then **throttle** expensive work (a
  counter, every N ticks) — no unthrottled `World.GetNearby*` scans or heavy work.
- **Bail early and cheaply:** not enabled, no player, wrong state → `return` before
  any scan.
- Wrap the body in one broad `try/catch` that **logs and returns** (a held
  `Vehicle`/`Ped` may despawn mid-tick and throw) — logs, never swallows blind.
- Don't mutate a collection you're iterating by index (a despawn removing from your
  list) — iterate a snapshot (`.ToList()`).

## Native calls & game API
- Prefer SHVDN wrappers (`Vehicle`, `Ped`, `Game.Player`, `World`) over raw natives;
  drop to `Function.Call<T>(Hash.…)` only when there's no wrapper.
- **Guard entity access:** `null`/`.Exists()` before every dereference — an entity
  held last tick may be gone this one.
- **Capture/restore symmetry:** every property read in the capture half must be
  written in the restore half — asymmetry is the usual "X doesn't save" bug. Mind
  apply order (some props need a setup call first, e.g. the mod kit).

## Config (INI)
- Load via `ScriptSettings.Load(...)` (DLL-relative) in the constructor. **Seed
  defaults idempotently** (write only when the key is absent) so a user's file is
  never clobbered, then read each value back into a field.
- Menu changes write straight back via `Config.SetValue` + `Config.Save()` so INI and
  UI stay in sync. Keep the seeding default and read-back fallback identical.

## UI / menu
- **LemonUI** (`NativeMenu`, `NativeItem`, `NativeCheckboxItem`, `NativeListItem<T>`).
  Build once in an init method; subscribe handlers there.
- The menu title's version string is user-facing — derive it at runtime from the
  assembly version so it always matches the release.
- **List items map by value, not index** (`Items.FindIndex(x => x == saved)`); keep
  the option set and the persisted value in the same units, and title/key derivation
  in sync, or lookups silently miss.

## Errors & logging
- **Never swallow blind.** The tick's broad `catch` exists only so a despawned-entity
  exception can't crash mid-frame — it logs and returns. A bare `catch { return
  false; }` is acceptable only for a best-effort per-item op (per-property
  capture/restore, so one bad entry doesn't abort the whole) — and even then prefer
  logging.
- Diagnostics go to the mod's own log (next to the writable data, cleared on
  startup) via a `Logger`, never a console the player lacks. The logger swallows its
  own file-IO failures — a locked log must never crash the mod.
- Fail fast on programmer errors; no defensive fallbacks that hide bugs.

## Build / lint / test
- `make build` — Release x64 .NET 4.8 → `bin/`. `make lint` — a clean rebuild; the
  compile *is* the static check, so keep it warning-free. `make rebuild`/`clean`.
- `make test` — no runner; the game is the integration test, the target prints
  manual steps.
- `make package` — build + zip the upload-ready `scripts/` layout (mod DLL + non-SHVDN
  deps, plus the Vortex `gta5mod.json`; never bundle the player's SHVDN/LemonUI). No
  version suffix in the zip name.
- Makefile finds MSBuild via `vswhere` (override `make build MSBUILD=…`); VS Code's
  `Ctrl+Shift+B` runs the same. To test: copy the DLL (+`.ini`+deps) into `scripts/`
  and reload in-game.

## Don't do
- No unthrottled heavy work in `OnTick`; no writing to the game root.
- Don't commit `bin/`, `obj/`, `.vs/`, logs, or stray `*.dll`; don't bundle or pin
  (`SpecificVersion`) SHVDN/LemonUI — bind against whatever the player has installed.
- No dead code, no comments that restate code — comment only WHY, concise and natural.
