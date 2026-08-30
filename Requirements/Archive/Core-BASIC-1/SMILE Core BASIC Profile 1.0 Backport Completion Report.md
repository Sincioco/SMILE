# SMILE Core BASIC Profile 1.0 Backport Completion Report

## Outcome

SMILE 1.0 now implements one canonical source language: **SMILE Core BASIC Profile 1.0** (`Core BASIC 1`), frozen from the current SMILE 2.0 implementation at `ec61dfa6324de7b22ea5ca0959828ff40e5e3902`.

This is an intentional breaking migration. No Core/Legacy selector, language option, automatic detection, compatibility alias, retry parser, or hidden fallback remains. Earlier SMILE 1.0-only source is rejected.

## Repository record

| Repository | Path | Branch | SHA during implementation | Final state |
|---|---|---|---|---|
| SMILE 1.0 | `D:\SMILE` | `main` | `5739b60c5a1a690fdb419e91796ea611e536e2bf` | implementation changes left uncommitted |
| SMILE 2.0 authority | `D:\SMILE 2.0` | `main` | `ec61dfa6324de7b22ea5ca0959828ff40e5e3902` | clean and unmodified |

At Sin's explicit request, all pre-existing SMILE 1.0 work was committed before this migration as `5739b60` (`Sin and Codex: Generate direct top-level Python scripts`). Its parent was `586bf22a0e391147e4cac1ac0a261eec85453dd2`. No migration work was added to that commit. No push occurred.

The post-snapshot task began from a clean SMILE 1.0 worktree. Consequently, every final working-tree change is part of this migration; the pre-existing user work remains preserved in the requested snapshot commit.

## Baseline

Before production edits, restore and Debug build succeeded with no errors, and the then-current MissionGuardrail passed 5/5. The course correction subsequently authorized deleting or replacing tests whose only purpose was preserving superseded source behavior.

## Frozen profile and corpus

- Human-readable profile: `docs/SMILE Core BASIC Profile 1.0.md`
- Official specification: `docs/SMILE Language Specification/001 - SMILE Core BASIC 1 Official Specification.md`
- Manifest: `tests/CoreBasicParity/profile.json`
- Manifest SHA-256: `e6e91989e8af9acca2ec087d528ac07ead902a3a4d2b4ef677fdb1bfd3a9224e`
- Positive unchanged programs: 2
- Negative programs: 9
- Hashed artifacts: 13, including paired stdout files

The two positive files cover the main statement/expression profile and the authoritative `For` counter boundary behavior. Both compile unchanged in SMILE 1.0 and SMILE 2.0. The negative corpus protects old declarations/mutation, input, pre-test loops, raw Print templates, interpolation, backslash escaping, old comment markers, and unsupported Text ordering.

## Canonical feature scope

Implemented:

- case-insensitive Unicode identifiers and the SMILE 2.0 reserved-word set;
- significant physical lines, original source spans, and apostrophe comments;
- Number, Boolean, and Text literals/defaults with exact type stability;
- doubled-quote Text escaping and normalized physical newlines in open Text;
- direct assignment with implicit first declaration;
- typed scalar `Dim` and program-level compile-time `Const`, including forward references and cycle rejection;
- unary, arithmetic, comparison/equality, concatenation, and short-circuit Boolean expressions with SMILE 2.0 precedence;
- blank, expression-list, and trailing-semicolon `Print`;
- `If / Else If / Else / End If`;
- ascending and descending inclusive `For`, once-evaluated bounds, zero-iteration behavior, and authoritative post-loop counter value;
- post-tested `Do / Loop [Until]`;
- lexically targeted `Exit For` and `Exit Do`, including cross-kind nesting;
- `End Program` from top level or a reached nested block.

Deliberately deferred/rejected:

- `Option Explicit`, arrays, Select Case, procedures/functions/calls, parameters, modules/imports/visibility, user-defined or object types;
- game window, graphics, runtime input, pointer, timer/random, audio, files/data, assets, Renderer3D, and other SMILE 2.0 superset facilities;
- every earlier SMILE 1.0-only source form listed in the migration guide.

## Architecture changes

- Replaced separate old tokenization/parsing behavior with the sole `Parser` and its nested canonical lexer.
- Replaced binding with the sole `Binder`, which owns case-insensitive names, exact scalar types, constants, loop counters, and typed-exit validity.
- Added focused canonical syntax and bound forms while retaining SMILE 1.0's lexer/parser/binder/bound-tree architecture rather than referencing a SMILE 2.0 assembly.
- Reduced `SmileEvaluator` to the canonical public API and canonical statement execution; removed the obsolete input overload/runtime path.
- Kept `SmileTranspiler` as the small public facade and registered one canonical backend for each of the ten active target IDs.
- Added a shared canonical target renderer with explicit per-target lowering. Every backend consumes the same bound program.
- Removed CLI/Desktop language selection and old interactive-input branching. Desktop owns one canonical transpiler.
- Replaced highlighting and the packaged `language.smile` with canonical syntax only.
- Deleted active old-language tests, specifications, and examples rather than skipping them or keeping the old behavior alive.

## All-target generation

| Target | Native constructs used |
|---|---|
| C# | typed locals/constants, `for`, `do`, `if`, `break`/target label only when required, `Console.Write`, `return` |
| C | fixed-width scalar storage, `for`, `do`, `if`, `break`/`goto` only for cross-kind exit, standard output, `return` |
| MASM x64 | `.data` storage, compare/branch loop labels, direct CRT output, ABI-correct calls, `ExitProcess` |
| JavaScript | `let`/`const`, structured loops and conditionals, labeled break only for cross-kind exit, stdout write, process exit |
| Java | primitive/String storage, structured loops and conditionals, labeled break only when required, `System.out.print`, return |
| COBOL | Working-Storage, `DISPLAY`, `PERFORM VARYING`, `PERFORM WITH TEST AFTER`, `EXIT PERFORM`/target paragraph, `STOP RUN` |
| Objective-C | dependency-light C-compatible console constructs in a `.m` file |
| Swift | top-level storage, `stride`, `repeat`, `if`, native print, labeled break only when required, `exit(0)` |
| Python | direct module-level storage, `range`, `while True`, `if`, native print, `SystemExit` |
| C++ | standard scalar/String storage, `for`, `do`, `if`, streams, normal return |

All four checked-in examples generate all ten primary files. The milestone toolchain fixture built and ran successfully on every one of the ten locally detected toolchains, matched evaluator stdout, returned successfully, and emitted no compiler warnings.

## Helpers and why they exist

No cross-target runtime or shared runtime library was added.

- C and Objective-C emit `smile_text_concat` only when Text `+` occurs because those targets have no native string-concatenation operator for dependency-light C strings.
- Python emits `_smile_div` only for `/` or `Mod`, and `_smile_mod` only for `Mod`, because Python floor division/remainder differs from the profile's truncate-toward-zero signed behavior.
- Python emits a tiny loop-exit exception only for a loop actually targeted across a differently nested loop kind. Ordinary and nearest-loop exits use plain `break` and receive no exception scaffolding.
- MASM emits its Text concatenation format and scratch buffer only when Text `+` occurs.

## Before/after generated example

Before the breaking migration, research source such as the following entered the old generator path:

```text
LET Name = "Sin"
PRINT Hello {Name}
```

It is now deliberately rejected, so it produces no target source.

Canonical source:

```smile
Name = "Sin"
Total = 0
For I = 1 To 2
    Total = Total + I
End For
Do
    Total = Total - 1
Loop Until Total = 0
Print "Hello "; Name
End Program
```

Representative C# output (formatting shortened only around the containing class):

```csharp
string Name = "";
long Total = 0;
long I = 0;
Name = "Sin";
Total = 0;
long _smile_for_end_1 = 2;
for (I = 1; I <= _smile_for_end_1; I++)
{
    Total = Total + I;
}
do
{
    Total = Total - 1;
} while (!(Total == 0));
Console.Write("Hello ");
Console.Write(Name);
Console.WriteLine();
return;
```

This shows direct assignment, expression Print, native For, native post-test Do, and target-native program termination. Source after an unconditional `Exit` or `End Program` is not emitted into that sequential target block.

## Authoritative implementation findings

The following material ambiguities were resolved from current SMILE 2.0 behavior:

- `Option Explicit` is explicitly deferred from Profile 1.0 despite appearing in one broad course-correction feature list.
- Text supports `=`, `<>`, and `+`; ordering such as Text `<` is rejected (SMILE 2.0 reports SML3308).
- A normally completed ascending/descending For leaves its counter one step past the final bound; a zero-iteration loop leaves the start value. This required explicit final-counter lowering for Python and Swift native ranges.
- The full SMILE 2.0 reserved vocabulary remains unavailable as identifiers even where its feature family is excluded.

## Validation actually run

Final commands and results:

```powershell
dotnet restore SMILE.sln -nologo
# succeeded; all projects up to date

dotnet build SMILE.sln -c Debug --no-restore -nologo
# succeeded; 0 warnings, 0 errors

dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --no-build --no-restore --filter "FullyQualifiedName~CoreBasic" --logger "console;verbosity=normal" -nologo
# passed 30/30

dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --no-build --no-restore --filter TestCategory=MissionGuardrail -nologo
# passed 3/3

dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --no-build --no-restore --filter "TestCategory=CoreBasic&TestCategory=Toolchain" -nologo
# passed 1/1; the single matrix test built/ran all 10 detected targets

powershell -ExecutionPolicy Bypass -File scripts/Test-CoreBasicParity.ps1
# passed 3/3; both repositories matched; authority SHA/status unchanged

git diff --check
# no whitespace errors (Git reported only configured LF-to-CRLF notices)
```

The detailed toolchain run reported C#, C, MASM x64, JavaScript, Java, COBOL, Objective-C, Swift, Python, and C++ all available and passing.

Desktop verification was automated rather than an interactive GUI session: its canonical packaged reference loaded and transpiled in every pane, the XAML exposed no language selector, and the highlighting tests accepted canonical keywords/apostrophe comments while refusing to color obsolete words as canonical keywords.

## Documentation and examples

Updated/replaced:

- README, Core Principles, Architecture, Toolchains, target generation standard, Roadmap, and historical index;
- one current official specification in place of nine contradictory old-language specifications;
- frozen profile, migration guide, parity report, deterministic manifest, and this completion report;
- cumulative packaged `examples/language.smile`, compact `core-basic.smile`, control-flow example, and existing canonical Print example;
- CLI help, Desktop version `1.0.0`, XAML, and syntax highlighting.

## Known target-native tradeoffs

- The evaluator enforces signed-64 overflow and divide-by-zero rules. Generated destinations intentionally use their clearest native integer operations rather than a large uniform checked-arithmetic runtime, so extreme overflow behavior remains target-native.
- JavaScript uses BigInt for integral operations; Swift may trap on native overflow; C-family overflow behavior follows the selected native/compiler representation.
- COBOL Text variables use a 4096-character field, and MASM Text concatenation uses a 64 KiB scratch buffer. These limits do not affect the ordinary deterministic parity corpus but remain target-specific bounds for unusually large Text.
- Objective-C uses portable C-compatible console code to keep the Windows toolchain dependency-light rather than requiring Foundation.

No unsupported SMILE 2.0 superset feature was imported. No network or sibling dependency is required for ordinary builds/tests; only the explicitly invoked optional parity script reads the sibling authority checkout.

## Source-control conclusion

SMILE 2.0 is still clean at its original SHA. SMILE 1.0 migration changes remain unstaged and uncommitted for review. The only commit made in this task sequence was the separately authorized pre-start snapshot commit. Nothing was pushed.
