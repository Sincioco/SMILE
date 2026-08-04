# SMILE — SET Statement Official Specification v1.0

## Status

This document is the complete official language specification for the SMILE `SET` statement.

It is intended for:

> **SMILE v0.5.0 — Runtime Variables, SET, and SET Block String Literals**

The SET-only multiline String form defined here is officially named:

> **SET Block String Literal — The SMILE Way**

This specification works together with:

- `SMILE - LET Statement Official Specification v1.0.md`
- `SMILE - PRINT Statement Official Specification v1.0.md`
- `SMILE - String Literals Official Specification v1.0.md`
- `SMILE - Core Types and Expressions Official Specification v1.0.md`

When this specification is implemented, the official LET specification must be updated to reflect this language model:

```text
LET declares a variable and gives it its initial value.
SET changes the current value of an existing variable.
The variable's SMILE type never changes.
```

---

# 1. Purpose

`SET` changes the value stored in a variable that was previously declared with `LET`.

Example:

```smile
LET Counter = 0
SET Counter = Counter + 1
PRINT {Counter}
```

Output:

```text
1
```

`LET` introduces the variable.

`SET` updates it.

---

# 2. Basic syntax

```text
SET variable-name = set-value
```

Examples:

```smile
SET Counter = Counter + 1
SET Name = "Louiery"
SET Ready = TRUE
```

Formal grammar:

```text
set-statement ->
    SET hspace+ identifier hspace* '=' hspace* set-value

set-value ->
    expression
    | set-block-string-literal

hspace ->
    space
    | tab
```

At least one space or tab is required after the `SET` keyword.

---

# 3. SET is a statement, not an expression

`SET` may appear only where a statement is allowed.

Valid:

```smile
SET Counter = 10
```

Invalid:

```smile
LET Result = SET Counter = 10
```

Invalid:

```smile
PRINT {SET Counter = 10}
```

SMILE does not have an assignment expression.

The equals sign inside an ordinary expression remains the equality operator.

Example:

```smile
LET Same = Left = Right
```

The second `=` compares `Left` and `Right`.

It does not assign.

---

# 4. LET and SET have different jobs

## LET declares

```smile
LET Counter = 0
```

This:

1. creates the variable `Counter`;
2. determines its permanent SMILE type;
3. evaluates its initializer;
4. stores its initial value.

## SET changes

```smile
SET Counter = Counter + 1
```

This:

1. finds the existing variable `Counter`;
2. evaluates the complete SET value;
3. verifies that the value has the variable's existing type;
4. replaces the variable's current value.

`SET` never declares a variable.

---

# 5. The target variable must already exist

Valid:

```smile
LET Counter = 0
SET Counter = 1
```

Invalid:

```smile
SET Counter = 1
```

because `Counter` has not been declared.

Also invalid:

```smile
SET Counter = 1
LET Counter = 0
```

A variable becomes available only after an earlier successful `LET` declaration.

A later declaration cannot make an earlier `SET` valid.

---

# 6. Variable names remain case-insensitive

SMILE variable lookup is ordinal case-insensitive.

These refer to the same variable:

```smile
LET Counter = 0
SET counter = 1
SET COUNTER = 2
PRINT {CoUnTeR}
```

Output:

```text
2
```

A destination generator must use one consistent mapped spelling for the declaration, all assignments, and all references.

---

# 7. SET does not change a variable's type

The variable's type is determined by its `LET` initializer.

Example:

```smile
LET Age = 49
```

`Age` is an `Integer`.

Valid:

```smile
SET Age = Age + 1
```

Invalid:

```smile
SET Age = "Fifty"
```

SMILE does not implicitly convert between:

- `String`;
- `Integer`;
- `Boolean`.

The SET value must have exactly the target variable's existing type.

---

# 8. Supported types

`SET` supports all current SMILE value types.

## String

```smile
LET Name = "Sin"
SET Name = "Louiery"
PRINT {Name}
```

Output:

```text
Louiery
```

## Integer

```smile
LET Counter = 0
SET Counter = Counter + 1
PRINT {Counter}
```

Output:

```text
1
```

## Boolean

```smile
LET Ready = FALSE
SET Ready = TRUE
PRINT {Ready}
```

Output:

```text
TRUE
```

---

# 9. The old value is visible on the right side

The right-hand expression is evaluated before the target variable changes.

Example:

```smile
LET Counter = 5
SET Counter = Counter + 1
PRINT {Counter}
```

The right-hand `Counter` is the old value `5`.

The assignment then stores `6`.

Output:

```text
6
```

---

# 10. Assignment is atomic

A `SET` statement follows this conceptual order:

```text
1. Evaluate the complete SET value.
2. Confirm that evaluation succeeded.
3. Replace the variable's current value.
```

The target variable is not changed while its new value is still being evaluated.

If evaluation fails, the previous value remains unchanged.

In SMILE v0.5.0 there is still no runtime input, branching, looping, function call, or external runtime data, so current arithmetic errors remain detectable before destination generation.

---

# 11. Sequential execution

Statements execute from top to bottom.

```smile
LET Counter = 0
PRINT {Counter}

SET Counter = Counter + 1
PRINT {Counter}

SET Counter = Counter + 2
PRINT {Counter}
```

Output:

```text
0
1
3
```

Every later statement sees the current value produced by earlier statements.

---

# 12. Later LET statements see updated values

```smile
LET A = 1
SET A = 10
LET B = A + 5

PRINT {B}
```

Output:

```text
15
```

`B` is initialized from the current value of `A`, not its original value.

---

# 13. PRINT sees the current value

```smile
LET Name = "Sin"
PRINT {Name}

SET Name = "Louiery"
PRINT {Name}
```

Output:

```text
Sin
Louiery
```

This applies to:

- direct evaluated `PRINT`;
- raw-template holes;
- interpolated String holes;
- expressions inside holes.

Example:

```smile
LET Score = 10
SET Score = 25

PRINT Score={Score}
LET Message = $"Final score: {Score}"
PRINT {Message}
```

Output:

```text
Score=25
Final score: 25
```

---

# 14. SET may be repeated

```smile
LET Value = 1
SET Value = 2
SET Value = 3
SET Value = 4

PRINT {Value}
```

Output:

```text
4
```

There is no assignment-count limit.

---

# 15. Self-assignment is valid

```smile
LET Name = "Sin"
SET Name = Name
PRINT {Name}
```

Output:

```text
Sin
```

This is a valid no-op assignment.

---

# 16. Ordinary SET expressions

The normal right side of SET may be any existing valid SMILE expression whose type matches the target variable.

## String literal

```smile
SET Name = "Louiery"
```

## String copy

```smile
SET CurrentName = OtherName
```

## String concatenation

```smile
SET FullName = FirstName + " " + LastName
```

## One-line String interpolation

```smile
SET Message = $"Hello {Name}. Score={Score}"
```

## Integer expression

```smile
SET Counter = Counter + 1
```

## Boolean expression

```smile
SET Adult = Age >= 18
```

Ordinary quoted String expressions remain one-line.

The block form defined below is a separate SET-only source form.

---

# 17. SET Block String Literal — The SMILE Way

## 17.1 Purpose

A SET Block String Literal lets a learner assign a multiline String without a special multi-character delimiter.

Example:

```smile
LET Name = ""

SET Name ="
S
 I
  N
"

PRINT {Name}
```

Output:

```text
S
 I
  N
```

The value begins with the physical line after the opening delimiter and ends with the physical line before the closing delimiter.

The opening and closing delimiter lines are not part of the value.

---

# 18. Opening delimiter

A SET Block String Literal begins when:

1. the parser is reading the complete right-hand value of a `SET` statement;
2. the next token is an ordinary double quote;
3. that quote is the final non-whitespace character on the physical SET line.

Both forms are valid:

```smile
SET Name ="
```

```smile
SET Name = "
```

Spaces or tabs may appear after the opening quote before the physical newline.

They are structural and do not become String data.

This is not a block opening:

```smile
SET Name = "Sin"
```

That remains an ordinary one-line String literal.

This is invalid:

```smile
SET Name ="Sin
```

A block String begins only when the opening quote ends the SET line.

---

# 19. Closing delimiter

The closing delimiter is a physical line whose only non-whitespace character is one ordinary double quote.

Valid closing lines:

```text
"
```

```text
    "
```

```text
	"
```

The spaces or tabs before the closing quote define the block's structural indentation margin.

Spaces or tabs after the closing quote are allowed.

No other non-whitespace content may appear on the closing delimiter line.

Invalid:

```text
" + Suffix
```

Invalid:

```text
" PRINT {Name}
```

---

# 20. Content boundaries

Only the physical lines between the opening and closing delimiter lines belong to the String.

Given:

```smile
SET Name ="
S
 I
  N
"
```

The content lines are:

```text
S
 I
  N
```

The resulting String is conceptually:

```text
S\n I\n  N
```

There is:

- no automatic newline before `S`;
- one newline between adjacent content lines;
- no automatic newline after `N`.

---

# 21. Newline normalization

Each physical line boundary between content lines becomes one logical line-feed character:

```text
\n
```

This rule is independent of the source file's physical line-ending convention.

These physical line endings all normalize to logical `\n` inside the block value:

- Windows `CRLF`;
- Unix `LF`;
- legacy standalone `CR`, if accepted by the existing source reader.

This keeps the resulting SMILE String identical across platforms and all ten destinations.

---

# 22. Structural indentation margin

The spaces or tabs before the closing delimiter quote define the structural indentation margin.

That exact leading whitespace sequence is removed from each content line when that content line begins with the same sequence.

Any additional indentation remains part of the String.

Example:

```smile
    LET Name = ""

    SET Name ="
    S
     I
      N
    "
```

The closing delimiter has four leading spaces.

Four leading spaces are removed from each content line.

The resulting value is:

```text
S
 I
  N
```

This rule allows the SET statement to be indented naturally inside future control-flow, loop, function, or scope syntax without forcing structural code indentation into the String value.

---

# 23. Content lines that do not contain the margin

If a content line does not begin with the exact closing-delimiter margin, that line is preserved exactly.

The compiler does not guess a different margin for that line.

Example:

```smile
    SET Message ="
  Left
    Right
    "
```

The closing delimiter margin is four spaces.

The first content line begins with only two spaces, so it remains:

```text
  Left
```

The second begins with four spaces, so those four are removed:

```text
Right
```

Result:

```text
  Left
Right
```

This behavior is deterministic and avoids silently deleting learner-authored indentation.

---

# 24. Top-level blocks preserve content indentation

At top level, the closing delimiter normally has no indentation:

```smile
SET Name ="
S
 I
  N
"
```

The structural margin is empty.

Therefore all content indentation is preserved exactly.

Output:

```text
S
 I
  N
```

---

# 25. Blank content lines

Blank content lines create real blank lines in the String.

Example:

```smile
SET Message ="
First line

Third line
"
```

Resulting value:

```text
First line\n\nThird line
```

Output:

```text
First line

Third line
```

A content line containing spaces or tabs is not automatically empty data.

The structural margin is removed when present, and any remaining spaces or tabs are preserved.

---

# 26. Empty block String

A block with no content lines produces an empty String.

```smile
LET Message = "Before"

SET Message ="
"

PRINT {Message}
```

The opening delimiter line is followed immediately by the closing delimiter line.

The resulting value is:

```text
""
```

`PRINT` therefore emits only its normal newline.

---

# 27. No automatic trailing newline

The physical newline immediately before the closing delimiter is structural.

It is not automatically included in the String.

Example:

```smile
SET Message ="
Hello
"
```

Result:

```text
Hello
```

not:

```text
Hello\n
```

---

# 28. Intentional trailing newline

To make the String end with one newline, include one empty content line before the closing delimiter.

```smile
SET Message ="
Hello

"
```

The content lines are:

```text
Hello
<empty line>
```

Joining the content lines produces:

```text
Hello\n
```

To end with two newlines, include two empty content lines before the closing delimiter.

---

# 29. Leading newline

To make the String begin with a newline, include an empty first content line.

```smile
SET Message ="

Hello
"
```

The content lines are:

```text
<empty line>
Hello
```

Result:

```text
\nHello
```

---

# 30. Content indentation is meaningful

At top level:

```smile
SET Name ="
S
 I
  N
"
```

the spaces before `I` and `N` are part of the String.

Output:

```text
S
 I
  N
```

Inside structurally indented code, the closing-delimiter margin is removed first, and any indentation beyond that margin remains meaningful.

---

# 31. Trailing spaces and tabs are meaningful

Spaces and tabs at the end of content lines are part of the String.

Example:

```smile
SET Message ="
Hello 
World
"
```

The space after `Hello` is preserved.

Resulting value:

```text
Hello \nWorld
```

Source tooling must not silently trim trailing spaces or tabs.

---

# 32. Quotes inside block content

An ordinary double quote inside a content line is part of the String unless the entire physical line, apart from whitespace, forms the closing delimiter.

Example:

```smile
SET Message ="
He said "Hello".
"
```

Result:

```text
He said "Hello".
```

To create a content line containing only one double quote, use the official escaped quote:

```smile
SET Message ="
\"
"
```

That content line is not mistaken for the closing delimiter.

Result:

```text
"
```

---

# 33. Escape sequences

All official SMILE String escapes remain valid inside a SET Block String Literal:

```text
\\
\"
\n
\r
\t
\0
\b
\f
```

Example:

```smile
SET Message ="
First\nSecond
Path: C:\\SMILE
"
```

The explicit `\n` creates an additional logical newline character inside the first content line.

The physical newline between the two content lines also becomes one logical `\n`.

Escape decoding happens after:

1. content-line extraction;
2. structural-margin removal;
3. physical newline normalization.

---

# 34. Backslash at the end of a content line

A backslash immediately followed by a physical source newline is not a line-continuation operator.

Example:

```smile
SET Message ="
Hello\
World
"
```

The backslash is followed by the block's logical newline and does not form a valid official escape.

The existing invalid or unterminated String escape diagnostic applies.

---

# 35. Embedded NUL

Embedded NUL remains valid.

```smile
SET Data ="
A\0B
"
```

Resulting bytes:

```text
41 00 42
```

Every destination target must preserve the complete value and exact byte sequence.

---

# 36. Block Strings do not interpolate

A SET Block String Literal is an ordinary String value, not an interpolated String.

Example:

```smile
LET Name = "Sin"
LET Message = ""

SET Message ="
Hello {Name}
"

PRINT {Message}
```

Output:

```text
Hello {Name}
```

The braces are literal characters.

The `$"` interpolated String form remains one-line in v0.5.0.

This is invalid:

```smile
SET Message =$"
Hello {Name}
"
```

---

# 37. SET-only placement

A SET Block String Literal is valid only as the complete value of `SET`.

Valid:

```smile
LET Name = ""

SET Name ="
S
 I
  N
"
```

Invalid in LET:

```smile
LET Name ="
S
 I
  N
"
```

Invalid in PRINT:

```smile
PRINT "
S
 I
 N
"
```

Invalid in a future function argument:

```smile
CALL Something("
S
 I
 N
")
```

All non-SET contexts continue using conventional one-line String syntax unless a future official specification explicitly adds another form.

---

# 38. Complete SET value only

The block literal must be the entire SET value.

Valid:

```smile
SET Name ="
S
 I
  N
"
```

Invalid concatenation:

```smile
SET Name ="
S
 I
  N
" + Suffix
```

Invalid prefix expression:

```smile
SET Name = Prefix + "
S
 I
 N
"
```

Invalid parentheses:

```smile
SET Name = ("
S
 I
 N
")
```

Invalid additional statement content:

```smile
SET Name ="
S
 I
 N
" PRINT {Name}
```

After the closing delimiter quote, only spaces or tabs may appear before the physical newline or end-of-file.

---

# 39. One logical statement across physical lines

Ordinary SMILE statements normally occupy one physical line.

A SET Block String Literal creates one logical SET statement spanning multiple physical source lines.

The internal physical newlines belong to the block token or block syntax and do not terminate the logical SET statement.

The newline after the closing delimiter terminates the SET statement normally.

No other statement may begin before the block closes.

---

# 40. Unterminated block String

This is invalid:

```smile
SET Name ="
S
 I
 N
```

It produces:

```text
SMILE1003
```

The diagnostic span begins at the opening quote and extends through the point where termination becomes impossible, normally end-of-file.

---

# 41. Source text must remain exact

Trailing spaces and tabs can be meaningful block content.

The SMILE editor, CLI file reader, save pipeline, examples, tests, and any future formatter must not silently trim source lines.

A future optional “show whitespace” editor feature may help learners see invisible data, but it is not required for v0.5.0.

---

# 42. No target-specific block parsing

The block behavior belongs entirely to the SMILE front end.

The front end must:

```text
recognize the opening delimiter
find the closing delimiter
extract content lines
remove the structural margin
normalize physical newlines to \n
decode official escapes
produce one ordinary String value
```

The binder and all destination generators receive the same canonical String value used by ordinary one-line String expressions.

Target generators must not:

- scan source text;
- find delimiter lines;
- remove indentation;
- normalize block newlines;
- implement block-specific parsing.

---

# 43. Canonical semantic representation

The canonical bound statement is:

```text
BoundSetStatement
```

Conceptually:

```csharp
public sealed record BoundSetStatement(
    VariableSymbol Variable,
    BoundExpression Value)
    : BoundStatement;
```

A SET Block String Literal binds to:

```text
BoundStringLiteralExpression
```

containing the complete normalized String value.

A dedicated token or syntax node may exist in the front end to enforce source placement, but the bound program must not contain a target-specific block String representation.

---

# 44. SET is a reserved keyword

`SET` is case-insensitive and reserved in every casing.

Invalid:

```smile
LET SET = 1
LET set = 1
LET Set = 1
```

Bare PRINT text remains literal:

```smile
PRINT SET
```

Output:

```text
SET
```

Invalid evaluated reference:

```smile
PRINT {SET}
```

because `SET` cannot be a variable name.

---

# 45. Horizontal whitespace around SET

Valid:

```smile
SET Counter = 1
SET Counter=2
SET Counter    =    3
```

At least one space or tab is required after `SET`.

Invalid:

```smile
SETCounter = 1
```

---

# 46. Invalid SET examples

## Missing target

```smile
SET = 1
```

## Missing equals sign

```smile
SET Counter 1
```

## Missing value

```smile
SET Counter =
```

## Undefined target

```smile
SET Counter = 1
```

when no earlier `LET Counter` exists.

## Type mismatch

```smile
LET Counter = 0
SET Counter = "One"
```

## Assignment used as an expression

```smile
PRINT {SET Counter = 1}
```

## Multiple ordinary statements on one line

```smile
SET A = 1; SET B = 2
```

## Block String outside SET

```smile
LET Name ="
S
 I
 N
"
```

## Extra content after block

```smile
SET Name ="
S
 I
 N
" + "!"
```

---

# 47. Diagnostics

SMILE v0.5.0 reserves these SET diagnostics:

| Code | Meaning |
|---|---|
| `SMILE1301` | A variable name is required after `SET` |
| `SMILE1302` | The equals sign is missing after the SET target |
| `SMILE1303` | The SET value is missing |
| `SMILE1304` | The SET target variable is undefined |
| `SMILE1305` | The SET value type does not match the variable's declared type |
| `SMILE1306` | A SET Block String Literal is valid only as the complete value of SET |
| `SMILE1307` | Unexpected non-whitespace content follows the closing block delimiter |
| `SMILE1308` | The opening quote of a SET Block String Literal must end the physical SET line |

Existing lexer and String diagnostics remain applicable:

| Code | Meaning |
|---|---|
| `SMILE1003` | Unterminated String literal or SET Block String Literal |
| `SMILE1208` | Unknown or invalid String escape |
| `SMILE1209` | Unterminated String escape |

---

# 48. Reference execution model

The reference evaluator maintains:

```text
VariableSymbol -> current SmileValue
```

For `LET`:

```text
evaluate initializer
store initial current value
```

For ordinary `SET`:

```text
evaluate right side using current environment
replace target current value
```

For a block String `SET`:

```text
use the already normalized ordinary String value
replace target current value
```

For `PRINT`:

```text
evaluate using current environment
append canonical display text
append newline
```

The evaluator, not a permanent constant stored on `BoundLetStatement`, is the source of truth for current variable state.

---

# 49. Target-generation requirements

Every target generator consumes the shared bound program.

High-level targets preserve natural assignment.

Example:

```smile
SET Counter = Counter + 1
```

C#:

```csharp
Counter = Counter + 1;
```

C++:

```cpp
Counter = Counter + 1;
```

Java:

```java
Counter = Counter + 1;
```

JavaScript:

```javascript
Counter = Counter + 1;
```

Python:

```python
Counter = Counter + 1
```

Swift:

```swift
Counter = Counter + 1
```

C:

```c
Counter = Counter + 1;
```

Objective-C:

```objc
Counter = Counter + 1;
```

A SET Block String Literal generates exactly as its normalized ordinary String value.

No target generator performs block parsing or indentation processing.

---

# 50. Swift declaration rule

A Swift variable assigned by any `SET` statement must be declared with `var`.

```smile
LET Counter = 0
SET Counter = 1
```

Swift:

```swift
var Counter: Int = 0
Counter = 1
```

A variable never assigned after declaration may remain `let`.

---

# 51. Destination-language freeze

`SET` expands SMILE itself.

It does not add another destination language.

SMILE continues to target exactly:

1. C#
2. C
3. Windows x64 MASM Assembly
4. JavaScript
5. Java
6. COBOL
7. Objective-C
8. Swift
9. Python
10. C++

C++ remains the tenth and final planned target unless Sin explicitly reopens destination-language expansion.

---

# 52. Compatibility with LET

The LET specification must be read with these v0.5.0 clarifications:

- `LET` declares a variable once.
- `LET` initializes its first value.
- the variable's type is fixed;
- the variable may later change value through `SET`;
- LET still cannot redeclare an existing variable;
- self-reference in a LET initializer remains invalid;
- failed declarations do not leak symbols;
- declaration-before-use remains mandatory;
- LET String expressions remain one-line;
- SET Block String Literals are not legal LET initializers.

---

# 53. Compatibility with PRINT

PRINT rules do not otherwise change.

```smile
PRINT Name
```

prints literal text:

```text
Name
```

```smile
PRINT {Name}
```

prints the variable's current value.

SET Block String Literals are not legal directly in PRINT.

---

# 54. Compatibility with expressions

The expression grammar does not gain an assignment operator.

Expressions remain pure in v0.5.0.

`SET` is the only assignment statement.

A SET Block String Literal is a SET-only source form, not a general expression feature.

All existing precedence, typing, equality, arithmetic, interpolation, escape, and short-circuit rules remain normative.

---

# 55. Normative ordinary SET acceptance program

```smile
LET Counter = 0
LET Name = "Sin"
LET Ready = FALSE

PRINT Counter={Counter}, Name={Name}, Ready={Ready}

SET Counter = Counter + 1
SET Name = "Louiery"
SET Ready = TRUE

PRINT Counter={Counter}, Name={Name}, Ready={Ready}

SET Counter = Counter + 2
LET Message = $"{Name} finished with {Counter}."
PRINT {Message}
```

Required output:

```text
Counter=0, Name=Sin, Ready=FALSE
Counter=1, Name=Louiery, Ready=TRUE
Louiery finished with 3.
```

---

# 56. Normative SET Block String acceptance program

```smile
LET Name = ""
LET Message = ""

SET Name ="
S
 I
  N
"

SET Message ="
Hello {Name}
This is literal block text.
"

PRINT {Name}
PRINT {Message}
```

Required output:

```text
S
 I
  N
Hello {Name}
This is literal block text.
```

The `{Name}` text is literal because block Strings do not interpolate.

---

# 57. Future evolution

`SET` establishes mutable runtime state.

It prepares SMILE for:

- `IF / THEN / ELSE`;
- `INPUT`;
- loops;
- functions;
- scopes.

The SET Block String Literal remains deliberately SET-only.

A future general-purpose multiline String feature may use another explicitly specified source form. That future feature must not silently change the SET Block String behavior defined here.

Optimizations may use known values only when they preserve statement order, mutation, and left-to-right evaluation.
