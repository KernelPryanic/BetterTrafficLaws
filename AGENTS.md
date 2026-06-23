# AGENTS.md — GTA V SHVDN script mods

A collection of best practices for any GTA V script mod built on **ScriptHookV
.NET (SHVDN3)**: a C# class that subclasses `GTA.Script`, runs inside the game via
the SHVDN runtime, and reacts to the game loop. These conventions are generic —
not tied to any one mod — and apply to every mod of this shape in this workspace.

## Core principles
- Best modern C# practices for the target framework (.NET Framework 4.8). Simple,
  reusable, maintainable code; maintainability and simplicity come first, optimize
  only where it pays off (the `OnTick` hot path is the one place that earns it).
- Use proper abstraction only where truly required. Abstractions belong at the
  seams (config load/save, the menu, persistence, one method per feature/action) —
  not mid-code. Three similar lines beat a premature helper.
- Write the minimal thing first. Don't generalize for hypothetical future
  features; add the table/strategy when a second concrete case actually shows up.
- Design for reversibility: each feature is a self-contained unit (one method, one
  flag, one menu item). Ask "what would it take to delete this feature?" — it
  should be removing those, nothing more.
- Breaking changes are fine when they make the code better.
- After changing code, revisit it: simpler? something now unused? remove it.

## Project shape
- `*.cs` — the script source. The entry point is a `Script` subclass; its
  constructor wires up `Tick`/`KeyUp`/`KeyDown` handlers and loads config. A small
  mod is fine as one file, organized by region (menu init, config load, the tick
  loop, the feature methods). As it grows, split by concern into separate
  files/classes — one `Script` subclass per independent script, plus plain classes
  for menu, config, persistence, and each feature group. The SHVDN runtime loads
  every `Script` subclass it finds, so a mod can be several cooperating scripts;
  don't cram unrelated behaviour into one giant tick loop. Each `Compile Include`
  in the `.csproj` is one source file — add the entry when you add a file.
- `*.csproj` — MSBuild project. `OutputType=Library` (the DLL *is* the mod),
  `Platform=x64`, `TargetFrameworkVersion=v4.8`. Keep `Version`/`AssemblyVersion`/
  `FileVersion` in sync with the version string shown in the menu title.
- `packages.config` — NuGet deps. SHVDN, UI libs, and any other runtime libraries
  are referenced from `..\packages\` via `<HintPath>`, not restored by NuGet —
  they're game-runtime assemblies.
- `bin/`, `obj/` — build output. Disposable; never committed (see `.gitignore`).
- The deployable artifact is the built `*.dll` (+ its `.ini`), dropped into the
  game's `scripts/` folder. If the mod depends on a library SHVDN does not ship
  (e.g. a JSON library), that DLL ships alongside it too. Larger mods may ship
  several files (data/asset folders, multiple DLLs) — keep the layout flat and
  mirror it in your packaging.

## Paths & I/O
- Resolve all file paths **next to the loaded DLL**, not against the process CWD —
  the game's CWD is the root folder where GTA5.exe lives, so a relative path would
  litter the game root. Use a small `ScriptPaths` helper built from
  `Assembly.GetExecutingAssembly().Location` (falling back to
  `AppDomain.CurrentDomain.BaseDirectory`); route the log, the `.ini`, and any data
  directories through it.
- Persistence must tolerate first run and corruption: a missing data file is the
  valid initial (empty) state, not an error; a corrupt file should log and start
  empty. Reads must never throw. Track an explicit "loaded" flag rather than
  inferring load-state from an empty collection, so a genuinely-empty store doesn't
  re-hit (and re-throw on) the disk every call.

## Style
- Follow `.editorconfig` (Allman-off: braces on the same line, no newline before
  `else`/`catch`/`finally`). 4-space indent.
- `readonly` for anything set once in the constructor (config-derived flags, keys).
  Mutable per-tick state stays plain fields.
- `PascalCase` for methods, fields, and properties; `camelCase` for
  locals/parameters; `UPPER` only where the game API already uses it. Match the
  surrounding file's existing casing.
- Each feature/action is a self-contained method (a `bool Is…()` predicate for a
  detection, a `void …()` for an action). Keep predicates pure of side effects
  where possible so the tick loop just composes them. When there are many, group
  them into a class (or one class per feature) rather than letting the `Script`
  subclass sprawl — keep the tick loop a thin dispatcher.
- Keep types under a single mod namespace. One public `Script` subclass per
  in-game script; helpers (menu, config, logger, persistence, feature classes) are
  plain classes, `internal`/`static` unless another assembly needs them.
- Keep conversions (speed/units/angles) in named helpers (`ToKPH`, `ToMPH`,
  `DegreesToAngle`) rather than inlining magic factors.

## The tick loop
- `OnTick` runs every frame — it is the hot path. Process the menu, then
  **throttle** expensive work (run checks/scans once every N ticks via a counter).
  Don't do per-frame `World.GetNearby*` scans or heavy work without throttling.
- Wrap the tick body in one broad `try/catch` that **logs** (`Logger.LogError`)
  and returns — a held `Vehicle`/`Ped` may despawn between ticks and throw on
  access. The catch must not swallow blind.
- Bail out early and cheaply: not enabled, no player, already in the wrong state
  (not driving, wrong vehicle class, etc.) → `return` before any scan.
- Don't iterate a collection by index while a callee mutates it (e.g. a despawn
  that removes from the list you're walking). Iterate a snapshot (`.ToList()`).

## Native calls & game API
- Prefer the SHVDN wrapper types (`Vehicle`, `Ped`, `Game.Player`, `World`) over
  raw natives. Drop to `Function.Call<T>(Hash.…, …)` only when there's no wrapper.
- Comment WHY a magic hash/constant is what it is (a model hash, a timing window, a
  cone angle) — the value alone tells the reader nothing.
- Guard entity access: check `null` / `.Exists()` before dereferencing; a `Ped` or
  `Vehicle` you held last tick may be gone this tick.
- Capture/restore symmetry: if a mod reads game state into a snapshot and writes it
  back later, every persisted property must be read in the capture half AND written
  in the restore half — an asymmetry is the usual cause of "X doesn't save"
  reports. Mind apply order and prerequisites: some properties only take effect
  after a setup call (e.g. installing the mod kit) or in a specific sequence.
- Dispose `IDisposable` resources (`using`) even for short-lived helpers.

## Config (INI)
- Settings load via `ScriptSettings.Load(...)` in the constructor (with a
  DLL-relative path). Seed defaults idempotently (write only when the key is
  absent) so a user's existing file is never clobbered, then read each value back
  into a field.
- Menu changes write straight back through `Config.SetValue` + `Config.Save()`, so
  the `.ini` and the live UI stay in sync. Keep the seeding default and the
  read-back fallback identical.

## UI / menu
- The in-game menu uses **LemonUI** (`NativeMenu`, `NativeItem`,
  `NativeCheckboxItem`, `NativeListItem<T>`). Build it once in an init method;
  subscribe handlers there.
- The menu title's version string is user-facing — bump it together with the
  `csproj` versions on release.
- List items map by value, not index (`Items.FindIndex(x => x == saved)`); keep the
  option set and the persisted value in the same units so the lookup matches. If a
  menu item's title is later used as a lookup key, keep title and key derivation in
  sync or lookups silently miss.

## Errors & logging
- Never silently swallow an error that the user or a future maintainer needs to
  see. The tick loop's broad `catch` exists because a despawned-entity exception
  must not crash the script mid-frame — but it **logs** and returns, it doesn't
  swallow blind. A bare `catch { return false; }` is only acceptable for a
  best-effort per-item operation where there is genuinely nothing to do with the
  failure (and even then, prefer logging) — e.g. per-property capture/restore loops
  that catch-and-log per item so one bad entry doesn't abort the whole operation.
- Diagnostics go to the mod's own log file (next to the DLL, cleared on startup)
  via a `Logger` helper, not anywhere that depends on a console the player doesn't
  have. The logger itself must swallow file-IO failures — a locked or unwritable
  log must never crash the mod.
- Fail fast on programmer errors; don't add defensive fallbacks that hide bugs.

## Build / lint / test
- `make build` — compile the mod (Release x64, .NET 4.8) to `bin/`.
- `make lint` — a clean rebuild; for C# the compile *is* the static check, so keep
  it warning-free (don't let deprecation warnings accumulate). Green `make lint` is
  the bar that matters.
- `make test` — no automated runner; the game is the integration test. The target
  just prints the manual-test steps.
- `make package` — build, then zip an upload-ready archive (the `scripts/` layout
  the site expects: the mod DLL plus any non-SHVDN dependency DLLs, never the
  player-provided SHVDN/LemonUI assemblies). The zip name has no version suffix.
- `make rebuild` / `make clean` — full rebuild / remove `bin/`+`obj/`(+`dist/`).
- The Makefile discovers MSBuild via `vswhere`; override with
  `make build MSBUILD=/path/to/MSBuild.exe` if your VS edition differs. VS Code's
  `Ctrl+Shift+B` runs the same MSBuild via `.vscode/tasks.json`.
- To test in-game: copy the built `*.dll` (+ its `.ini` and any non-SHVDN
  dependency DLLs) into the game's `scripts/` folder and reload scripts in-game.
- SHVDN / LemonUI assemblies are *not* shipped by the mod — they come from the
  player's ScriptHookV .NET install. Reference them, don't bundle or commit them.

## Don't do
- No comments that restate the code. Comment only WHY: a native-API quirk, a magic
  hash/threshold, an entity-lifetime caveat, an apply-order requirement. If a
  careful reader wouldn't miss it, delete it.
- Don't do unthrottled heavy work in `OnTick` — it costs the player frames.
- Don't write files to relative paths (the game root) — always resolve next to the
  DLL.
- Don't commit `bin/`, `obj/`, `.vs/`, log files, or stray `*.dll`.
- Don't reference SHVDN/LemonUI/other libs by absolute machine path or a pinned
  `SpecificVersion` — keep `SpecificVersion=False` so it binds against whatever the
  player has installed.
- Don't leave dead code in the shipped script (superseded paths, old experiments,
  no-op constructions) — delete it rather than letting it rot.
