# SMILE Core BASIC 2.1 Text-Game Foundation Completion Report

## Outcome

Complete. SMILE 1.0 now implements the Core BASIC 2.1 Text-Game Foundation in the evaluator and all ten active generators. The work includes fixed rank-two arrays, nonblocking key polling, screen clear, wait, monotonic timer, inclusive random assignment, numeric intrinsics, execution budgeting, three original games, real pseudo-console coverage, the complete installed-toolchain matrix, current documentation, and a self-contained student reference.

No target, test, or required game is skipped. No graphics, blocking `Input`, extra destination, external framework, npm package, JNA, or SMILE source thread primitive was added.

## Repository record

| Repository | Path | Branch | Starting SHA/status | Package-completion SHA/status | Modified by this work? |
|---|---|---|---|---|---|
| SMILE 1.0 | `D:\SMILE` | `main` | `f167d35b437115253428309f05993bc711bdc041`; `?? .codex/` | same HEAD; reviewed unstaged implementation plus the untouched `?? .codex/` | Yes |
| SMILE 2.0 authority | `D:\SMILE 2.0` | `main` | `fb33b44449043b8f52db6e0b828c774044e3bc3f`; `?? docs/plans/` | `0049c72eb80a8c1ea366cdfe5840f7db71e89d76`; `?? docs/plans/` | No |

SMILE 2.0 advanced externally during the work. The advance was inspected: it adds Renderer3D static-core work and does not change `src/Smile.Language`. Parity was repinned to the actual ending authority SHA. The verifier records SMILE 2.0's starting status and requires the SHA and exact status to remain unchanged, preserving the pre-existing `docs/plans/` work without writing to that repository.

The selected ZIP was `C:\Users\louie\Downloads\smile-1-text-game-foundation-hardening.zip`. Its 17 of 17 manifest entries passed SHA-256 verification, and every numbered Markdown instruction was read before production edits.

## Baseline assessment

The starting compiler already had one shared lexer/parser/binder/bound-tree pipeline, Core BASIC 1/2 parity corpora, scalar routines and recursion, `Select Case`, fixed rank-one arrays, ten active target generators, asynchronous Desktop operations, and milestone toolchain coverage. The missing foundation was rank-two storage, console polling/redraw/timing/random operations, deterministic evaluator hosting, target lifecycle helpers, interactive conformance, and full games.

Baseline restore and build passed. The baseline suite reported 111 tests: 109 passed and two parity tests failed only because their manifest pinned an older SMILE 2.0 checkout than the authority found locally. No baseline product failure was hidden.

## Final language additions

- Arrays have fixed rank one or two. Dimensions are positive compile-time Number expressions; each dimension and the checked product must fit 32-bit managed storage. Indexes are zero-based, rank-exact, checked, and evaluated left to right exactly once. Assignment checks every index before evaluating the right-hand value. Dynamic failure is `SMILER1210`.
- `Get Key Variable` makes one nonblocking poll into a writable Number. It returns `KEY_NONE` when no event is ready, normalizes W/A/S/D, arrows, Enter, Escape, Space, digits 1–4, Tab, and other keys, and never adds console `Input`.
- `Clear Screen` clears and homes an attached terminal; redirected output is an escape-free no-op.
- `Wait Duration Milliseconds` evaluates once, treats negative duration as zero, and uses a non-busy wait. The evaluator advances virtual time without sleeping.
- `Random Variable From Lower To Upper` evaluates inclusive bounds left to right once. Equal bounds are deterministic; reversed bounds fail with `SMILER1221`; the source is process-scoped and is not reseeded per statement.
- `Timer()` returns monotonic elapsed milliseconds. `Abs`, `Min`, and `Max` accept Number operands. Evaluator signed-minimum `Abs` overflow is `SMILER1206`; targets retain the documented target-native extreme-overflow policy where using the ordinary intrinsic is more educational.
- The evaluator statement budget defaults to 1,000,000 and fails a runaway program with `SMILER1222`.

Deliberate exclusions remain blocking `Input`, held-key state, mouse/pointer input, cursor positioning, colors, graphics/Game Window, audio, files/data, dynamic or rank-three arrays, array values/parameters/returns, `ByRef`, optional/named/variadic parameters, records, classes, modules/imports in SMILE source, and an eleventh target.

## Architecture changes

- Syntax, parser recovery, diagnostics, binder checks, symbols, and bound operations cover the new statements, intrinsics, rank metadata, and ordering contract.
- `ISmileEvaluationHost` separates key events, frame capture, virtual Wait, monotonic time, and deterministic Random from evaluator logic. Existing callers receive a safe virtual default host.
- `CoreBasicProgramFeatureSet` inventories only the runtime capabilities a program uses. Imports, fields, declarations, helpers, async propagation, and the COBOL companion are feature-gated from that inventory.
- Common structured-target console/runtime writing was split into `CoreBasicStructuredRuntimeWriter`; COBOL support and MASM lowering remain target-specific.
- Node marks only required routines async, awaits transitive calls and Wait operations, and restores raw input in `finally`.
- Build-only toolchain operation and generated pause launchers support real attached-console tests without weakening ordinary captured execution.
- CLI help, Desktop About/version, highlighting, examples, living documentation, and the packaged cumulative `language.smile` now describe the implemented 2.1 language.

## Main-first layout result

| Target | First executable body | User routines after it? | Runtime helpers last? | Structural result |
|---|---|---:|---:|---:|
| C# | `private static void Main()` | Yes | Yes | Pass |
| C | `int main(void)` | Yes | Yes | Pass |
| MASM x64 | `main PROC` | Yes | Yes | Pass |
| JavaScript (Node.js) | feature-driven `async function main()` | Yes | Yes; invocation follows helpers | Pass |
| Java | `public static void main` | Yes | Yes | Pass |
| COBOL | primary program body | Yes, as later program units | Console companion is a separate file | Pass |
| Objective-C | `int main(void)` | Yes | Yes | Pass |
| Swift | direct top-level script; no synthetic main | Functions/helpers precede top-level use as Swift declarations | N/A | Pass |
| Python | direct module-level script; no synthetic main/guard | Function/helper definitions precede direct statements | N/A | Pass |
| C++ | `int main()` | Yes | Yes | Pass |

Required imports, constants, storage, prototypes, external declarations, and type/data sections may precede main; they are not executable bodies.

Representative before/after generator evidence:

```text
Before Core BASIC 2.1:
Dim Board[2, 3] As Number
Get Key KeyCode
=> no generated program; rank two and Get Key were outside the grammar.
```

```javascript
// After, JavaScript (Node.js), condensed from generated Program.js:
let Board = Array.from({ length: 2 }, () => Array(3).fill(0n));
async function main() {
    try {
        Board[1][2] = 7n;
        KeyCode = smileGetKey();
        await smileWait(20n);
    } finally {
        smileCleanup();
    }
}
// user routines follow; feature-gated helpers are last
```

The affected features are arrays, assignment, expressions, routines, key polling, clear, Wait, Timer, Random, and numeric intrinsics across all ten targets. Custom helpers were added only for bounds/order preservation and terminal facilities that have no direct statement-level construct. No helper contains learner game rules.

## Target console and storage implementation

| Target | Get Key / Clear | Wait / Timer / Random | Rank-two storage | Cleanup |
|---|---|---|---|---|
| C# | `Console.KeyAvailable`/`ReadKey`, `Console.Clear` | `Thread.Sleep`, `Environment.TickCount64`, cryptographic fill | rectangular array | Console APIs require no retained raw mode |
| C | `_kbhit`/`_getch`, Win32 screen-buffer APIs | `Sleep`, `GetTickCount64`, one xorshift64* state | native fixed array | no retained mode |
| MASM x64 | direct CRT/Win32 calls | direct `Sleep`/`GetTickCount64`, process state | flattened checked offset | no retained mode |
| JavaScript (Node.js) | raw stdin queue, TTY ANSI clear | Promise `setTimeout`, `hrtime.bigint`, `crypto.randomBytes` | independent nested rows | `finally` restores raw stdin |
| Java | JDK 21 preview FFM to UCRT, console-gated ANSI clear | `Thread.sleep`, `nanoTime`, `ThreadLocalRandom` | primitive nested array | no retained mode |
| COBOL | feature-gated C/Win32 companion | feature-gated C support | nested `OCCURS` | no retained mode |
| Objective-C | portable C `_kbhit`/`_getch` and Win32 | C/Win32 helpers | native fixed array | no retained mode |
| Swift | CRT declarations and WinSDK-gated clear | `Thread.sleep`, system uptime, `Int64.random` | independent nested arrays | no retained mode |
| Python | `msvcrt`, console-gated ANSI clear | `time.sleep`, `monotonic_ns`, `random.randint` | independent list comprehensions | no retained mode |
| C++ | CRT polling and Win32 clear | `sleep_for`, `steady_clock`, `mt19937_64` | nested `std::array` | RAII/native calls require no retained mode |

Generated secondary files are limited to `GeneratedProgram.csproj` for deterministic .NET compilation and feature-gated COBOL `SmileRuntime.c` for UCRT/Win32 calls unavailable directly in GnuCOBOL. A `Run Program - Press Any Key.cmd` file is a toolchain workspace launcher, not destination source; it exists only when requested and preserves the child exit code.

## Evaluator evidence

Focused tests prove immediate and timed key scripts, `KEY_NONE`, virtual Wait and monotonic Timer, captured screen frames, deterministic injected random values, inclusive/cross-zero/equal ranges, budget failure, fresh rank-two local arrays in calls, static and dynamic bounds, default values, no aliased rows, index/RHS evaluation order, and exactly-once evaluation. Scripted sessions cover scoring/state changes in each complete game.

## Text-game results

| Game | Binds | All ten generate | All ten compile | All ten launch/redraw/exit | Deterministic evaluator session | Manual play |
|---|---:|---:|---:|---:|---:|---:|
| Trail Runner (`text-snake.smile`) | Yes | Yes | Yes | Yes | Yes | Yes, attached Node terminal |
| Lantern Maze (`text-maze-muncher.smile`) | Yes | Yes | Yes | Yes | Yes | Representative automation |
| Sky Foundry (`text-falling-blocks.smile`) | Yes | Yes | Yes | Yes | Yes | Representative automation |

Trail Runner is an original continuous trail game with WASD/arrows, food, score, growth, collision, restart, and Escape. Lantern Maze is an original bounded maze/lantern collection game with a moving hazard and Escape. Sky Foundry is an original falling-block workshop with seven four-cell families, movement/rotation/drop, row clearing, score, restart, and Escape; it does not use copied title/trade dress/assets.

The automated game matrix performed 30 native attached-console sessions. Each showed its title at least twice (initial frame plus redraw), accepted Enter and movement input, followed Escape, returned native exit code 0, and reached the restored launcher's `Press any key to exit` prompt. Representative manual Trail Runner play used a directly generated `Program.js`: Enter started the round, the board redrew and advanced, the round reached the border cleanly, Escape displayed the closing message, Node returned 0, and the terminal prompt/cursor state was restored.

## Ten-toolchain deterministic matrix

The deterministic fixture combines Number/Boolean/Text rank-two arrays, Clear, no-input Get Key, Wait/Timer, fixed and cross-zero Random, and `Abs`/`Min`/`Max`. Exact normalized output was `7Trueready`, `0`, `54-2-2`, `True`, `True` on every row.

| Target | Tool/version | Generated | Compiled | Ran/contract | Compiler warnings |
|---|---|---:|---:|---:|---:|
| C# | .NET SDK 10.0.400 | Yes | Yes | Pass | 0 |
| C | VS 2026 22.9.2 / MSVC 19.51.36256 | Yes | Yes | Pass | 0 |
| MASM x64 | MASM 14.51.36256 | Yes | Yes | Pass | 0 |
| JavaScript (Node.js) | Node.js 24.14.0 | Yes | syntax check pass | Pass | 0 |
| Java | javac 21.0.12.1, preview enabled | Yes | Yes | Pass | 0 |
| COBOL | GnuCOBOL 3.2.0 | Yes | Yes | Pass | 0 |
| Objective-C | Clang 21.1.6 | Yes | Yes | Pass | 0 |
| Swift | Swift 6.3.3 for Windows | Yes | Yes | Pass | 0 |
| Python | CPython 3.13.12 | Yes | syntax compile pass | Pass | 0 |
| C++ | VS 2026 22.9.2 / MSVC 19.51.36256 | Yes | Yes | Pass | 0 |

## Interactive matrix

| Target | Attached ConPTY | `KEY_NONE` | WASD/arrows | Enter/Space/digits/Tab/other | Escape/redraw/Wait | Restored launcher | Result |
|---|---:|---:|---:|---:|---:|---:|---|
| C# | Yes | Pass | Pass | Pass | Pass | Pass | Exit 0, within 20 s bound |
| C | Yes | Pass | Pass | Pass | Pass | Pass | Exit 0, within 20 s bound |
| MASM x64 | Yes | Pass | Pass | Pass | Pass | Pass | Exit 0, within 20 s bound |
| JavaScript (Node.js) | Yes | Pass | Pass | Pass | Pass | Pass | Exit 0, within 20 s bound |
| Java | Yes | Pass | Pass | Pass | Pass | Pass | Exit 0, within 20 s bound |
| COBOL | Yes | Pass | Pass | Pass | Pass | Pass | Exit 0, within 20 s bound |
| Objective-C | Yes | Pass | Pass | Pass | Pass | Pass | Exit 0, within 20 s bound |
| Swift | Yes | Pass | Pass | Pass | Pass | Pass | Exit 0, within 20 s bound |
| Python | Yes | Pass | Pass | Pass | Pass | Pass | Exit 0, within 20 s bound |
| C++ | Yes | Pass | Pass | Pass | Pass | Pass | Exit 0, within 20 s bound |

The 21 interactive tests consist of one harness self-test, ten full key-contract rows, and ten rows that each launch all three games.

## Regression and validation results

| Gate | Result |
|---|---|
| Debug solution build | Pass, 0 warnings, 0 errors |
| Full suite | 178 passed, 0 failed, 0 skipped; 4 min 13 s |
| `TextGameFoundation` package gate | 65 passed, 0 failed, 0 skipped; 2 min 1 s |
| Focused semantic/generation tests | 34 passed |
| Deterministic text-game toolchain rows | 10 passed |
| Interactive and game rows | 21 passed; 30 game launches |
| `MissionGuardrail` | 6 passed, 0 failed, 0 skipped |
| `MilestoneMatrix` | 20 passed, 0 failed, 0 skipped; 1 min 55 s |
| Core BASIC 1/2 parity script | 5 passed against authority `0049c72...`; status unchanged |
| Updated conformance plus parity focus | 47 passed |
| HTML/highlighting/Desktop focus | 9 passed |
| `git diff --check` | Pass; line-ending conversion notices only, no whitespace errors |

The full suite includes checked-in example validation, Desktop sample/loading behavior, process-runner cancellation and input modes, exact array bounds identity, and direct Python script structure. These were not skipped. GitHub Actions was not dispatched; this is local evidence, not a claim that CI ran.

Commands actually used for final or focused evidence included:

```powershell
dotnet restore SMILE.sln -nologo
dotnet build SMILE.sln -c Debug --no-restore -nologo
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-TextGameFoundation.ps1
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --no-build --filter TestCategory=MissionGuardrail -nologo
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --no-build --filter TestCategory=MilestoneMatrix -nologo
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-CoreBasicParity.ps1
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~SmileLanguageReferenceTests|FullyQualifiedName~CoreBasicHighlightingTests|FullyQualifiedName~CoreBasicDesktopTests" -nologo
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --no-build -nologo
git diff --check
git status --short
```

Manual-play preparation also ran the CLI generation path. Its ordinary `--run` mode deliberately uses the captured, closed-input runner, so the first interactive attempt displayed the title and hit the ordinary 10-second timeout. Running that generated `Program.js` directly with `node Program.js` in an attached terminal produced the successful manual result recorded above. Real-time games must use an attached console or the generated pause launcher; captured runner execution is for deterministic/noninteractive programs.

## Documentation and HTML

Current governance, product, architecture, toolchain, generation, migration, roadmap, parity, history, CLI, Desktop, highlighting, examples, and language-specification material was updated together. `docs/smile-1-language-reference.html` is a polished single-file reference with 37 singly numbered navigation entries, working internal anchors, unique IDs, no external CSS/script/image/font, at least five titled/described diagrams, no curved arrow paths, all ten targets, more than 20 all-target-valid examples, at least 12 evaluated examples with checked output, three game sections, key-code reference, and accurate exclusions. Automated protection rejects CSS/list double numbering.

All arrows in the HTML diagrams use straight line/polyline or 90-degree orthogonal connectors.

## Housekeeping and exact file record

No file was deleted. Existing useful history remains in place, and the older 2.0 specification is explicitly preserved as a subset record rather than presented as the current complete language. The monolithic generator was decomposed without adding dormant generators or compatibility modes.

Modified files:

- `AGENTS.md`, `README.md`
- `docs/Architecture.md`, `docs/Core BASIC 2 Parity Report.md`, `docs/Historical Requirements Index.md`, `docs/Migrating to Core BASIC 2.md`, `docs/Roadmap.md`, `docs/SMILE Core BASIC Profile 2.0.md`, `docs/SMILE Core Principles.md`, `docs/SMILE Language Specification/002 - SMILE Core BASIC 2 Official Specification.md`, `docs/SMILE Target Code Generation Standard v1.0.md`, `docs/Toolchains.md`, `docs/smile-1-language-reference.html`
- `examples/language.smile`, `scripts/Test-CoreBasicParity.ps1`
- `src/SMILE.Cli/Program.cs`, `src/SMILE.Desktop/Highlighting/SMILE.xshd`, `src/SMILE.Desktop/MainWindowViewModel.cs`, `src/SMILE.Desktop/SMILE.Desktop.csproj`
- `src/SMILE.Engine/Binder.cs`, `src/SMILE.Engine/CoreBasicSyntax.cs`, `src/SMILE.Engine/Evaluation.cs`, `src/SMILE.Engine/Generation/CoreBasicCobolWriter.cs`, `src/SMILE.Engine/Generation/CoreBasicCodeGenerator.cs`, `src/SMILE.Engine/Generation/CoreBasicMasmWriter.cs`, `src/SMILE.Engine/Language.cs`, `src/SMILE.Engine/Parser.cs`
- `src/SMILE.Toolchains/Toolchains.cs`
- `tests/CoreBasic2Parity/profile.json`, `tests/CoreBasicParity/profile.json`, `tests/SMILE.Tests/CoreBasic2ConformanceTests.cs`, `tests/SMILE.Tests/CoreBasic2ParityTests.cs`, `tests/SMILE.Tests/CoreBasicConformanceTests.cs`, `tests/SMILE.Tests/CoreBasicHighlightingTests.cs`, `tests/SMILE.Tests/CoreBasicParityTests.cs`, `tests/SMILE.Tests/SmileLanguageReferenceTests.cs`

Added files:

- `.codex/config.toml`
- `docs/SMILE Core BASIC 2.1 Text-Game Foundation Completion Report.md`
- `docs/SMILE Language Specification/003 - SMILE Core BASIC 2.1 Text-Game Foundation Official Specification.md`
- `examples/text-game-foundation.smile`, `examples/text-snake.smile`, `examples/text-maze-muncher.smile`, `examples/text-falling-blocks.smile`
- `scripts/Test-TextGameFoundation.ps1`
- `src/SMILE.Engine/EvaluationHost.cs`, `src/SMILE.Engine/Generation/CoreBasicCobolRuntimeSupport.cs`, `src/SMILE.Engine/Generation/CoreBasicProgramFeatureSet.cs`, `src/SMILE.Engine/Generation/CoreBasicStructuredRuntimeWriter.cs`
- `tests/SMILE.Tests/TextGameFoundationTests.cs`, `tests/SMILE.Tests/TextGameInteractiveMatrixTests.cs`, `tests/SMILE.Tests/TextGameToolchainMatrixTests.cs`, `tests/SMILE.Tests/WindowsPseudoConsole.cs`

The pre-existing `.codex/config.toml` contains only `sandbox_mode = "danger-full-access"`. It was initially preserved outside the package work, then inspected and added after Sin explicitly requested that `.codex/` be included in the repository.

## Known limitations and target-native tradeoffs

- Interactive behavior requires an attached Windows terminal. Redirected input returns `KEY_NONE`; redirected clear is a no-op. CLI/Desktop captured execution is noninteractive, so learners should run the generated program or pause launcher in an attached console for games.
- Wait duration and frame cadence are subject to the destination OS scheduler and timer granularity. Exact cross-target wall-clock timing is not promised.
- Random results are inclusive and unbiased for the implemented range mapping, but sequences intentionally differ by target and run.
- Java key polling requires JDK 21 preview FFM flags and the Windows UCRT. Swift key polling/clear uses Windows CRT/WinSDK declarations. COBOL needs its tiny generated C companion only for used console/time/random primitives.
- Full-frame terminal clear can visibly flicker on some console hosts.
- Fixed array dimensions and their product are limited to 2,147,483,647 by the compiler model; practical memory/toolchain limits are lower.
- `Abs(Int64.MinValue)` is a stable evaluator error. Generated destinations follow documented native extreme-overflow behavior rather than receiving a universal checked-arithmetic runtime.
- The generated code favors direct, readable native constructs. Bounds/order and terminal normalization helpers add some source volume to feature-using programs, but are omitted otherwise.

## Source control

At package completion, all implementation remained unstaged on `main` at the original SMILE 1.0 HEAD `f167d35b437115253428309f05993bc711bdc041` because the package explicitly withheld commit/push authorization. Sin subsequently issued direct instructions to make green-task auto-commit/push permanent and to add `.codex/`; those higher-authority follow-ups authorized this reviewed milestone and the inspected configuration file to be committed and pushed. SMILE 2.0 was not modified.
