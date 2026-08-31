# Migrating to SMILE Core BASIC 2.1

## What changed

SMILE 1.0 now accepts one canonical source language: SMILE Core BASIC 2.1 — Text-Game Foundation. This is a deliberate breaking replacement of the research-era SMILE 1.0 grammar plus an additive extension of the preserved Core BASIC 2.0 subset, not a compatibility layer. The parser never guesses a dialect or retries invalid source through a legacy path.

Programs already written in the canonical Core BASIC 1 subset remain valid because Profile 2 contains that subset. Older research syntax outside that subset must be rewritten.

## Common source rewrites

| Earlier research form | Core BASIC 2 form |
|---|---|
| `LET Score = 10` | `Score = 10` |
| `SET Name = "Sin"` | `Name = "Sin"` |
| raw `PRINT Score={Score}` | `Print "Score="; Score` |
| `$"Score={Score}"` | `"Score=" + Score` is invalid because Text and Number do not convert; use expression-list `Print` |
| `WHILE Ready ... END WHILE` | use a post-tested `Do ... Loop Until` design appropriate to the program |
| `// comment`, `# comment`, or `REM` | `' comment` |
| backslash Text escapes | double an embedded quote: `"She said ""Hi""."` |
| old `INPUT` statement | no replacement in this milestone; console Input is deferred authority-first work |

This compiler reports these old spellings as errors. It does not retain aliases that could make source meaning depend on hidden mode detection.

## Declarations and Option Explicit

Direct assignment still provides the smallest introduction to variables:

```smile
Score = 10
Name = "Sin"
```

Use `Dim` when you want to teach the type or default value explicitly. Put `Option Explicit` first when every variable and `For` counter should require a declaration:

```smile
Option Explicit

Dim Score As Number
Score = 10
Print Score
```

Types are exact. SMILE does not silently convert among `Number`, `Boolean`, and `Text`.

## Routines and ByVal parameters

Move repeated work into a top-level `Sub` and computed work into a top-level `Function`. Parentheses are always required, even when there are no arguments.

```smile
Option Explicit

Call Greet("Alyssa")
Print Double(21)

Sub Greet(ByVal StudentName As Text)
    Print "Hello, "; StudentName
End Sub

Function Double(Value As Number) As Number
    Return Value * 2
End Function
```

`ByVal` may be written or omitted; both spellings copy the argument. Assigning a parameter changes only that invocation. Locals and local arrays are also fresh for every call, including recursive calls.

## Select Case and arrays

Use `Select Case` for one exact typed choice and square brackets for a fixed one- or two-dimensional array:

```smile
Option Explicit

Dim Scores[3] As Number
Dim Choice As Number

Scores[0] = 90
Choice = 1

Select Case Choice
    Case 0
        Print Scores[0]
    Case 1
        Print "Second choice"
    Case Else
        Print "Unknown"
End Select
```

Indexes start at zero. A dynamic invalid index fails with `SMILER1210` before the destination accesses memory. Under the pinned SMILE 2.0 behavior, do not place a blank source line between `Select Case ...` and its first `Case`.

Rank two writes its dimensions and indexes in the same order:

```smile
Option Explicit
Const Width = 12
Const Height = 8
Dim Board[Width, Height] As Text
Board[3, 2] = "@"
Print Board[3, 2]
```

## Text-game terminal features

The current language can poll an attached terminal without blocking, clear/redraw it, wait in milliseconds, read a monotonic timer, and choose inclusive random Numbers:

```smile
Option Explicit
Dim KeyCode As Number
Dim Roll As Number
Clear Screen
Get Key KeyCode
Random Roll From 1 To 6
Wait 20 Milliseconds
Print KeyCode; " "; Roll; " at "; Timer()
```

Use `KEY_W`/`KEY_A`/`KEY_S`/`KEY_D`, the four arrow constants, `KEY_ENTER`, `KEY_ESCAPE`, `KEY_SPACE`, `KEY_1` through `KEY_4`, `KEY_TAB`, `KEY_OTHER`, and `KEY_NONE`. This is real-time terminal polling, not blocking console Input and not graphics.

## Features that remain outside this profile

Do not migrate source by inventing SMILE 1.0-only versions of console Input, `ByRef`, Optional or named parameters, dynamic or rank-three arrays, Enum, Type, Module, Class, graphics, files, audio, or other SMILE 2.0 feature families. They require separate explicit profile decisions.

## Check a migrated program

Build the solution, transpile to one target, and then run the focused Core BASIC tests:

```powershell
dotnet build SMILE.sln -c Debug -nologo
dotnet run --project src/SMILE.Cli -- examples/core-basic-2-canonical.smile --target javascript --run
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=CoreBasic -nologo
```

The [Core BASIC 2.1 Text-Game Foundation official specification](SMILE%20Language%20Specification/003%20-%20SMILE%20Core%20BASIC%202.1%20Text-Game%20Foundation%20Official%20Specification.md) is the normative language reference. The [student reference](smile-1-language-reference.html) provides examples and explanations that work offline.
