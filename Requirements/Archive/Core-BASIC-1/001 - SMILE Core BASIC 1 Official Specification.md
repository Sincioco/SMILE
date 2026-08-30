# SMILE Core BASIC 1 Official Specification

## Status and authority

This is the only current SMILE 1.0 language specification. It defines the frozen Core BASIC 1 profile taken from the authoritative SMILE 2.0 repository at commit `ec61dfa6324de7b22ea5ca0959828ff40e5e3902`.

SMILE 1.0 has one language and one front end. It has no compatibility dialect, selector, automatic detection, or fallback parser. Source outside this specification is rejected.

## Source model

- Keywords and identifiers are case-insensitive.
- A physical newline ends a statement except while an expression is inside parentheses or while a Text literal remains open.
- CRLF, LF, and CR source newlines are accepted. Newlines inside Text values normalize to LF.
- Spaces, tabs, form feed, and vertical tab are horizontal whitespace.
- An apostrophe starts a comment that continues to the physical line ending. Comments may occupy a full line or follow a statement.
- A semicolon is not a statement separator. It is used only in a `Print` value list.

Identifiers begin with a Unicode letter or underscore and continue with Unicode letters, decimal digits, or underscores. All words reserved by SMILE 2.0 remain reserved even when their larger-language feature is outside Core BASIC 1.

## Values and types

Core BASIC has three scalar types:

| Type | Meaning | Default value |
|---|---|---|
| `Number` | signed 64-bit whole number | `0` |
| `Boolean` | `True` or `False` | `False` |
| `Text` | Unicode text | empty Text |

Types are exact. Core BASIC performs no implicit conversion among Number, Boolean, and Text.

Unsigned decimal digits form a Number literal. Unary minus supplies a negative sign. A literal must fit the signed 64-bit range after parsing.

Text begins and ends with a double quote. Two adjacent double quotes inside Text represent one quote:

```smile
Message = "She said ""Hello""."
```

Backslash escape sequences and interpolation syntax are not part of Core BASIC 1.

## Variables and constants

All names have program-wide, case-insensitive identity.

A direct assignment creates an implicit variable on its first assignment. Its initializer fixes its type. Later assignments must use that same type.

```smile
Name = "Sin"
Score = 10
Ready = True
Score = Score + 1
```

`Dim` explicitly declares a variable and supplies its default value:

```smile
Dim Name As Text
Dim Score As Number
Dim Ready As Boolean
```

`Const` declares an immutable, compile-time scalar. Constants are program-level declarations, may refer to other constants declared later, and must not form cycles:

```smile
Const Answer = Base + 2
Const Base = 40
```

A variable or constant must be declared or assigned before an ordinary expression reads it. A constant cannot be assigned, and a `For` counter must be a writable Number.

## Expressions

From highest to lowest binding strength:

| Level | Operators | Operand/result rules |
|---|---|---|
| Unary | `-`, `Not` | Number to Number; Boolean to Boolean |
| Multiplicative | `*`, `/`, `Mod` | Number to Number |
| Additive | `+`, `-` | Number arithmetic; `+` also concatenates Text with Text |
| Relational | `<`, `<=`, `>`, `>=` | Number to Boolean |
| Equality | `=`, `<>` | like-typed scalar values to Boolean |
| Conjunction | `And` | Boolean to Boolean, short-circuiting |
| Disjunction | `Or` | Boolean to Boolean, short-circuiting |

Parentheses override precedence. Division truncates toward zero. `Mod` is defined by `left - (left / right) * right`, so its sign follows the left operand. Division or remainder by zero is a runtime error. Arithmetic is signed 64-bit in the language model; destinations use their nearest normal scalar representation as described by the target standard.

## Print

`Print` writes zero or more expressions. Semicolons separate values without adding spaces. A trailing semicolon suppresses the statement's final newline.

```smile
Print
Print "Hello "; Name
Print "loading";
Print "."
```

Number uses invariant decimal text. Boolean prints as uppercase `TRUE` or `FALSE`. Text prints exactly its value.

## If

```smile
If Score >= 90 Then
    Grade = "A"
Else If Score >= 80 Then
    Grade = "B"
Else
    Grade = "C"
End If
```

Every condition must be Boolean. The first true clause runs; otherwise the optional `Else` body runs.

## For

```smile
For I = 1 To 3
    Print I
End For

For I = 3 Down To 1
    Print I
End For
```

The lower and upper bounds are Number expressions evaluated once on loop entry. `To` increments by one and includes the upper bound. `Down To` decrements by one and includes the upper bound. A loop executes zero times when its initial bound ordering cannot reach the final bound.

The counter is assigned the lower/start value before the first range test. After normal nonempty completion it contains one step past the final bound; after a zero-iteration loop it still contains the start value. `Exit For` leaves the current counter value unchanged.

`Exit For` transfers control out of the nearest lexically containing `For`, including when a `Do` is nested between the statement and that `For`.

## Do

```smile
Do
    Attempts = Attempts + 1
Loop Until Attempts = 3
```

`Do` is post-tested and therefore executes its body at least once. `Loop` without `Until` repeats indefinitely. An `Until` expression must be Boolean. `Exit Do` transfers control out of the nearest lexically containing `Do`, including across nested `For` loops.

## End Program

`End Program` terminates successful program execution immediately. Statements later in the source remain valid source but are not executed after control reaches `End Program`.

## Canonical grammar summary

```text
statement     := assignment | dim | const | print | if | for | do | exit | end-program
assignment    := identifier "=" expression
dim           := "Dim" identifier "As" ("Number" | "Boolean" | "Text")
const         := "Const" identifier "=" expression
print         := "Print" [expression {";" expression} [";"] | ";"]
if            := "If" expression "Then" lines
                 {"Else" "If" expression "Then" lines}
                 ["Else" lines] "End" "If"
for           := "For" identifier "=" expression ["Down"] "To" expression
                 lines "End" "For"
do            := "Do" lines "Loop" ["Until" expression]
exit          := "Exit" ("For" | "Do")
end-program   := "End" "Program"
```

## Deliberate exclusions

Core BASIC 1 excludes SMILE 2.0 modules, procedures, functions, arrays, file/data facilities, graphics, audio, input APIs, timers, user-defined types, classes, enums, properties, imports, and `Option Explicit`. Their reserved words remain unavailable as identifiers.

Earlier SMILE 1.0 statement forms, raw Print templates, interpolated strings, block strings, backslash escapes, and non-apostrophe comment markers are unsupported and rejected. See [Migrating to Core BASIC 1](../Migrating%20to%20Core%20BASIC%201.md) for explicit rewrite examples.
