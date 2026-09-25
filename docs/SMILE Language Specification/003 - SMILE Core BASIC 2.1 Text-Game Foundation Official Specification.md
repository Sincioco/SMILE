# SMILE Core BASIC 2.1 Text-Game Foundation Official Specification

## Status and authority

This is the current complete SMILE 1.0 language specification. It additively extends the preserved [Core BASIC 2.0 subset](002%20-%20SMILE%20Core%20BASIC%202%20Official%20Specification.md) with console operations, rank-two arrays, and the text/routine/Double/file additions below.

The text/routine/Double/file back-port was verified against SMILE 2.0 commit `e97afe296f6866d97ad035c9a9b0c9596b919fe0`. This is an incremental back-port, not a claim of complete non-graphical SMILE 2.0 parity. The [back-port inventory](../SMILE%202%20Core%20Backport%20Progress.md) records the remaining features.

The shared Core BASIC source spelling and meaning were verified against the read-only SMILE 2.0 repository at commit `b34f4c5284f9f636e17a62ce5b6e2721d53be464`. The SMILE 1.0-only `Move Cursor To` and `Text Color` terminal statements were subsequently authorized directly for this profile; they do not claim SMILE 2.0 parity. SMILE 1.0 has one parser, binder, evaluator, and language—2.1 is a milestone label, not a dialect selector.

Every source-model, typing, expression-order, Print, control-flow, routine, scope, Select Case, one-dimensional-array, and `End Program` rule in the 2.0 subset remains in force except where this document additively permits the features described below. `Move`, `Cursor`, `Color`, `Default`, and the eight color names are now reserved words; this research project has no external compatibility obligation, so an older experiment that used one as an identifier must rename it.

## Fixed one- and two-dimensional arrays

Enum element types are also supported, with the same bounds, location and
routine-local lifetime rules below; their default value is nominal zero.

```smile
Const Width = 12
Const Height = 8

Dim Board[Width, Height] As Number
Dim Visible[Width, Height] As Boolean
Dim Glyphs[Width, Height] As Text

Board[X, Y] = 1
Print Board[X, Y]
```

- An array has rank one or rank two.
- Each dimension is a positive compile-time Number expression and is an element count.
- Each dimension and the checked product of both dimensions must fit the compiler's `Int32`-sized managed storage model (at most 2,147,483,647 elements); practical game boards should be far smaller.
- Indexes are zero-based Number expressions. Their count must exactly match the declared rank.
- Authored index order is preserved. The games conventionally use `[X, Y]`, with the first dimension horizontal and the second vertical.
- Index expressions evaluate left to right and exactly once. For an assignment, all indexes and their bounds checks occur before the right-hand value is evaluated.
- A constant out-of-range index is a compile-time diagnostic. A dynamic invalid index fails with `SMILER1210` before storage is touched.
- Global and routine-local Number, Double, Boolean, and Text arrays are supported. Each routine invocation, including recursive calls, receives fresh local arrays defaulted to `0`, `0.0`, `False`, or empty Text.
- Whole-array values, assignment, comparison, Print, parameters, returns, resizing, dynamic dimensions, and rank greater than two remain invalid.

Grammar:

```text
array-dim    := "Dim" identifier "[" constant-number-expression
                ["," constant-number-expression] "]" "As" scalar-type
array-access := identifier "[" expression ["," expression] "]"
```

Square brackets do not create a multiline expression-continuation context.

## Named key constants

The following built-in Number constants use the authoritative stable values:

| Constant | Value | Constant | Value |
|---|---:|---|---:|
| `KEY_NONE` | 0 | `KEY_UP` | 10 |
| `KEY_W` | 1 | `KEY_DOWN` | 11 |
| `KEY_A` | 2 | `KEY_LEFT` | 12 |
| `KEY_S` | 3 | `KEY_RIGHT` | 13 |
| `KEY_D` | 4 | `KEY_ENTER` | 14 |
| `KEY_ESCAPE` | 15 | `KEY_SPACE` | 16 |
| `KEY_1` | 17 | `KEY_2` | 18 |
| `KEY_OTHER` | 19 | `KEY_3` | 20 |
| `KEY_TAB` | 21 | `KEY_4` | 22 |
| `KEY_O` | 27 | `KEY_F` | 28 |
| `KEY_G` | 29 | `KEY_R` | 30 |
| `KEY_P` | 31 | `KEY_B` | 32 |
| `KEY_CONTROL` | 33 | `KEY_BACKTICK` | 34 |
| `KEY_X` | 35 | `KEY_Y` | 36 |
| `KEY_Z` | 37 | `KEY_E` | 38 |
| `KEY_PLUS` | 39 | `KEY_MINUS` | 40 |
| `KEY_C` | 41 | | |

Letters accept either case. Backtick includes the shifted tilde key; Plus
includes `+`/`=`, and Minus includes `-`/`_`. These character events work on all
ten targets. `KEY_CONTROL` reserves the authority's value, but standalone Control
events are not currently supplied by the target character-stream readers.
Adding native modifier-only event handling remains a back-port gap.

Pad-only and pointer constants are outside this console profile.

## Get Key

```smile
Get Key PressedKey
```

`PressedKey` is a writable Number variable under the ordinary `Option Explicit` and scope rules. The statement polls without waiting, consumes at most one pending event, never requires Enter, never echoes movement input, and stores `KEY_NONE` when no event is available or no interactive terminal is attached.

Uppercase and lowercase W/A/S/D normalize identically. Arrow events, Enter, Escape, Space, digits 1–4, and Tab map to their named constants. Any otherwise ordinary event maps to `KEY_OTHER`. A complete ANSI arrow sequence is one event; a standalone Escape is `KEY_ESCAPE`.

## Clear Screen

```smile
Clear Screen
```

In an attached interactive terminal, this erases the visible console and moves the cursor to the home position without launching a child process. This deliberate SMILE 1.0 behavior prevents text from an earlier, wider frame or instruction screen from remaining beside a later frame. Output needed before the clear is flushed. When output is redirected, the statement is a safe no-op and emits no terminal-control bytes. Destination-native console facilities may differ in how they retain terminal scrollback.

## Move Cursor To

```smile
Move Cursor To Column, Row
```

`Column` and `Row` are Number expressions evaluated left to right and exactly once. Coordinates are 1-based and use the modern X-then-Y order: column first, then row. Values below 1 act like 1. Attached terminals clip or reject positions beyond their available buffer according to the closest normal destination facility; programs should use positions that fit their intended console. The statement writes no visible character and does not erase the screen. It is a safe no-op with redirected output.

Moving to `1, 1` is the normal way for a text game to overwrite an existing frame without exposing a blank screen between frames.

## Text Color

```smile
Text Color Yellow, Black
Text Color Default
```

The two-color form selects the foreground and background for subsequent terminal output. Both names are required and must be one of `Black`, `Red`, `Green`, `Yellow`, `Blue`, `Magenta`, `Cyan`, or `White`. `Text Color Default` restores the terminal's normal color. Exact shades are destination-native and may differ, but each target preserves the named distinction as closely as its ordinary console supports.

Color statements write no visible character. They are safe no-ops with redirected output and emit no terminal-control bytes there. A program that changes color must use `Text Color Default` before leaving or handing control back to a launcher.

## Wait

```smile
Wait Duration Milliseconds
```

The Number duration is evaluated exactly once. A positive value pauses for approximately that many milliseconds using a normal non-busy-wait destination facility. Zero returns promptly. In alignment with current SMILE 2.0 runtime behavior, a negative value is treated as zero and a value above `4,294,967,295` is clamped to `4,294,967,295`. The evaluator advances injected virtual monotonic time immediately rather than sleeping.

## Random

```smile
Random Result From LowerBound To UpperBound
```

`Result` is a writable Number variable. Bounds evaluate left to right exactly once. The stored whole Number is inclusive: `LowerBound <= Result <= UpperBound`. One random source is initialized per process; normal runs are not required to share sequences across targets. The evaluator accepts a deterministic injected source.

When the lower bound is greater than the upper bound, the lower bound is stored without consuming randomness. Equal bounds always produce that value. Implementations must not silently swap bounds or reseed per statement.

## Timer, Abs, Min, and Max

```smile
Elapsed = Timer()
Distance = Abs(PlayerX - EnemyX)
Clamped = Min(Max(Value, Lower), Upper)
```

- `Timer()` takes no arguments and returns monotonic elapsed milliseconds as Number. Its epoch is unspecified and it does not move backward within a process.
- `Abs(Number)` returns Number. The evaluator reports signed-minimum overflow as `SMILER1206`; generated targets retain the documented destination-native extreme-overflow policy rather than adding a general checked-arithmetic runtime.
- `Min(Number, Number)` and `Max(Number, Number)` return Number.
- Built-in arguments evaluate left to right and exactly once.
- A destination intrinsic or standard-library operation is preferred when it preserves these rules.

## Console lifecycle and evaluator host

Programs that use interactive terminal features initialize only the required console state. Any changed input, echo, or raw terminal mode is restored on normal completion, `End Program`, controlled SMILE runtime failure, and normal target exception/error cleanup paths. Source that selects a text color restores it explicitly with `Text Color Default`. Redirected execution does not attempt raw-mode setup.

The evaluator exposes an injectable host for one-event key polling, clear/top-left frame capture, cursor moves, color changes, virtual Wait, monotonic time, inclusive Random, and an execution budget. Existing ordinary evaluator callers use a safe default host. Exceeding the configured statement budget fails deterministically rather than hanging automated tests.

## Generation contract

Every target lowers the bound operations to normal destination facilities. Helpers/imports are emitted only when used and contain terminal/runtime mechanics only—not game rules.

For C#, C, MASM x64, Java, COBOL, Objective-C, and C++, the main or primary program is the first executable body, followed by user routines and then compiler helpers. Required imports, data, fields, external declarations, and prototypes may precede main. Node.js uses a dependency-free async main only when asynchronous console behavior requires it; Wait uses a Promise and never blocks the event loop. Python remains a direct module-level script without a synthetic main guard.

## Grammar additions

```text
statement       := existing-statement | get-key | clear-screen | move-cursor
                 | text-color | wait | random | load-text-file
load-text-file  := "Load" "Text" "File" expression "Into" identifier "Count" identifier
get-key         := "Get" "Key" identifier
clear-screen    := "Clear" "Screen"
move-cursor     := "Move" "Cursor" "To" expression "," expression
text-color      := "Text" "Color" color-name "," color-name
                 | "Text" "Color" "Default"
color-name      := "Black" | "Red" | "Green" | "Yellow"
                 | "Blue" | "Magenta" | "Cyan" | "White"
wait            := "Wait" expression "Milliseconds"
random          := "Random" identifier "From" expression "To" expression
builtin-call    := "Timer" "(" ")"
                 | "Abs" "(" expression ")"
                 | ("Min" | "Max") "(" expression "," expression ")"
```

## Deliberate exclusions

This milestone does not add blocking `Input`, `Key_Held`, pointer/mouse input, cursor visibility/shape control, arbitrary terminal escape strings, graphics or `Game Window`, sound, unrestricted file writing, dynamic arrays, more than two dimensions, whole-array parameters or returns, variadic parameters, threads in SMILE source, or an eleventh target. Historical LET/SET assignment, INPUT/WHILE/interpolation/block-string syntax remains rejected. Blocking Input is also absent from current SMILE 2.0. The later back-port sections below add bounded file reads, integer/Data persistence, Enum, Type value records, Class references, methods/properties, With, modules, projects and libraries.

## Unicode text inspection

```smile
Print Text_Length("A😀B")
Print Text_Code_At("A😀B", 1)
Print Text_Slice("A😀B", 1, 1)
```

The output is `3`, `128512`, and `😀`, on separate lines. Indexes and counts use Unicode scalar values, not UTF-8 bytes, UTF-16 units, or grapheme clusters. A combining accent is a separate scalar. All explicit arguments evaluate once, left to right.

- `Text_Length(Text)` returns a Number scalar count.
- `Text_Code_At(Text, Number)` returns a scalar value, or `-1` for a negative or out-of-range index.
- `Text_Slice(Text, Number, Number)` returns Text beginning at the zero-based start. A negative start, nonpositive count, or start beyond the end returns empty Text. A large count stops at the end without overflow.
- These functions accept positional arguments only and are not permitted in Const initializers, matching the current authority.
- Existing target Text storage limits remain: COBOL stores at most 4096 UTF-8 bytes, while C/Objective-C/MASM use null-terminated UTF-8. No normalization or grapheme grouping is performed.

## Optional parameters, named arguments, and multiline declarations

Required parameters accept `[ByVal | ByRef] Name As Type`, where Type is Number,
Double, Boolean, or Text. Omitted mode means ByVal. A ByRef argument must be an
exact-type writable variable, array element, record field, or parameter; constants,
literal values, computations, routine results, and whole arrays are invalid.
Writing a ByRef parameter immediately changes its caller location. Two
parameters may share that location, and forwarding preserves the alias. ByVal
parameters remain independent copies even if the callee passes its copy ByRef.

Each explicit argument is captured once in source order before declaration-order
placement. ByRef captures a location rather than its current value. For an array
cell, evaluate and check each index before the next dimension or later argument;
a failed bounds check stops the call without later effects and retains earlier
output. Scalars, fixed array cells, whole records and writable record fields are
supported. `SMILE2165` diagnoses non-writable ByRef
arguments; normal exact-type diagnostics also apply. `Optional ByRef` is invalid.

```smile
Call Greet()
Call Greet(Caption:="Welcome", Name:="Sin")

Sub Greet(
    Optional Name As Text = "student",
    Optional ByVal Caption As Text = "Hello"
)
    Print Caption; ", "; Name
End Sub
```

Optional parameters are ByVal, have an explicit scalar type and default, and follow all required parameters. A default is an exact-type literal or Const; parentheses and a directly negated numeric literal are allowed. Computed defaults such as `1 + 2` must first be named by a Const. Required parameters cannot have defaults.

Calls use `ParameterName:=Expression`. Names are case-insensitive. Positional arguments precede all named arguments. Unknown names, duplicate arguments, missing required values, and mismatched types are errors. Explicit values are captured once in source order before placing them in declaration order; omitted defaults are then supplied. No extra routine is generated to implement this behavior.

Balanced declaration parentheses permit newlines between parameter tokens and commas. The opening parenthesis stays on the Sub/Function declaration line; a Function's `As Type` stays on the same line as the closing parenthesis. Square brackets still do not imply continuation. A routine must still declare explicit types; legacy untyped SMILE 2.0 declarations are outside this profile.

Unary `+` is accepted on Number or Double with the same precedence as unary `-` and leaves its value unchanged.

## Double values and math

Double is a distinct eight-byte IEEE binary64 type. Number retains signed 64-bit
integer semantics on every SMILE 1.0 target. There is no implicit conversion
between them in assignment, arithmetic, comparison, parameters, or returns.

```smile
Dim Speed As Double
Speed = ToDouble(3) / 2.0
Print Speed
Print ToNumber(-3.9)
Print Sqrt(9.0); ":"; Round(2.5)
Print Text_From_Double(-0.0)
```

This prints `1.5`, `-3`, `3.0:2.0`, and `-0.0`. Decimal or exponent literals
such as `0.125`, `1e-3`, and `2.5E2` are Double. Digits are required on both
sides of a decimal point and after an exponent sign: `.5`, `1.`, `1e+`, and
nonfinite literals are invalid. Plain integer literals remain Number. Double is
a contextual type name; existing identifiers such as `Function Double(...)`
remain valid. User routines take precedence over the new numeric intrinsic names.

Double supports same-type `+`, `-`, `*`, `/`, unary signs, and exact comparisons.
Equality adds no tolerance. Signed zeros compare equal but retain their signs
through storage, calls, rounding, and text conversion. The default is positive
`0.0`; finite subnormals and underflow to signed zero are allowed. Variables,
constants, fixed arrays, routine parameters/returns, Optional defaults, named
arguments, Print, and Select Case accept Double. Duplicate zero Case values are
rejected. Mod, loop controls, array dimensions, and indexes remain Number-only.

| Function | Contract |
|---|---|
| `ToDouble(Number)` | Nearest binary64 value; large integers may lose precision |
| `ToNumber(Double)` | Truncate toward zero, then require `[-2^63, 2^63)` |
| `Abs(Value)`, `Min(First, Second)`, `Max(First, Second)` | Same-type Number or Double; preserve the result type |
| `Clamp(Value, Minimum, Maximum)` | Double; reject inverted bounds |
| `Sqrt(Value)` | Double; reject negative input |
| `Sin(Value)`, `Cos(Value)`, `Atan2(Y, X)` | Double; angles in radians |
| `Floor(Value)`, `Ceiling(Value)`, `Truncate(Value)` | Double result; floor, ceiling, or truncation |
| `Round(Value)` | Double result; nearest value, ties to even |
| `Text_From_Double(Value)` | Invariant Text with enough precision for a numeric round trip |
| `Text_To_Double(Text)` | Complete invariant decimal/exponent text to Double |

Min/Max return the first argument on a tie, including signed zero. Clamp keeps
the original value within inclusive bounds. Atan2 honors signed-zero quadrants;
rounding preserves the mathematical sign of a zero result. Intrinsics accept
positional arguments evaluated once in source order. Numeric intrinsic calls
are permitted in Const expressions; Optional defaults still require a literal
or a previously declared Const rather than an inline computation.

Text parsing accepts surrounding ASCII space and U+0009–U+000D, an optional
sign, leading digits, an optional fraction, and an optional exponent. It rejects
partial input, hexadecimal, separators, NaN, and Infinity. Formatting preserves
`-0.0`; exponent spelling and transcendental rounding may differ between native
standard libraries. Binary floating values are approximate, not decimal accounting.

Malformed/nonfinite literals use `SMILE3900`; mixed numeric expression and
intrinsic argument errors use `SMILE3901`. Existing assignment/parameter typing
diagnostics retain their own codes. Invalid domains, zero divisors, conversion
overflow, and nonfinite results use `SMILE3902` during constant checking or
`SMILER3902` at runtime with the actual source line. Failure happens before the
destination is changed; earlier source-order effects remain.

Targets use native binary64 storage, operators, and math APIs with focused checks
where their normal behavior permits infinity or different conversions. MASM uses
REAL8, SSE, and native floating argument/return registers; a C companion supplies
required numeric conversion/checking services. GnuCOBOL's FLOAT-LONG storage is
binary64, but its ordinary numeric literals, COMPUTE, and comparisons pass
through decimal representations. The generated program therefore uses standard
C interoperability for exact literals/arithmetic/comparisons and normal
FLOAT-LONG-to-FLOAT-LONG MOVE for storage copies. No numeric interpreter, new
dependency, or source-language runtime framework is introduced.

## Load Text File

```smile
Dim Bytes[64] As Number
Dim ByteCount As Number
Load Text File "lesson.txt" Into Bytes Count ByteCount
Print ByteCount
```

The path is a Text expression evaluated exactly once before the array changes.
A known empty/whitespace-only path or wrong path type reports `SMILE3027`.
The destination is a declared rank-one Number array; Count is a writable Number
scalar under ordinary scope and Option Explicit rules. A runtime empty/invalid
path safely returns zero.

The operation clears the entire array, reads raw UTF-8 bytes as Number values
0–255, skips one initial UTF-8 BOM (EF BB BF), and copies at most the array
capacity. It does not decode or validate the file's contents. Count receives the
copied length; remaining cells stay zero. Missing, inaccessible, empty, or
unreadable files return Count zero with a zeroed destination. No partial data is
retained after an I/O failure.

Both slash styles are accepted. Repeated separators and `.` collapse, and
contained `..` segments normalize. Rooted/drive/UNC/URI paths, NUL, and traversal
above the program directory are rejected at runtime. The normalized relative
path must be nonempty, below 4096 UTF-8 bytes, and contain at most 512 segments.
The native C-family/MASM/COBOL path buffer also limits the complete path to 2047
UTF-16 units, matching the authority. Existing null-terminated Text limitations
still apply to C, Objective-C, and MASM path expressions.

Files resolve relative to the executable directory, the Node/Python script
directory, or Java's generated class directory. Loose generated programs require
manual file placement there, as loose-file SMILE 2.0 builds do. SMILE 1.0 does not
yet implement multi-file project manifests or automatic asset publication.
Desktop/CLI Build & Run uses a fresh generated-program directory each run; it
does not infer or copy arbitrary neighboring source files.

Targets use standard stream/file APIs, with small helpers for normalization,
BOM handling, bounded copying, and recoverable I/O. Node.js uses asynchronous
file handles and propagates await through calling routines. MASM adds a small
`SmileFileRuntime.c` companion; COBOL uses its existing C-interoperability
companion. The evaluator's `SmileEvaluationOptions.Files` accepts an
`ISmileFileHost`; its default reads beneath `AppContext.BaseDirectory` through
`SmileDirectoryFileHost`. The reader owns/disposes the returned stream.

## Integer persistence

```smile
Dim Best As Number
Load Best From "best-score" Default 0
Best = Max(Best, 100)
Save Best To "best-score"
```

Load requires a writable Number scalar (including a ByRef parameter) under the
ordinary scope/Option Explicit rules. Save accepts a Number variable or constant.
The key is a nonempty, non-whitespace Text literal; malformed storage keys report
`SMILE3025`. Default is an exact Number expression, evaluated once before I/O even
when a saved value exists. Array cells and computed keys are not integer-storage
operands; byte Data storage has the separate contract below.

Load reads at most the first 63 file bytes. After trimming ASCII space, tab, CR,
and LF, the entire bounded result must be an optionally signed ASCII decimal
integer in the signed-64 range. Empty, malformed, overflowing, missing, or
unreadable storage returns the evaluated default. Save writes invariant decimal
ASCII with no BOM/newline, replacing the existing file; I/O failures are ignored.
This legacy integer operation does not provide backup recovery or atomic writes.

Storage is `%LOCALAPPDATA%\SMILE\Games\<program>\<key>.txt`. Program and key names
retain ASCII letters/digits, underscore and hyphen; other UTF-16 units become
underscores. Each name stops at NUL or 255 units, with an empty name becoming `_`.
Windows case-insensitivity and sanitization can therefore cause name collisions.
Unavailable LocalAppData produces the same recoverable load/save behavior.

CLI/Desktop embed the source filename stem, keeping saves stable across all ten
targets and temporary build directories. Save As under a new source name selects
a new directory. Direct `SmileTranspiler.Transpile`/`TranspileMany` callers may
supply `programName`; its default is `Program`. Evaluator callers may inject
`SmilePersistentStorage(programName, storageRoot)` through
`SmileEvaluationOptions.Storage`. The optional root replaces LocalAppData.
SMILE 1.0 intentionally has a separate product storage namespace from SMILE 2.0;
it does not automatically migrate that product's executable-named integer saves.

## Nominal enums

```smile
Enum Direction
    None
    Up = 10
    Down
    Left = -1
    Right = -1
End Enum
Const StartingDirection = Direction.Left
Dim Heading As Direction
Heading = StartingDirection
Print Heading = Direction.Right
```

- Declare a nonempty Enum directly at program level. Type/member names are
  case-insensitive. The type shares the program declaration namespace; members
  belong to that type and are accessed with a dot. Contextual member names such
  as None, Up, Down, Left, Right, Key, Text and Double follow SMILE 2.0.
- The first implicit member is zero; each subsequent implicit member is the
  preceding value plus one. Values are checked signed 64-bit integers. Explicit
  expressions may use Number literals, forward Number Const references,
  parentheses, unary minus, `+`, `-`, `*`, `/`, Mod, Abs, Min and Max. Overflow,
  zero division and circular constants are errors. Enum members, unary plus,
  Double and runtime expressions are not permitted as initializer operands.
- Duplicate member names are errors; duplicate values are aliases. Every Enum
  is a distinct nominal type, even if its member names and values match another.
- Variables and array cells initialize to zero of their declared Enum, including
  when no named member has value zero. Enum Const values, inferred assignment,
  fixed rank-one/rank-two arrays, ByVal/ByRef parameters, Optional member/Const
  defaults and Function returns preserve exact nominal identity.
- Only `=` and `<>` between the same Enum are permitted. Print, arithmetic,
  ordering, implicit/explicit numeric conversions, array indexes and loop
  controls do not accept Enum values.
- Select Case accepts exact-type compile-time Enum values. Cases that name two
  aliases of the same value are duplicates and therefore invalid.

Generation uses C# long enums, C++ scoped int64 enums, Objective-C fixed-int64
enums, Java/Swift/Python native enums, JavaScript frozen BigInt objects, MASM EQU
constants and COBOL level-78 constants with native scalar storage. C17 uses a
normal enum for int-sized members, otherwise an int64 typedef plus named integer
macros because standard C17 enumerators cannot represent all signed-64 values.
Java/Swift aliases refer to a canonical member; Java documents numeric values in
comments because the language exposes no underlying integer enum type. Java,
Swift and Python add a named internal zero case only when the declaration lacks
one. These are native declarations, with no enum runtime helper.

## Data persistence

```smile
Dim Bytes[8] As Number
Dim ByteCount As Number
Dim State As Number
Bytes[0] = 72
Bytes[1] = 105
Save Data Bytes Count 2 To "greeting" Status State
Load Data "greeting" Into Bytes Count ByteCount Status State
Print ByteCount; ":"; State
```

Save requires a fixed rank-one Number array, a Number count expression and a Text
key expression. Load requires a Text key, a fixed rank-one Number array and a
writable Number Count location. Optional `Status` names a writable Number location;
it is contextual, so `Dim Status As Number` remains valid. Output locations may be
scalars, ByRef parameters or array cells. Ordinary scope/Option Explicit rules
apply. Type/rank mismatches report `SMILE3506`.

Save evaluates Count then Key exactly once, before inspecting array contents.
Load evaluates Key once before changing the array. Both perform I/O before
evaluating Status's location; Load writes Count first, then evaluates/writes
Status. Shared Count/Status locations therefore end with the status value.

`DATA_BLOCK_MAX_BYTES` is 1048576. Array capacity and saved count must not exceed
that bound; count must be nonnegative and no greater than capacity. The first
Count values must be integers 0–255; unused array cells are irrelevant. Empty
blocks and empty keys are valid. Keys use their exact UTF-8 bytes, up to 1 MiB;
overlong keys make storage unavailable. No path characters or integer-key
sanitization affect Data identity. Existing C/Objective-C/MASM null-terminated
Text and COBOL fixed-Text limits still apply to their key expressions.

| Constant | Value | Meaning |
|---|---:|---|
| DATA_STATUS_OK | 0 | Operation succeeded |
| DATA_STATUS_MISSING | 1 | No primary or usable backup exists |
| DATA_STATUS_RECOVERED | 2 | Loaded a valid backup |
| DATA_STATUS_INVALID | 3 | Invalid capacity, count, or byte value |
| DATA_STATUS_UNAVAILABLE | 4 | Storage/path/I/O unavailable |
| DATA_STATUS_CORRUPT | 5 | Invalid envelope, length, version, or checksum |
| DATA_STATUS_TOO_LARGE | 6 | Valid stored payload exceeds destination capacity |

With Status, load failures return Count zero and leave the destination unchanged.
Success overwrites exactly Count cells, preserving the remaining cells. A missing
or corrupt primary permits `.bak` recovery; other failures do not. A successful
backup gives RECOVERED without repairing the primary. A missing backup retains
the original primary status; another backup failure replaces it.

Without Status, Load clears the entire valid destination first, does not recover
backups, and returns zero for a missing primary. Other failures stop with a visible
stderr message and exit 2 in generated programs. Strict Save likewise stops on
any failure. The evaluator reports `SMILER3506`. Earlier output survives failures.

Files are `%LOCALAPPDATA%\SMILE\Games\<identity-hash>\Data\<key-hash>.bin`, where
both hashes are lowercase SHA-256 of exact UTF-8 text. Loose-program identity uses
the source filename stem supplied by CLI/Desktop (`Program` for unsaved/direct
API programs). Projects use ApplicationId, falling back to OutputName. The namespace
is separate from SMILE 2.0; no automatic save migration occurs.

The file envelope is 44 bytes plus payload: ASCII `SMD4`, little-endian uint32
version 1, little-endian uint32 payload length, and the payload's 32-byte SHA-256.
Validation checks the exact file length and digest before writing any array cell.
Save writes and flushes a fresh exclusive temporary file, validates an existing
primary and atomically replaces it while retaining that valid primary as `.bak`.
A checked save can replace a corrupt primary only after validating its backup;
the corrupt primary never overwrites that backup. Strict save rejects it.
Failed operations remove only their owned temporary files.

Node.js's built-in API lacks Windows replace-with-backup. It publishes a flushed
verified backup and then the primary using two atomic renames; a failure between
them may leave the backup equal to the unchanged valid primary. Other targets use
Windows atomic replacement directly. None of these operations provides a
multi-process transaction lock. Standard crypto/file APIs and focused helpers
implement this contract without a persistence framework or third-party package.

## Type value records

```smile
Type ScoreEntry
    Player As Text
    Points As Number
End Type
Dim Current As ScoreEntry
Dim Saved As ScoreEntry
Current.Player = "Sin"
Current.Points = 10
Saved = Current
Current.Points = 20
Print Saved.Player; ":"; Saved.Points
```

- Declare a nonempty Type at program level. Names are case-insensitive and each
  declaration introduces a distinct nominal type. Fields use `Name As Type` or
  `Name[constant-size, constant-size] As Type`; one or two fixed dimensions are
  supported. Field types may be Number, Double, Boolean, Text, Enum or another Type.
- Nested records and arrays initialize each leaf independently to its normal
  default. Forward type references are supported; recursive value layouts and
  layouts/record arrays exceeding signed-32-bit storage size are errors.
- Dot selects a field; brackets index fixed-array fields or arrays of records.
  Each index is evaluated and checked before later indexes and call arguments.
  Out-of-bounds access reports SMILER1210 and does not continue the operation.
- Assignment and ByVal/return copy the complete record value, including all
  nested arrays and Text values. Exact types must match. An earlier ByVal
  argument is copied before later arguments run.
- ByRef accepts records and writable scalar, nested-record or fixed-array field
  cells. The location remains valid when another alias assigns the containing
  record. A field of a temporary function result is readable but not writable.
  Data Count/Status also accept writable Number field locations after I/O.
- Whole-record Print, comparisons, arithmetic, conversions, Const, Optional
  defaults and Select Case are not supported. Use the corresponding fields.
- Class reference fields are unsupported; reference objects are specified below.

Generation uses native structs/aggregates and copy semantics wherever available.
Java/JavaScript/Python and array-bearing C# records need copy support to preserve
value semantics and stable aliases. Text storage limits of individual targets
remain unchanged, including COBOL's fixed 4096-byte Text capacity.

## With record locations

`With location` ... `End With` captures a writable record variable, array cell or
nested field. Every index is evaluated and checked once on block entry. A leading
dot, such as `.Points` or `.Position.X`, uses the nearest enclosing With receiver.
Nested With blocks can themselves use leading-dot targets. Whole-record replacement
preserves the selected storage location, including when a later argument or the
right side of an assignment replaces that record.

Return, End Program and typed loop exits retain their ordinary behavior inside
With. A temporary returned record is not writable and cannot be a With target.
Scalar targets and leading-dot access outside a valid With block are errors.
An invalid nested target does not fall back to the outer receiver. Member calls and
properties also accept leading-dot receivers. Class-reference With is specified below.

## Type methods and properties

A Type may contain Sub/Function declarations and `Property name As type` blocks.
Fields, methods and properties share one case-insensitive member namespace.
Methods/properties are Public by default; explicit Public and Private are allowed.
Fields are always Public. Private access is confined to the declaring Type,
including access to another instance of that same Type.

Methods use `Call receiver.SubName(arguments)` or `receiver.FunctionName(arguments)`.
Optional defaults and named arguments retain ordinary exact typing and authored
evaluation order. The receiver is captured before method arguments; it must be a
writable record location. A method-only Type is valid.

`Me` denotes borrowed current-instance storage inside methods/accessors. Its fields
are writable; Me itself cannot be assigned or passed ByRef. Me can be read/copied,
passed ByVal, returned, used as a member receiver or selected by With.

Properties contain Get and/or Set blocks, each closed with End Get/End Set, and
end with End Property. Get must return the property's exact type on every normal
path. Set receives an implicit, mutable ByVal `Value` of that type and has Sub
return rules. Receiver and Value are outside the authored parameter list. A property
assignment evaluates/copies its right-hand value before capturing the receiver.
Get/Set may call routines and perform ordinary statements, including file reads.
Read-only and write-only access violations are errors. A property value is not
writable storage and cannot be passed ByRef or used as a writable record receiver.

Property/method aggregate values use the same independent copies and stable field
locations as ordinary record routines. Native target member constructs are used
where available; aliasing Swift receivers and asynchronous JavaScript accessors
require ordinary explicit-receiver or getter/setter methods as documented in the
generation standard.

## Class reference objects

`Class Name` ... `End Class` declares an exact nominal reference type at program
level. Empty classes are valid. Fields have the same scalar, Enum, Type and fixed
array shapes as Type fields, but are Private by default; explicit Public exposes
them. Methods and properties are Public by default. Private access is confined to
the declaring class. Class-valued fields and arrays of class references are errors.

`New Name(arguments)` creates a distinct instance. `Dim Item As New Name(arguments)`
declares and initializes a reference; `Dim Item As Name` defaults to Nothing.
There is at most one constructor, spelled `Sub New`; it must be Public and follows
ordinary ByVal/ByRef, Optional and named-argument rules. An absent constructor takes
no arguments. Each instance initializes its own fields before its constructor runs.
Allocation is not a constant expression.

Class assignment, ByVal and return copy the reference. ByRef aliases the caller's
reference slot. Nothing may be assigned/returned/passed where an exact Class type
is expected; it cannot infer a variable type or serve as an Optional default.
`Is` and `Is Not` compare references of the same Class type, including Nothing.
Two Nothing literals have no Class type and cannot be compared on their own.
Ordinary equality, arithmetic, conversion, Print and Select do not accept classes.

Me refers to the current instance and can escape through a return or assignment;
Me itself cannot be assigned or passed ByRef. Fields of temporary class expressions
are writable because their instance owns the storage. Borrowed field locations
remain valid when the original reference variable is rebound. Record-valued fields
still copy by value. Class-valued properties and routine parameters/results are
allowed even though Class-valued fields are not.

With captures a Class reference once, including a newly constructed or returned
instance. Dereferencing Nothing, including entry to With, fails before member
arguments or field indexes are evaluated. Property assignment retains value-first
evaluation before receiver capture and validation. The evaluator reports SMILER3457;
generated targets use their native failure mechanism and preserve prior output.

## Modules, projects and libraries

The current core back-port includes `Module`, `Import ... As`, module-level
Public/Private declarations, qualified values/routines/types and multi-file source
inputs. A physical module source contains exactly one Module; modules can span
sources within one provider. Declarations default to Private. Imports and Option
Explicit belong to the physical source. Values and nominal types occupy distinct
namespaces, and enum member selection resolves the type namespace. Public API
signatures cannot leak private types. Import aliases cannot collide with explicit
or implicit application globals. Routine locals can shadow aliases. Modules cannot
read consuming-program globals. Import cycles are errors.

Dependencies initialize before importers; module/support initializers precede the
selected startup source. Non-module support sources cannot contain executable
top-level statements. Project and loose-source paths accompany diagnostics.

`.smileproj` application inputs, `.smilelibproj` library projects, exact direct
provider references, ApplicationId, runtime Asset includes and deterministic
source-owned `.smilelib` format 7 are implemented. The
[Modules and Projects contract](../Modules%20and%20Projects.md) specifies project
selection, visibility, package validation/resource limits, asset publication and
identity fallback. Library packages are built through the CLI; Desktop opens
application projects and edits their startup source asynchronously.
