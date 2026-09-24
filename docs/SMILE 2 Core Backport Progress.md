# SMILE 2.0 non-graphical back-port inventory

Authority inspected read-only: `D:\SMILE 2.0`, commit
`e97afe296f6866d97ad035c9a9b0c9596b919fe0`. The authority repository was clean
before inspection and after the unchanged fixture compilation/execution.

The overall requested back-port is **not complete**. This file records the
completed text/routine milestone and the remaining source-language gaps.

## Implemented in this milestone

| Feature | SMILE 1.0 result |
|---|---|
| `Text_Length`, `Text_Code_At`, `Text_Slice` | Unicode scalar semantics in evaluation and all ten targets |
| Optional parameters | Exact scalar literal/Const defaults; explicit ByVal supported |
| Named arguments | `Name:=Value`; single evaluation in authored order before parameter placement |
| Multiline routine declarations | Balanced declaration parentheses; explicit parameter and return types |
| Unary Number `+` | Identity operation with unary precedence |
| Unicode console output | UTF-8 for C#, Java, Python when source contains non-ASCII Text |

The UTF-8 output fix addresses failures observed during actual generated-program
execution on Windows. Ordinary ASCII programs retain their minimal output.

## Remaining non-graphical gaps

| Area | Implemented SMILE 2.0 features still absent from SMILE 1.0 |
|---|---|
| Fractional arithmetic | `Double` literals/storage/operators; checked explicit conversions; polymorphic Abs/Min/Max; Clamp, Sqrt, Sin, Cos, Atan2, Floor, Ceiling, Truncate, Round; Text_From_Double/Text_To_Double |
| Writable arguments | Exact-type `ByRef`, including scalar variables, checked array cells, and subsequently record locations |
| Console keys | New named keyboard constants, including letters O/F/G/R/P/B/X/Y/Z/E/C, Control, Backtick, Plus, Minus, with target-appropriate event mapping |
| Files and persistence | `Load`/`Save` integer values; UTF-8 `Load Text File`; byte Data save/load and recoverable Status; associated constants and application storage identity |
| Value types | Nominal Enum declarations/members and Type records, nested fields, fixed-array fields, deep copies, and exact nominal typing |
| Object members | Type methods/properties, Class references/constructors, Me, New, Nothing, Is/Is Not, With blocks, member visibility and ownership |
| Program organization | Module/Import, qualified names, visibility, multi-file source/project inputs, deterministic source-owned libraries |

Records, classes, and modules require broader storage, binding, generation, and
project-system work than the completed scalar/routine milestone. The scope
question about including those facilities remains unanswered in the conversation.
Do not reinterpret their presence in this inventory as implementation.

## Outside the console/core back-port

Graphics, Game Window, pointer input, rendering, images, audio and media are
graphical/runtime facilities. `Key_Held` and `Key_Event_Held` require Game Window
in the authority's semantic analyzer, so they are not console primitives.
GUI file pickers and shell-reveal operations should be scoped separately from
ordinary text-file/persistence operations.

Blocking `Input`, While, dynamic arrays, variadic parameters, and rank-three
arrays are not newly implemented SMILE 2.0 features to import. Legacy untyped
routine/array forms are older compatibility forms, not additions to the canonical
explicitly typed SMILE 1.0 subset.

## Ownership and validation

- `Binder.RoutineArguments.cs` validates defaults and binds names to parameter
  slots. The bound tree retains source-order expressions and the parameter map.
- `RoutineArguments.cs` applies the map after evaluation/capture; it is a compiler
  utility, not emitted runtime machinery.
- `TextIntrinsics.cs` owns evaluator/static-analysis scalar traversal.
- `CoreBasicTextInspectionWriter.cs` owns structured-target text lowering;
  `NativeTextInspection.cs` owns the UTF-8 traversal needed by C-family/MASM/COBOL.
- Slices reuse existing C/Objective-C/MASM root tracking. COBOL keeps its existing
  4096-byte storage and explicit logical lengths. No third-party dependency or
  source-language graphics feature was added.

Actual validation includes the new evaluator/diagnostic tests, both fixtures
built and run across all ten targets, MissionGuardrail, existing core conformance
and terminal coverage, and unchanged fixtures built/run with the authoritative
SMILE 2.0 compiler. Generated binaries and parity scratch files belong in ignored
`out/backport-validation` and `out/backport-parity`.

No repository file-size/complexity checker or reviewed no-growth baseline was
found in SMILE 1.0. Existing broad writers received focused wiring; new text and
argument algorithms have their own owners. No guardrail limit or exclusion was
changed. Ordinary target-native Number overflow and existing native Text storage
limitations remain; full Double/object/module parity is pending.
