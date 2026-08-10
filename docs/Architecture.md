# Architecture

## Current Strategy

SMILE is a beginner-first educational programming language. Generated source is learner-facing output, so architecture must make normal native destination-language code the easiest generator path.

The permanent rules live in [SMILE Core Principles](SMILE%20Core%20Principles.md). All ten implemented destinations are active: C#, C, Windows x64 MASM Assembly, JavaScript, Java, COBOL, Objective-C, Swift, Python, and C++.

One shared compiler pipeline feeds every generator. Product exposure, routine toolchain detection, Transpile All, and target enumeration use the same central active-target policy.

## Compiler Pipeline

```text
Source
  -> Lexer and physical-source scanners
  -> Tokens plus ordered comments/blank lines
  -> Recursive parser
  -> Syntax tree
  -> Binder and variable symbols
  -> Ordered bound tree
  -> Statement/branch/loop analysis
  -> Target generator
  -> Learner-facing generated files
  -> Optional active toolchain build and run
```

The front end defines SMILE semantics once. Target generators consume the shared bound tree; they do not reparse source text or invent target-specific SMILE language rules.

`Parser.cs` remains focused on parsing. `Binder.cs` remains focused on binding, type checking, and producing the canonical bound representation. `Generation.cs` remains the small public generation facade. Shared generator helpers live under `src/SMILE.Engine/Generation/`, with each destination generator in its own focused file.

This organization is intentionally small. It does not require a plugin system, reflection-based discovery, a generic generated runtime, or a template framework.

## Front End And Ordered Source

The lexer and parser own:

- case-insensitive SMILE keywords;
- ordinary and interpolated Strings;
- raw PRINT templates;
- LET/SET Block String scanning and normalization;
- full-line comments;
- blank source lines;
- recursive IF and WHILE structure;
- syntax diagnostics and source spans.

Curly braces are interpolation holes in text-oriented syntax. They are not general variable-reference delimiters. Expression and statement positions use direct identifiers.

Comments and blank physical lines remain ordered non-semantic source items. They retain source order for generation but never receive runtime values, statement ordinals, mutations, or execution-trace entries. Block String content remains owned by the Block String scanner and cannot be reclassified as comments or control-flow headers.

## Binding, Runtime State, And Analysis

The binder resolves variables through the shared ordinal case-insensitive symbol table and assigns the current SMILE `String`, `Integer`, or `Boolean` type.

- LET declares and initializes a variable.
- SET evaluates a SMILE expression and updates an existing variable without changing its type.
- INPUT reads one runtime line into an existing variable without changing its type.
- IF and WHILE consume the same bound expression model and preserve genuine source control flow.

Current runtime values belong to the evaluator environment, not permanently to `BoundLetStatement`.

Analysis remains statement-order, mutation, branch, and loop aware. It distinguishes source-known values from runtime-unknown values, never propagates an initializer past SET or INPUT, never leaks one branch's value into another, and never substitutes a pre-loop value for current loop-carried storage.

The compiler does not execute or unroll learner WHILE loops to discover facts. The evaluator executes actual selected branches and loop iterations and accepts host cancellation.

## Revised INPUT Boundary

INPUT keeps one canonical syntax and bound statement, but the compiler no longer requires every generated target to copy the evaluator's byte-level input implementation.

The language-level contract is:

- operate on one existing variable;
- let the fixed variable type choose conversion;
- read an ordinary line of text;
- keep the resulting value runtime-unknown to static propagation;
- consume input only on reached paths;
- contain evaluator read/conversion failures;
- keep CLI and Desktop interaction responsive.

Strict UTF-8 byte algorithms, a universal 4096-byte limit, embedded-NUL console input, exact CR/LF edge handling, and identical cross-target error text are not current universal requirements.

The reference evaluator retains injectable input so focused tests remain deterministic. It is the semantic reference for ordinary behavior, not a runtime implementation template for generated source.

## Central Active-Target Policy

`ActiveTargetLanguages` is the small central source of truth for the current active set.

Conceptually:

```csharp
public static class ActiveTargetLanguages
{
    public static readonly IReadOnlyList<TargetLanguage> All = TargetLanguageInfo.All;
}
```

The central policy drives:

- Desktop target selectors and default panes;
- CLI target enumeration and `--target all`;
- Transpile All behavior;
- routine generation tests;
- normal toolchain detection;
- active-target documentation.

Do not scatter independent hard-coded target checks across the UI, CLI, toolchains, and tests. All ten implemented backends remain registered and returned as normal active choices.

## Native-First Generation

For every SMILE feature, a generator first identifies the ordinary beginner-level destination construct and emits it directly when practical.

### C#

Prefer ordinary C# facilities such as `Console.WriteLine`, `Console.ReadLine`, conventional parsing, normal variables, interpolation, `if`, and `while`. Do not expose raw input streams, a custom UTF-8 line reader, or a generated `SmileRuntime` for a simple program.

### C

Prefer ordinary C facilities such as `printf`, `scanf`, and `fgets` where each is appropriate. Use normal variables, arrays/strings, conditions, and loops. Document a target-native limitation instead of automatically generating a general cross-runtime simulator.

### Windows x64 MASM

Assembly is naturally more verbose, but it must remain proportional to the source program. Prefer recognizable CRT/Win64 calls, clear `.data` storage, correct calling convention and stack alignment, direct `printf`/`scanf` where suitable, direct `ExitProcess`, and understandable labels for IF and WHILE.

`MasmX64NativeGeneration.cs` owns this ordinary learner-facing path, including checked Integer arithmetic through direct x64 instructions and compact failure labels. `MasmX64CodeGenerator.cs` retains the proven compatibility lowering only for currently approved String cases the concise CRT path cannot yet represent safely, including exact source-authored embedded-NUL Strings. That fallback is not the default and must not be expanded to cover ordinary programs merely for historical parity.

### Other Active Targets

JavaScript, Java, COBOL, Objective-C, Swift, Python, and C++ consume the same bound tree and preserve normal destination control flow. Prefer `console.log`, `System.out.println`, `DISPLAY`, `printf`/normal Objective-C Strings, `print`, `print`, and `std::cout` respectively, together with each language's conventional variables, input, conditions, and loops.

Custom helpers are exceptional. A helper must be required by a current approved language rule, small, target-local, and clearer than the available native alternative.

## Expression Intent And Control Flow

The shared bound tree preserves expression intent:

- raw PRINT text remains text;
- `{expression}` remains interpolation;
- `$"..."` remains explicit interpolation;
- explicit concatenation remains concatenation when natural;
- IF remains target-native conditional structure;
- WHILE remains genuine target-native pre-test control flow;
- direct variable reads use current target storage;
- identifier mapping remains symbol based and collision safe.

Target-local lowering may combine operations only when ordinary behavior remains safe and the generated result becomes clearer. It must not create hidden context-sensitive SMILE rules. In particular, PRINT retains its newline even when INPUT follows; a future no-newline form requires an explicit language feature.

## Desktop And Process Architecture

`SMILE.Engine` has no WPF dependency. `SMILE.Toolchains` owns detection, temporary workspaces, compiler/runtime invocation, standard-input modes, timeouts, cancellation, bounded output, and process-tree termination.

Toolchain detection, generation, compilation, linking, execution, process output, and long file operations remain asynchronous and must not block the WPF dispatcher.

The Desktop keeps three visible generated panes defaulted to C#, MASM x64, and C. Each pane remains an independent editable build unit with its own selected language, edit revision, divergence marker, generated cache relationship, and Build & Run operation. Every pane selector exposes all ten active targets.

Startup completes first paint before loading the packaged cumulative `language.smile` reference. Startup and debounced live generation target only visible active languages. Older results must not overwrite later source changes, language changes, New, explicit Transpile All, or later target-pane edits according to the existing revision rules.

Recoverable toolchain, process, folder, command-refresh, and logging failures remain contained and visible without closing the IDE.

Generated workspaces remain isolated under:

```text
%TEMP%\SMILE\Runs\<unique-id> - <language>\
```

## Validation Architecture

SMILE is in Velocity Mode. Routine validation uses the smallest focused tests and build that cover the changed subsystem.

Fast `MissionGuardrail` tests inspect learner-facing source without requiring every toolchain. The reset-reference checks remain detailed for C#, C, and MASM, while focused source and conformance tests protect the other active generators.

Broader Debug/Release and ten-toolchain integration validation belongs to major milestones, release candidates, broad architecture changes, and explicit requests. Routine work remains focused on the targets it changes.

The hosted `SMILE CI` workflow retains its complete Windows Debug/Release job but is manually invoked through `workflow_dispatch` during Velocity Mode. Automatic push/pull-request triggers and the exact-SHA post-push completion gate are suspended.

## Ten-Target Architecture History

Before the Strategic Reset, SMILE maintained ten simultaneous backends and extensive exact cross-target runtime conformance. The reset temporarily focused three targets, then all ten were reactivated on 2026-08-10 through the same central policy.

Historical descriptions of strict UTF-8 input, shared byte limits, exact NUL-capable console storage, generic runtime error dispatch, and mandatory matrices still describe superseded architecture. They are not current design authority; all active generators follow the permanent native beginner-first rule and may retain documented target-native differences.

## Architectural Decision Rule

When the architecture offers a choice between a normal native target construct and a generalized compiler/runtime subsystem, use the native path when it satisfies current approved SMILE semantics.

KISS keeps the system centered on one engine, one toolchain layer, one CLI, one responsive Desktop app, one test project, and learner-readable generated programs.
