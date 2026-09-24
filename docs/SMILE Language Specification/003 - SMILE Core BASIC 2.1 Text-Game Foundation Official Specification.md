# SMILE Core BASIC 2.1 Text-Game Foundation Official Specification

## Status and authority

This is the current complete SMILE 1.0 language specification. It additively extends the preserved [Core BASIC 2.0 subset](002%20-%20SMILE%20Core%20BASIC%202%20Official%20Specification.md) with console operations, rank-two arrays, and the text/routine additions below.

The text/routine back-port was verified against SMILE 2.0 commit `e97afe296f6866d97ad035c9a9b0c9596b919fe0`. This is an incremental back-port, not a claim of complete non-graphical SMILE 2.0 parity. The [back-port inventory](../SMILE%202%20Core%20Backport%20Progress.md) records the remaining features.

The shared Core BASIC source spelling and meaning were verified against the read-only SMILE 2.0 repository at commit `b34f4c5284f9f636e17a62ce5b6e2721d53be464`. The SMILE 1.0-only `Move Cursor To` and `Text Color` terminal statements were subsequently authorized directly for this profile; they do not claim SMILE 2.0 parity. SMILE 1.0 has one parser, binder, evaluator, and language—2.1 is a milestone label, not a dialect selector.

Every source-model, typing, expression-order, Print, control-flow, routine, scope, Select Case, one-dimensional-array, and `End Program` rule in the 2.0 subset remains in force except where this document additively permits a second array dimension and the new terminal statements. `Move`, `Cursor`, `Color`, `Default`, and the eight color names are now reserved words; this research project has no external compatibility obligation, so an older experiment that used one as an identifier must rename it.

## Fixed one- and two-dimensional arrays

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
- Global and routine-local Number, Boolean, and Text arrays are supported. Each routine invocation, including recursive calls, receives fresh local arrays defaulted to `0`, `False`, or empty Text.
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
                 | text-color | wait | random
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

This milestone does not add blocking `Input`, `Key_Held`, pointer/mouse input, cursor visibility/shape control, arbitrary terminal escape strings, graphics or `Game Window`, sound, files, dynamic arrays, more than two dimensions, array parameters or returns, `ByRef`, variadic parameters, Double, records, enums, classes, modules, imports, threads in SMILE source, or an eleventh target. Historical LET/SET/INPUT/WHILE/interpolation/block-string syntax remains rejected. Blocking Input is also absent from current SMILE 2.0.

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

Unary `+` is accepted on Number with the same precedence as unary `-` and leaves its value unchanged.
