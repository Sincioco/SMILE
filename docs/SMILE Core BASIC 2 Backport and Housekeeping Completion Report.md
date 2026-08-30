# SMILE Core BASIC 2 Backport and Housekeeping Completion Report

## Outcome

**Complete.** SMILE 1.0 now implements one canonical SMILE Core BASIC 2 language through the evaluator and all ten registered target generators. The milestone includes focused conformance, a 100-cell all-toolchain validation matrix, pinned cross-repository parity, current documentation, archived historical Profile 1 documents, dead-code removal, thirteen checked-in examples, and a self-contained student reference.

The authority repository remained read-only throughout this work. During final validation, SMILE 2.0 advanced from the package's starting commit to a new branding-only commit. Both parity manifests and current SMILE 1.0 documents were repinned to the new authoritative commit, and the full parity gate was rerun successfully.

## Repository record

| Repository | Path | Branch | Starting SHA | Ending SHA/status | Modified by this work? |
|---|---|---|---|---|---|
| SMILE 1.0 | `D:\SMILE` | `main` | `cf300b3bab481620e5335b3828a0d538eeca6a3a` | milestone committed and pushed by explicit follow-up; `main` and `origin/main` aligned; preserved `?? .codex/` | Yes |
| SMILE 2.0 authority | `D:\SMILE 2.0` | `main` | `ec61dfa6324de7b22ea5ca0959828ff40e5e3902`, clean | `9aa9583a651eab452ea3af80772b08b68fc03220`, clean | No |

Starting SMILE 1.0 status:

```text
?? .codex/
```

Starting SMILE 2.0 status was clean. The final authority SHA is the public `origin/main` commit `9aa9583a651eab452ea3af80772b08b68fc03220`, subject `Sin and Codex: docs(branding): define the SMILE acronym`; its only shared-profile authority change is the expansion **Simple Modern and Intuitive Language for Everyone**. No `src/Smile.Language` syntax or semantic file changed in that commit.

## Implemented language profile

The one supported language now includes:

- optional first-item `Option Explicit`;
- top-level `Sub` and `Function` declarations;
- `Call` and `Return`;
- exact typed positional scalar parameters, ByVal whether the optional word is present or omitted;
- forward calls, direct recursion, and mutual recursion;
- fresh local variables and arrays per invocation;
- global/local lookup and explicit local shadowing;
- selector-once, first-match `Select Case` with exact typed constant cases and optional final `Case Else`;
- fixed one-dimensional `Number`, `Boolean`, and `Text` arrays with zero-based checked indexes;
- whole-program `End Program` propagation from any active routine;
- all earlier valid Core BASIC 1 programs as a language subset, without a Profile 1 mode or parser.

Deliberate exclusions are console Input, ByRef, Optional/ParamArray/named arguments, array parameters or returns, dynamic or multidimensional arrays, Enum, Type, Module, Import, classes/OOP, files/data, timing/randomness, graphics, games, media, and audio. Historical `LET`, `SET`, old `INPUT`, `WHILE`, interpolation, raw Print templates, block strings, backslash escapes, and alternate comments are rejected. There is no dialect selector, alias, fallback lexer/parser, or hidden compatibility path.

## Resolved authority findings

The final implementation follows direct inspection and execution of the current SMILE 2.0 authority:

- SMILE expands to **Simple Modern and Intuitive Language for Everyone**. The authority changed this branding during final validation, so current SMILE 1.0 governance, README, Desktop title/About text, principles, tests, and HTML were aligned.
- Boolean output is exactly `True` and `False`.
- Ordinary binary operands, array indexes, Select selectors, For bounds, nested calls, and arguments evaluate exactly once from left to right.
- `And` and `Or` short-circuit and may skip the right operand.
- Parameters are ByVal with or without the optional keyword. Assigning a parameter cannot change caller storage.
- A routine invocation owns fresh parameter, local, and local-array storage. Direct and mutual recursion are valid.
- A local declaration may shadow a global. A same-named read before a later local declaration is not silently redirected to the global.
- A Function must Return its exact declared type on every reachable normal path. A loop is never assumed to execute for definite-return analysis.
- Select evaluates one selector, requires exact-type compile-time cases, rejects duplicate values, and executes the first match.
- The pinned authority rejects a blank source item immediately between the Select header and its first Case. Shared fixtures omit that one blank; later case-body blank lines remain valid.
- Arrays are fixed, one-dimensional, zero-based, default initialized, and checked before access. Dynamic failure uses `SMILER1210`.
- `End Program` exits the entire program even when reached through a routine call.

## Architecture changes

### Tokens, parser, and syntax

The lexer/token model now recognizes the Core BASIC 2 routine, parameter, selection, and bracket forms. `Parser.cs` is the sole reachable parser and constructs ordered top-level/routine source items, calls, Returns, Select clauses, and array expressions/assignments. Historical syntax/scanner nodes were removed rather than retained behind a switch.

### Symbols, scopes, and binder

The bound program owns global variables, constants, routine signatures, routine declarations, parameters, locals, arrays, calls, Returns, and Select clauses. Binding is multipass: it inventories globals/constants and routine signatures before bodies, resolves forward/mutual calls, creates routine scopes, validates exact arguments/returns, performs definite-return checks, folds dimensions/cases, and diagnoses static bounds errors. Generators consume symbols and never resolve source names.

### Evaluator

The evaluator uses global storage plus a reentrant call-frame stack. It implements ByVal copies, fresh locals/local arrays, Return transfer, recursion, selector-once Select, checked arrays, and whole-program termination. It remains the expected-output oracle for generated execution.

### Generation

`Generation.cs` remains the small facade. One structured writer serves the seven structured backends with explicit target branches, while dedicated current writers handle COBOL and MASM. C, C++, and Objective-C capture operands/arguments where native evaluation order is not guaranteed. Swift emits immutable parameter backing values unless the routine assigns the parameter. COBOL uses recursive program units and fresh local storage. MASM uses Windows x64 ABI frames and emits CRT/bounds support only when used.

### Desktop, CLI, and validation

Desktop highlighting includes the new keywords and brackets. The packaged cumulative `language.smile` includes routines, recursion, Select, and arrays. The Desktop title/About acronym and informational version are current. CLI help explicitly describes dependency-free JavaScript (Node.js). HTML tests validate every positive snippet against all ten generators and every documented runnable output against the evaluator.

## Generated target mapping

| Target | Routine construct | Array construct | Select construct | Helpers added and why |
|---|---|---|---|---|
| C# | static methods and native calls | native typed arrays | captured selector plus `if / else if / else` | `SmileIndex` only for programs with arrays, because SMILE requires a deterministic diagnostic |
| C | static functions; readable temporaries preserve left-to-right calls | fixed C arrays | captured selector plus conditionals | `smile_index`; Text concatenation helper only when Text `+` occurs |
| MASM x64 | ABI-correct recursive `PROC` frames, register/stack arguments | QWORD global or frame storage | compare/branch labels | `smile_bounds_fail` only for arrays; low-level calls/frames are required by the target |
| JavaScript (Node.js) | ordinary functions | native `Array(...).fill(...)` | captured selector plus conditionals | `smileIndex` prevents negative access and sparse extension |
| Java | static methods | primitive/String arrays | captured selector plus conditionals | `smileIndex` supplies the stable SMILE runtime diagnostic |
| COBOL | recursive program units with LINKAGE and LOCAL-STORAGE | `OCCURS` | `EVALUATE` | inline checked one-based subscript conversion and call-capture temporaries are required to preserve zero-based ByVal semantics |
| Objective-C | dependency-light static C-compatible functions | fixed C arrays in `.m` | captured selector plus conditionals | same narrowly emitted index/Text helpers as C |
| Swift | `func` and copied mutable parameters only when assigned | native arrays | captured selector plus conditionals | `smileIndex` supplies checked SMILE behavior |
| Python | `def`, direct module-level learner statements | native lists | captured selector plus `if / elif / else` | `smile_index`; truncating division/remainder helpers where Python differs; typed-exit exception only for a cross-kind loop exit |
| C++ | static functions | `std::array` | captured selector plus conditionals | `smile_index` supplies stable checked behavior |

No helper was added for ordinary routines, calls, recursion, or Select where a native destination construct already preserves the language rule.

## JavaScript/Node.js result

- Stable target ID: `javascript`.
- User-facing name: **JavaScript (Node.js)**.
- Primary output: dependency-free `Program.js`.
- Validation runtime: Node.js `v24.14.0`.
- npm dependencies, modules, TypeScript, and an eleventh target: none.
- Runtime-specific helper: `smileIndex` only when arrays occur; it prevents negative indexes and sparse extension and reports `SMILER1210`.

## Before and after generated example

Source idea:

```smile
Option Explicit
Dim Scores[2] As Number
Scores[0] = 21
Print Twice(Scores[0])
End Program

Function Twice(Value As Number) As Number
    Return Value * 2
End Function
```

Before this milestone, the active parser rejected `Option Explicit`, brackets, Function, call expressions, and Return, so no target output existed. After the milestone, representative actual lowering is:

```csharp
private static long[] Scores = new long[2];
private static long Twice(long Value) { return Value * 2; }
Console.Write(Twice(Scores[SmileIndex(0, 2, "Scores")]));
```

```javascript
let Scores = Array(2).fill(0n);
function Twice(Value) { return Value * 2n; }
process.stdout.write(String(Twice(Scores[smileIndex(0n, 2, "Scores")])));
```

```python
Scores = [0] * 2
def Twice(Value):
    return Value * 2
print(Twice(Scores[smile_index(0, 2, "Scores")]), end="")
```

```cobol
05 STUDENT-Scores.
   10 STUDENT-Scores-ITEM PIC S9(18) COMP-5 OCCURS 2 TIMES.
PROGRAM-ID. STUDENT-Twice IS RECURSIVE.
```

```asm
_smile_Scores QWORD 2 DUP(0)
_smile_Twice PROC
    push rbp
    mov rbp, rsp
    ; parameter frame and ordinary arithmetic
_smile_Twice ENDP
```

The affected features are routines, ByVal parameters, calls/returns, arrays, and bounds on all ten targets. The native constructs are methods/functions/program units/PROCs and native array storage. The checked-index helpers are unavoidable because destination behaviors range from negative indexing and sparse extension to one-based or unchecked memory access.

## Semantic evidence

- `core-basic-2-evaluation-order.smile` records nested call order and short-circuit side effects; evaluator and all ten executables match.
- `core-basic-2-byval-scope.smile` proves parameter assignment isolation and shadowing.
- `core-basic-2-recursion.smile` proves direct and mutual recursion with fresh frames.
- `core-basic-2-local-arrays.smile` proves local arrays are re-defaulted for ordinary and recursive calls.
- `core-basic-2-parameters.smile` covers zero, one, four, five, eight, and sixteen parameters, including stack arguments on MASM.
- `core-basic-2-select.smile` and focused tests prove selector-once, typed constants, first match, duplicate rejection, and final Case Else.
- `core-basic-2-arrays.smile` plus the ten-target expected-failure matrix prove defaults and checked dynamic indexes.
- `core-basic-2-end-program-routine.smile` proves whole-program termination from a routine on all ten executables.
- `CoreBasic2ConformanceTests` supplies parser, binder, evaluator, negative, definite-return, scope, and static-bounds coverage.

## Housekeeping

| Deleted or changed item | Reason | Safety evidence | Replacement |
|---|---|---|---|
| old syntax/scanner/analysis files | unreachable pre-Core implementation | reference search, build, and 111-test suite | canonical parser/binder/evaluator |
| dormant ten-generator predecessor family and orphan helpers | duplicate architecture was not registered | registry/reference search and 100-cell matrix | current Core BASIC writer family |
| active Profile 1 docs | no longer the current specification | byte-preserved copies under historical archive | Profile 2 spec/profile/migration/parity docs |
| old active assertions | preserved superseded behavior | rewritten current tests; no skips | Core BASIC 2 conformance/generation tests |
| root governance/current docs | contained old syntax and acronym claims | active-doc searches and current authority diff | current Core BASIC 2 wording |

No NuGet/npm package or project reference was required or added. No test is skipped. No build/output directory is staged. No user file was discarded. The pre-existing `.codex/` directory was left untouched.

### Exact files modified

- `AGENTS.md`
- `README.md`
- `docs/Architecture.md`
- `docs/Historical Requirements Index.md`
- `docs/Roadmap.md`
- `docs/SMILE Core Principles.md`
- `docs/SMILE Target Code Generation Standard v1.0.md`
- `docs/Toolchains.md`
- `examples/language.smile`
- `scripts/Test-CoreBasicParity.ps1`
- `src/SMILE.Cli/Program.cs`
- `src/SMILE.Desktop/Highlighting/SMILE.xshd`
- `src/SMILE.Desktop/MainWindow.xaml`
- `src/SMILE.Desktop/MainWindowViewModel.cs`
- `src/SMILE.Desktop/SMILE.Desktop.csproj`
- `src/SMILE.Engine/Binder.cs`
- `src/SMILE.Engine/CoreBasicSyntax.cs`
- `src/SMILE.Engine/Evaluation.cs`
- `src/SMILE.Engine/Generation.cs`
- `src/SMILE.Engine/Generation/CoreBasicCodeGenerator.cs`
- `src/SMILE.Engine/Language.cs`
- `src/SMILE.Engine/Parser.cs`
- `src/SMILE.Engine/SyntaxKind.cs`
- `src/SMILE.Engine/SyntaxToken.cs`
- `src/SMILE.Engine/TargetIdentifierMap.cs`
- `tests/CoreBasicParity/profile.json`
- `tests/SMILE.Tests/CoreBasicConformanceTests.cs`
- `tests/SMILE.Tests/CoreBasicGenerationTests.cs`
- `tests/SMILE.Tests/CoreBasicHighlightingTests.cs`

### Exact files created

- `docs/Core BASIC 2 Parity Report.md`
- `docs/Migrating to Core BASIC 2.md`
- `docs/SMILE Core BASIC Profile 2.0.md`
- `docs/SMILE Core BASIC 2 Backport and Housekeeping Completion Report.md`
- `docs/SMILE Language Specification/002 - SMILE Core BASIC 2 Official Specification.md`
- `docs/smile-1-language-reference.html`
- `examples/core-basic-2-arrays.smile`
- `examples/core-basic-2-byval-scope.smile`
- `examples/core-basic-2-canonical.smile`
- `examples/core-basic-2-end-program-routine.smile`
- `examples/core-basic-2-evaluation-order.smile`
- `examples/core-basic-2-local-arrays.smile`
- `examples/core-basic-2-parameters.smile`
- `examples/core-basic-2-recursion.smile`
- `examples/core-basic-2-select.smile`
- `src/SMILE.Engine/Generation/CoreBasicCobolWriter.cs`
- `src/SMILE.Engine/Generation/CoreBasicMasmWriter.cs`
- `tests/CoreBasic2Parity/byval-scope.smile`
- `tests/CoreBasic2Parity/byval-scope.stdout`
- `tests/CoreBasic2Parity/canonical.smile`
- `tests/CoreBasic2Parity/canonical.stdout`
- `tests/CoreBasic2Parity/profile.json`
- `tests/CoreBasic2Parity/recursion.smile`
- `tests/CoreBasic2Parity/recursion.stdout`
- `tests/SMILE.Tests/CoreBasic2ConformanceTests.cs`
- `tests/SMILE.Tests/CoreBasic2ParityTests.cs`
- `tests/SMILE.Tests/CoreBasic2ToolchainMatrixTests.cs`
- `tests/SMILE.Tests/SmileLanguageReferenceTests.cs`

The following historical files were moved byte-for-byte from active `docs/` locations into `Requirements/Archive/Core-BASIC-1/`:

- `001 - SMILE Core BASIC 1 Official Specification.md`
- `Core BASIC Parity Report.md`
- `Migrating to Core BASIC 1.md`
- `SMILE Core BASIC Profile 1.0 Backport Completion Report.md`
- `SMILE Core BASIC Profile 1.0.md`

### Exact production files deleted

- `src/SMILE.Engine/Analysis.cs`
- `src/SMILE.Engine/ExecutionTrace.cs`
- `src/SMILE.Engine/FullLineCommentFacts.cs`
- `src/SMILE.Engine/InterpolatedStringScanner.cs`
- `src/SMILE.Engine/Generation/BoundProgramSimplifier.cs`
- `src/SMILE.Engine/Generation/BoundStatementTree.cs`
- `src/SMILE.Engine/Generation/CCodeGenerator.cs`
- `src/SMILE.Engine/Generation/CGenerationHelpers.cs`
- `src/SMILE.Engine/Generation/CSharpCodeGenerator.cs`
- `src/SMILE.Engine/Generation/CobolCodeGenerator.cs`
- `src/SMILE.Engine/Generation/CppCodeGenerator.cs`
- `src/SMILE.Engine/Generation/GeneratorConditionFacts.cs`
- `src/SMILE.Engine/Generation/GeneratorValueFacts.cs`
- `src/SMILE.Engine/Generation/JavaCodeGenerator.cs`
- `src/SMILE.Engine/Generation/JavaScriptCodeGenerator.cs`
- `src/SMILE.Engine/Generation/MasmX64CodeGenerator.cs`
- `src/SMILE.Engine/Generation/MasmX64NativeGeneration.cs`
- `src/SMILE.Engine/Generation/ObjectiveCCodeGenerator.cs`
- `src/SMILE.Engine/Generation/PythonCodeGenerator.cs`
- `src/SMILE.Engine/Generation/ReadOnlyListExtensions.cs`
- `src/SMILE.Engine/Generation/RuntimeTextPlan.cs`
- `src/SMILE.Engine/Generation/SwiftCodeGenerator.cs`
- `src/SMILE.Engine/Generation/TargetComments.cs`
- `src/SMILE.Engine/Generation/TargetExpression.cs`
- `src/SMILE.Engine/Generation/TargetIntegerProfile.cs`
- `src/SMILE.Engine/Generation/TargetMultilineLiterals.cs`
- `src/SMILE.Engine/Generation/TargetRuntimeFacts.cs`
- `src/SMILE.Engine/Generation/TargetTypes.cs`
- `src/SMILE.Engine/Generation/TextOutput.cs`

The five old active documentation paths were also removed after their historical copies were created. No test file was merely disabled or skipped; current test bodies were rewritten in place and new Profile 2 suites were added.

## Student language-reference HTML

- Final path: `docs/smile-1-language-reference.html`.
- File size: 44,622 bytes.
- Self-contained result: inline CSS, JavaScript, SVG, and system fonts; no stylesheet/script/image dependency, CDN, network fetch, or package.
- Content: 32 navigation sections, 23 compiler-validated positive examples, 15 evaluator-validated runnable/output pairs, 13 checked-in `.smile` examples, 10 target cards, and 6 accessible SVG diagrams.
- Validation: unique IDs, all internal anchors, exactly one authored navigation number, no CSS counters/ordered markers, exact target count, JavaScript (Node.js) terminology, and excluded-feature wording all pass.
- Diagram rule: every diagram has `<title>` and `<desc>`; connectors use only `<line>` or `<polyline>` and no `<path>`.
- Browser review: Chromium-based Codex in-app browser at 1920×1080, 1366×768, and 390×844; no global horizontal overflow; mobile navigation collapse, search, theme toggle, and keyboard focus presentation reviewed.
- Print review: Microsoft Edge/Chromium 146 produced a 24-page Letter PDF. All pages were rendered to PNG and inspected as a contact sheet, with long cumulative code and the final page inspected at full size. The first print pass exposed a repeated skip-link/search-control defect; the final pass hides interactive controls and has no clipping, overlap, or broken section transition.

## Validation commands and exact results

Instruction intake and baseline:

```powershell
# ZIP enumeration and System.IO.Compression entry inspection under Downloads
# matching package found; manifest-sha256.txt verified 19/19

dotnet restore SMILE.sln
# passed

dotnet build SMILE.sln -c Debug -nologo
# baseline passed

dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug -nologo
# baseline: 53 passed, 0 failed, 0 skipped

powershell -ExecutionPolicy Bypass -File scripts/Test-CoreBasicParity.ps1
# baseline Profile 1 parity: 3 passed
```

Final focused and full validation:

```powershell
dotnet build SMILE.sln -c Debug -nologo
# passed; 0 warnings, 0 errors

dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter FullyQualifiedName~CoreBasic2ConformanceTests -nologo
# 41 passed, 0 failed, 0 skipped

dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter FullyQualifiedName~CoreBasicGenerationTests -nologo
# 7 passed, 0 failed, 0 skipped

dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=MissionGuardrail -nologo
# 4 passed, 0 failed, 0 skipped

dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=HtmlValidation -nologo
# 3 passed, 0 failed, 0 skipped

dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=MilestoneMatrix -nologo
# 10 tests passed; 90 valid compile/run/output cells plus 10 expected SMILER1210 runtime-failure cells; 0 compiler warnings

powershell -ExecutionPolicy Bypass -File scripts/Test-CoreBasicParity.ps1
# 5 passed in 20 seconds; Profiles 1 and 2 match SMILE 2.0 9aa9583a651eab452ea3af80772b08b68fc03220

dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug -nologo
# 111 passed, 0 failed, 0 skipped in 2 minutes 13 seconds

git diff --check
# exit 0; no whitespace errors (Git emitted only configured LF-to-CRLF notices)
```

Read-only integrity/search commands also verified:

```powershell
git -C 'D:\SMILE 2.0' rev-parse HEAD
git -C 'D:\SMILE 2.0' status --short
# 9aa9583a651eab452ea3af80772b08b68fc03220; clean

rg -n "AnalysisSession|ExecutionTrace|InterpolatedStringScanner|FullLineCommentFacts|BoundProgramSimplifier|BoundStatementTree|MasmX64NativeGeneration|RuntimeTextPlan|TargetExpression" src tests --glob '*.cs'
# no active references

rg -n -i "8-program|eight Profile|outside Profile 1.0|INPUT Name|IF/WHILE|WHILE bodies" AGENTS.md README.md docs src --glob '!Requirements/**'
# no stale active matches; migration-only SET example excluded from this expression
```

Implementation-time failures were fixed rather than hidden:

- HTML validation initially found a target-terminology phrase, an overbroad ID regex, and a reserved example routine name.
- The first warning assertion treated MSBuild's `0 Warning(s)` as a warning; the assertion was corrected.
- The all-target matrix then found real Swift immutable-parameter warnings and a MASM cross-procedure `End Program` label; both generators were fixed.
- A print contact sheet found the repeated fixed-position skip link; print CSS was corrected and all pages rerendered.
- A final full suite passed 109 tests but failed two parity pins after SMILE 2.0 advanced to a new authority commit during the run. The authority diff was inspected, the acronym was aligned, both manifests were repinned, parity passed, and the full suite then passed 111/111.

## Ten-toolchain matrix

| Target | Tool/version | Generated | Compiled | Ran | Output matched | Warnings |
|---|---|---:|---:|---:|---:|---:|
| C# | .NET SDK 10.0.400 | 9/9 | 9/9 | 9/9 | 9/9 | 0 |
| C | Visual Studio/MSVC 18.9.2 | 9/9 | 9/9 | 9/9 | 9/9 | 0 |
| MASM x64 | Visual Studio/MASM 18.9.2 | 9/9 | 9/9 | 9/9 | 9/9 | 0 |
| JavaScript (Node.js) | Node.js v24.14.0 | 9/9 | direct-run | 9/9 | 9/9 | 0 |
| Java | Temurin JDK/javac 21.0.12.1 LTS | 9/9 | 9/9 | 9/9 | 9/9 | 0 |
| COBOL | GnuCOBOL 3.2.0 | 9/9 | 9/9 | 9/9 | 9/9 | 0 |
| Objective-C | MSYS2 Clang 22.1.8 | 9/9 | 9/9 | 9/9 | 9/9 | 0 |
| Swift | Swift 6.3.3 for Windows | 9/9 | 9/9 | 9/9 | 9/9 | 0 |
| Python | CPython 3.13.12 | 9/9 | direct-run | 9/9 | 9/9 | 0 |
| C++ | Visual Studio/MSVC 18.9.2 | 9/9 | 9/9 | 9/9 | 9/9 | 0 |

Each target also generated, compiled/direct-ran, and produced the expected nonzero `SMILER1210` bounds failure in the tenth matrix cell. Thus the milestone executed 100 target cells total: 90 valid exact-output cells and 10 expected runtime-failure cells.

## Cross-repository parity

The final gate ran against a clean SMILE 2.0 checkout at `9aa9583a651eab452ea3af80772b08b68fc03220` and rechecked the same clean SHA afterward.

Profile 1 positive fixture hashes:

| Fixture | Source SHA-256 | stdout SHA-256 |
|---|---|---|
| `canonical` | `de958319adb71c7d36ac32cc6bc751a9880c7fec89b7bfae9274262c0b518b94` | `6c0787bfba3b37d97161fb5bbeca7ad74a682af52bfdaec39b7d4c4412a12e83` |
| `counter-semantics` | `b737ab9b7584fa37dd37a9fb9ba97a6ab9e80108180cd5e01a0e8aca1531e529` | `663f5b9f1d35c2633dab5ca48f47096639d5abdcc2d8e2e5543f81986bb7831d` |

Profile 2 hashes:

| Fixture | Source SHA-256 | stdout SHA-256 |
|---|---|---|
| `canonical` | `508cd6032e712f3497872476583cda058505bde747670dcb3c541b4dd0d89249` | `8fefdcd14242d1fc4d24de5d4460f38259b8c1b24c87de8584930a02f1985dc3` |
| `byval-scope` | `b6dc161edc8803b4970d2f0d80f47d4f27132c68de1b0d71e880134103869985` | `b15600d5ed3d5d0b6e2b8d307327594009c93947f138dd83d528cbd6a218bd07` |
| `recursion` | `2e54f90ba52a417fe21a8107e111152b4eb4b6463eca2fc6af40d8b9cc1c37f4` | `1d7c601de16f9dca5c3da6174464e71184414ae979e425203079e30b1c9f4ba7` |

Five parity tests passed: three retained Profile 1 checks and two Profile 2 checks. The Profile 2 source bytes are identical to the public examples. The intentional restriction is the frozen Core BASIC 2 profile; it is not a claim that SMILE 1.0 implements every SMILE 2.0 feature.

## Known target-native tradeoffs

- Number is a signed 64-bit contract, but overflow at extreme values can follow destination-native checked/unchecked behavior. The compiler does not add an arbitrary-precision runtime.
- Deep or infinite recursion ends through destination-native stack exhaustion.
- COBOL Text storage is a fixed `PIC X(4096)` teaching representation.
- C-family and MASM Text concatenation use generated allocation suitable for short educational console programs; there is no full ownership/garbage-collection runtime.
- Objective-C intentionally uses portable C-compatible console constructs and does not require Foundation.
- Python needs small helpers for truncating division/remainder because native `//` and `%` differ for negatives.
- Deterministic checked-array behavior requires a small helper or inline check even where a target has its own exception or bounds behavior.
- MASM and COBOL necessarily expose more ABI/storage ceremony than structured languages, but learner routines, arrays, calls, and selection remain recognizable.
- Toolchain-specific compiler warnings are zero for the milestone corpus.

## Deferred work

Console Input remains a separate authority-first milestone. ByRef, Optional/ParamArray/named parameters, multidimensional/dynamic arrays, Enum, Type, modules/imports, classes/OOP, files/data, graphics, games, media, and audio also remain outside this profile. None is presented as implemented, hidden, or partially scaffolded.

## Source-control conclusion

- The milestone was initially completed without source-control mutation, as its instruction package required.
- Sin then gave a separate explicit follow-up instruction to change the acronym everywhere, commit, and push.
- This report is included in that public commit on `main`; after the push, local `main` and `origin/main` are aligned. The commit hash is intentionally not embedded in the commit that defines it; `git rev-parse HEAD` supplies the immutable identifier.
- The pre-existing untracked `.codex/` directory is preserved, untouched, and excluded from the commit.
- No branch was created; work remained on `main`.
- SMILE 2.0 is clean at `9aa9583a651eab452ea3af80772b08b68fc03220` and was never modified by this work.
