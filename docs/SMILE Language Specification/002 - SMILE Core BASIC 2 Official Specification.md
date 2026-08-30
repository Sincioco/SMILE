# SMILE Core BASIC 2 Official Specification

## Status and authority

This is the only current SMILE 1.0 language specification. It defines SMILE Core BASIC Profile 2.0, backported from the authoritative SMILE 2.0 repository at commit `9aa9583a651eab452ea3af80772b08b68fc03220`.

SMILE 1.0 has one language, parser, binder, evaluator, and meaning. It has no legacy dialect, selector, source auto-detection, compatibility alias, or fallback parser. Core BASIC Profile 1.0 remains a valid subset of this language; historical SMILE 1.0 research syntax does not.

## Source model

- Keywords and identifiers are case-insensitive.
- A physical newline ends a statement except inside balanced expression parentheses or an open Text literal.
- CRLF, LF, and CR source newlines are accepted. Newlines inside Text normalize to LF.
- An apostrophe starts a comment through the physical line ending.
- A semicolon is used only to separate `Print` expressions or suppress its final newline.
- Identifiers begin with a Unicode letter or underscore and continue with Unicode letters, decimal digits, or underscores.
- All SMILE 2.0 reserved words remain reserved, including words for features outside this profile.

## Values and exact types

| Type | Meaning | Default |
|---|---|---|
| `Number` | signed 64-bit whole number | `0` |
| `Boolean` | `True` or `False` | `False` |
| `Text` | Unicode text | empty Text |

There are no implicit conversions among these types. Decimal digits form a Number literal; unary minus supplies a negative sign. Text uses double quotes and doubles an internal quote:

```smile
Message = "She said ""Hello""."
```

Backslash escapes, interpolation, raw Print templates, block strings, and alternate comment markers are not part of the language.

## Top-level items

A file contains declarations and executable statements. `Option Explicit`, `Const`, `Dim`, `Sub`, and `Function` are top-level declarations. A routine may appear before or after executable statements and does not run merely because control reaches its source position. No source-level `Sub Main()` is required.

Program-level constants, variables, arrays, and routines share one case-insensitive namespace. Routine names cannot be overloaded.

## Variables, Dim, Const, and Option Explicit

Without `Option Explicit`, a first direct assignment at top level creates a global variable whose value fixes its exact type:

```smile
Name = "Sin"
Score = 10
Score = Score + 1
```

`Dim` declares default-initialized storage. `Const` declares a program-level immutable compile-time scalar and may refer to later constants when no cycle results.

```smile
Dim Name As Text
Dim Score As Number
Const Answer = Base + 2
Const Base = 40
```

`Option Explicit` is optional, may occur once, and must be the first nonblank, noncomment item:

```smile
Option Explicit

Dim Score As Number
Score = 100
```

When enabled, every variable and `For` counter must be declared by `Dim` or as a parameter. When omitted, an assignment to an unknown name inside a routine creates a routine-local variable unless a visible global already owns that name. Reading a name before declaration or first assignment is always an error.

## Expressions and evaluation order

From highest to lowest precedence:

| Level | Operators | Rules |
|---|---|---|
| Primary | literals, names, array access, function call, parentheses | yields a scalar |
| Unary | `-`, `Not` | Number to Number; Boolean to Boolean |
| Multiplicative | `*`, `/`, `Mod` | Number to Number |
| Additive | `+`, `-` | Number arithmetic; Text `+` concatenation |
| Relational | `<`, `<=`, `>`, `>=` | Number to Boolean |
| Equality | `=`, `<>` | like-typed scalars to Boolean |
| Conjunction | `And` | Boolean, short-circuiting |
| Disjunction | `Or` | Boolean, short-circuiting |

Division truncates toward zero. `Mod` is `left - (left / right) * right`, so its sign follows the left operand. Division or remainder by zero is a runtime error.

Non-short-circuited binary operands, call arguments, nested calls, array indexes, Select selectors, and For bounds are evaluated exactly once from left to right. `And` and `Or` skip the right operand when their result is already known.

## Print

`Print` writes zero or more expressions. Semicolons add no spaces. A trailing semicolon suppresses the final newline.

```smile
Print
Print "Score: "; Score
Print "loading";
```

Number uses invariant decimal text, Boolean prints as `True` or `False`, and Text writes its exact value.

## If, For, Do, typed exits, and End Program

```smile
If Score >= 90 Then
    Grade = "A"
Else If Score >= 80 Then
    Grade = "B"
Else
    Grade = "C"
End If

For Index = 1 To 3
    Print Index
End For

For Index = 3 Down To 1
    Print Index
End For

Do
    Attempts = Attempts + 1
Loop Until Attempts = 3
```

Every condition is Boolean. For bounds are Number expressions evaluated once. The range is inclusive and steps by one. After normal nonempty completion the counter is one step past the final bound; a zero-iteration loop leaves the start value. `Do` is post-tested and runs at least once.

`Exit For` leaves the nearest lexically containing `For`; `Exit Do` leaves the nearest lexically containing `Do`, even across a nested loop of the other kind. The target must be in the same routine invocation. `End Program` terminates the whole program successfully, including when executed inside a routine.

## Sub, Call, and parameters

```smile
Sub ShowScore(ByVal Score As Number, Caption As Text)
    Print Caption; Score
End Sub

Call ShowScore(125, "Score: ")
```

A Sub is top-level, cannot be nested, has no return type, and is invoked only by `Call Name(...)`. Parentheses are required even for zero arguments. A Sub may use `Return` without a value.

Parameters are required positional typed scalars:

```text
parameter := ["ByVal"] identifier "As" ("Number" | "Boolean" | "Text")
```

Omitting `ByVal` still means ByVal. Argument count and types must match exactly. Each argument is evaluated once from left to right and copied into independent parameter storage. Assigning a parameter does not change the caller. There is no four-argument limit.

## Function and Return

```smile
Function Add(LeftValue As Number, RightValue As Number) As Number
    Return LeftValue + RightValue
End Function

Total = Add(10, 20)
```

A Function declares `As Number`, `As Boolean`, or `As Text`. Its call is a primary expression and requires parentheses. It must return an exactly matching value on every reachable normal path. `Return` without a value is invalid in a Function; a value Return is invalid in a Sub. A Function cannot be invoked through `Call`, and a Sub cannot be used as an expression.

Direct recursion, mutual recursion, and calls to later declarations are valid. Stack exhaustion remains a destination-native failure rather than a small artificial language limit.

## Scope and call frames

Top-level storage is global. Inside a routine, lookup first checks parameters and locals, then globals and constants. Parameters and local variables are fresh per invocation. Locals do not leak into other routines or top-level code.

An explicit local `Dim` may shadow a global from its declaration onward. A same-named use before that later local declaration is an error, not a global reference. Parameters and locals share one routine namespace and cannot be redeclared.

## Select Case

```smile
Select Case Command
    Case "Attack"
        Print "The hero attacks."
    Case "Defend"
        Print "The hero defends."
    Case Else
        Print "Unknown command."
End Select
```

The selector may be Number, Boolean, or Text and is evaluated once. Each ordinary Case is a compile-time scalar of exactly the selector type. Cases are tested in source order; at most the first match runs. `Case Else` is optional, unique, and last. Duplicate values are errors. Text equality is ordinal and case-sensitive.

The pinned SMILE 2.0 implementation requires the first `Case` to follow the `Select Case` header without an intervening blank source item. Blank lines inside later case bodies are allowed. Ranges, comma lists, `Case Is`, fall-through, and `Exit Select` are excluded.

## Fixed one-dimensional arrays

```smile
Const ArrayLength = 4
Dim Scores[ArrayLength] As Number
Dim Names[ArrayLength] As Text
Dim Active[ArrayLength] As Boolean

Scores[0] = 100
Print Scores[0]
```

An array has exactly one dimension and uses square brackets. Its dimension is a positive compile-time Number expression giving the element count. Indexes are zero-based Numbers; size `N` accepts `0` through `N - 1`. Elements default to `0`, `False`, or empty Text.

Arrays may be global or routine-local. Each call receives a freshly defaulted local array. A source-known invalid index is a compile-time error. A dynamic invalid index causes deterministic runtime diagnostic `SMILER1210` before target memory is accessed.

Array names are not scalars. Whole-array assignment, comparison, Print, parameters, returns, resizing, slicing, and multiple dimensions are excluded.

## Grammar summary

```text
program-item       := option-explicit | declaration | routine | statement
option-explicit    := "Option" "Explicit"
declaration        := "Dim" identifier ["[" constant-number-expression "]"]
                      "As" scalar-type
                    | "Const" identifier "=" constant-scalar-expression
routine            := "Sub" identifier "(" [parameter-list] ")" lines "End" "Sub"
                    | "Function" identifier "(" [parameter-list] ")"
                      "As" scalar-type lines "End" "Function"
parameter-list     := parameter {"," parameter}
parameter          := ["ByVal"] identifier "As" scalar-type
statement          := assignment | call | return | print | if | select
                    | for | do | exit | end-program
assignment         := identifier ["[" expression "]"] "=" expression
call               := "Call" identifier "(" [argument-list] ")"
call-expression    := identifier "(" [argument-list] ")"
return             := "Return" [expression]
select             := "Select" "Case" expression line-end
                      {"Case" constant-scalar-expression line-end lines}
                      ["Case" "Else" line-end lines] "End" "Select"
array-access       := identifier "[" expression "]"
scalar-type        := "Number" | "Boolean" | "Text"
```

## Intentional exclusions

Console `Input`, `ByRef`, Optional/ParamArray/named arguments, dynamic or multidimensional arrays, array parameters/returns, Enum, Type, Class, New, Nothing, Property, Module, Import, visibility modifiers, files/data, timing/randomness, graphics, games, media, and audio are not implemented.

Historical `LET`, `SET`, old `INPUT`, `WHILE`, raw Print templates, interpolation, block strings, backslash escapes, and non-apostrophe comment markers are rejected rather than treated as compatibility aliases.
