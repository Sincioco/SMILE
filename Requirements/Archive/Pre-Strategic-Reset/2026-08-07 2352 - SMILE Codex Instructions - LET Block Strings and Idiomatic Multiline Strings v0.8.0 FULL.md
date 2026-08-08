# Codex Implementation Instructions — LET Block Strings and Idiomatic Multiline Strings
## FULL v0.8.0-Aware Implementation Brief

> [!IMPORTANT]
> **HISTORICAL / PARTIALLY SUPERSEDED**
>
> This document records the original LET/SET Block String implementation milestone. Front-end scanning and normalization remain governed by current official String, LET, and SET specifications. Requirements for routine all-ten-target maintenance, strict all-target validation, or low-level exactness that would override native beginner-readable target code are superseded by `docs/SMILE Core Principles.md` and the current active three-target policy.

---

# 1. Repository and current baseline

Repository:

```text
Sincioco/SMILE
```

Work from the latest `main`.

When this brief was prepared, the latest observed `main` commit was:

```text
fc67661eb67f37c6b96aeef886afac160b0149a2
Sin and Codex: Add WHILE loops
```

That commit is only a reference point. Before changing anything:

1. fetch/read the actual current `main`;
2. read the current `AGENTS.md`;
3. inspect the current specifications, parser, binder, analyzer, evaluator, generators, tests, Desktop highlighting, and examples;
4. treat newer repository code as authoritative if `main` has advanced.

SMILE development currently occurs directly on `main`.

Do not create or recommend a feature branch unless Sin explicitly changes that project rule.

Do not commit or push unless Sin explicitly asks.

Never reset, discard, overwrite, or clean up unrelated user work.

Never commit generated build/output folders or files.

---

# 2. High-level objective

Implement one coherent SMILE language improvement:

1. A variable may be declared and initialized directly from a multiline Block String using `LET`.
2. `SET` continues to support the same Block String form.
3. `LET` and `SET` use one shared Block String scanner and one shared normalization algorithm.
4. The feature is generalized from:

```text
SET Block String Literal — The SMILE Way
```

to:

```text
Block String Literal — The SMILE Way
```

5. Target generators emit multiline String values using the clearest, idiomatic, semantically exact representation available in each of SMILE’s ten target languages.
6. Exact SMILE String semantics always take priority over visual prettiness.
7. The implementation must preserve all current v0.8.0 behavior, including:
   - IF;
   - WHILE;
   - INPUT;
   - comments;
   - blank-line preservation;
   - target editor independence;
   - exact String byte semantics;
   - embedded NUL support;
   - runtime checked arithmetic;
   - loop fixed-point analysis;
   - bounded loop-carried String analysis;
   - mixed IF/WHILE recovery;
   - asynchronous Desktop transpilation.

---

# 3. Beginner-facing language goal

This must become valid SMILE:

```smile
LET MultilineText = "
    Hello World!
    This is SMILE!
        How are you?
"

PRINT {MultilineText}
```

The value of `MultilineText` must be exactly:

```text
    Hello World!\\n    This is SMILE!\\n        How are you?
```

The `\\n` characters shown above describe logical LF characters in the value.

They are not the two source characters backslash and `n`.

Runtime output:

```text
    Hello World!
    This is SMILE!
        How are you?
```

followed by the normal one line ending that `PRINT` appends.

---

# 4. Why LET must support Block Strings

The current workaround is unnecessarily ceremonial for beginners:

```smile
LET MultilineText = ""

SET MultilineText = "
    Hello World!
    This is SMILE!
        How are you?
"
```

That requires the learner to:

1. declare a String with a fake placeholder value;
2. immediately replace that value;
3. learn SET before they can naturally initialize multiline text.

That is not desirable.

`LET` already means:

> Declare a variable, determine its type from the initializer, and store its initial value.

A Block String is simply another String initializer.

The beginner should be able to write the intended initial value at the declaration site.

The preferred form is therefore:

```smile
LET MultilineText = "
    Hello World!
    This is SMILE!
        How are you?
"
```

This improves SMILE's educational consistency without changing the meaning of LET.

---

# 5. Preserve official PRINT semantics

Do not change PRINT behavior as part of this task.

This:

```smile
PRINT MultilineText
```

continues to mean literal raw-template text and prints:

```text
MultilineText
```

This:

```smile
PRINT {MultilineText}
```

evaluates the variable and prints its value.

Therefore, for C#:

```smile
PRINT MultilineText
```

may generate:

```csharp
Console.WriteLine("MultilineText");
```

while:

```smile
PRINT {MultilineText}
```

must generate:

```csharp
Console.WriteLine(MultilineText);
```

Never change one form into the other.

---

# 6. Official language decision

Generalize the language feature from:

```text
SET Block String Literal — The SMILE Way
```

to:

```text
Block String Literal — The SMILE Way
```

The generalized feature is valid only as the complete value of:

```text
LET
SET
```

It does not become a general multiline expression form.

## 6.1 Valid LET

```smile
LET Message = "
First line
Second line
"
```

## 6.2 Valid SET

```smile
LET Message = ""

SET Message = "
First line
Second line
"
```

## 6.3 Invalid PRINT placement

```smile
PRINT "
First line
Second line
"
```

## 6.4 Invalid concatenation placement

```smile
LET Message = "Prefix: " + "
First line
Second line
"
```

## 6.5 Invalid interpolation placement

```smile
LET Message = $"
First line
Second line
"
```

Interpolated quoted Strings remain one-line in this milestone.

## 6.6 Invalid parenthesized/general-expression placement

```smile
LET Message = (
"
First line
Second line
"
)
```

Do not make the Block String a general `primary-expression`.

It is a complete-value source form for LET and SET.

---

# 7. Grammar

Update the conceptual grammar to the equivalent of:

```text
let-statement ->
    LET hspace+ identifier hspace* '=' hspace* let-value

let-value ->
    expression
    | block-string-literal

set-statement ->
    SET hspace+ identifier hspace* '=' hspace* set-value

set-value ->
    expression
    | block-string-literal
```

Do not change ordinary expression grammar merely to accommodate this feature.

Do not allow a Block String inside arbitrary nested expressions.

---

# 8. Type semantics

A Block String Literal always has type:

```text
String
```

Example:

```smile
LET Message = "
Hello
"
```

Conceptually:

```text
Message : String
```

The variable becomes visible only after:

1. the complete Block String is parsed;
2. normalization succeeds;
3. escapes are decoded;
4. binding succeeds;
5. initialization succeeds.

Preserve existing declaration-before-use behavior.

Preserve existing self-reference behavior.

---

# 9. Current scope rules remain in force

SMILE v0.8.0 still does not have formal nested variable scopes.

Therefore LET remains prohibited inside current structured bodies.

The new Block String initializer must not accidentally weaken that rule.

## 9.1 LET inside IF

Still invalid:

```smile
IF Ready = TRUE THEN
    LET Message = "
Hello
"
END IF
```

Use the current IF LET diagnostic:

```text
SMILE1414
```

## 9.2 LET inside WHILE

Still invalid:

```smile
WHILE Count < 10
    LET Message = "
Hello
"
    SET Count = Count + 1
END WHILE
```

Use the current WHILE LET diagnostic:

```text
SMILE1610
```

## 9.3 Lexically inside WHILE

The current v0.8.0 rule is recursive.

If a LET appears anywhere lexically inside a WHILE body, including inside a nested IF or nested WHILE, the WHILE LET prohibition remains applicable according to the current binder rules.

Do not accidentally change diagnostic precedence.

Example:

```smile
WHILE Count < 10
    IF Ready = TRUE THEN
        LET Message = "
Hello
"
    END IF

    SET Count = Count + 1
END WHILE
```

Preserve the current v0.8.0 diagnostic behavior.

## 9.4 Future compatibility

When SMILE eventually introduces formal scopes and later permits LET inside blocks, this same Block String initializer syntax should work naturally wherever LET becomes valid.

Do not design the scanner around "top-level only."

The placement restriction belongs to LET validity, not to Block String scanning.

---

# 10. Block String source semantics

LET and SET must use exactly one scanner and normalization algorithm.

Do not implement:

```text
LetBlockStringScanner
SetBlockStringScanner
```

as separate implementations.

Generalize the current SET-specific implementation.

Preferred names:

```text
SetBlockStringScanner
    ->
BlockStringScanner
```

and:

```text
SetBlockStringScanResult
    ->
BlockStringScanResult
```

Keep existing neutral syntax names where already suitable:

```text
BlockStringLiteralToken
BlockStringLiteralExpressionSyntax
```

---

# 11. Opening delimiter

A Block String begins when:

1. the parser is reading the complete value of a LET or SET statement;
2. the next source character is an ordinary ASCII double quote;
3. the quote is the final non-space-or-tab character on that physical statement line.

Valid:

```smile
LET Message = "
```

Valid:

```smile
LET Message ="
```

Valid:

```smile
SET Message = "
```

Spaces or tabs after the opening quote and before the physical newline are structural.

They do not become String data.

---

# 12. Closing delimiter

The closing delimiter is a physical line whose only non-space-or-tab character is one ordinary ASCII double quote.

Valid:

```text
"
```

Valid:

```text
    "
```

Valid:

```text
<TAB>"
```

Spaces or tabs before the closing quote define the structural indentation margin.

Spaces or tabs after the closing quote are structural and do not become String data.

---

# 13. Content boundaries

Only physical lines between the opening and closing delimiter lines belong to the String.

The opening and closing delimiter lines do not belong to the value.

Example:

```smile
LET Name = "
S
 I
  N
"
```

Value:

```text
S\\n I\\n  N
```

There is:

- no automatic LF before `S`;
- one LF between adjacent content lines;
- no automatic LF after `N`.

---

# 14. Logical newline normalization

Each physical line boundary between Block String content lines becomes one logical:

```text
U+000A LF
```

This must be independent of the source file's physical line-ending convention.

The following source line endings must produce the same bound value:

```text
CRLF
LF
standalone CR
```

where the current source reader accepts standalone CR.

Normalization belongs entirely to the SMILE front end.

Target generators must never inspect or reinterpret the original Block String's physical line endings.

---

# 15. Structural indentation margin

The exact spaces/tabs before the closing delimiter define the structural indentation margin.

That exact sequence is removed from a content line only when the content line begins with the same sequence.

Do not:

- calculate a different margin for each line;
- infer minimum indentation;
- treat spaces and tabs as equivalent;
- convert spaces to tabs;
- convert tabs to spaces;
- trim additional indentation.

Example:

```smile
    LET Message = "
        Four spaces remain
            Eight spaces remain
    "
```

The closing delimiter margin is four spaces.

The resulting value is:

```text
    Four spaces remain\\n        Eight spaces remain
```

---

# 16. Critical whitespace rule

Four spaces and one tab are not the same String.

The compiler must preserve exact characters.

Do not transform:

```text
SPACE SPACE SPACE SPACE
```

into:

```text
TAB
```

Do not transform:

```text
TAB
```

into:

```text
SPACE SPACE SPACE SPACE
```

The previously observed generated C# form such as:

```csharp
"\\tHello World!\\n\\tThis is SMILE!\\n\\t\\tHow are you?"
```

is incorrect if the SMILE value actually contains spaces rather than tab characters.

---

# 17. Blank lines and trailing whitespace

Blank content lines are real String data.

A content line containing only spaces or tabs is also real String data after applicable structural-margin removal.

Trailing spaces and tabs are significant.

Do not call:

```text
Trim
TrimStart
TrimEnd
Strip
Dedent
stripIndent
textwrap.dedent
ReplaceLineEndings
```

or equivalent cleanup operations on the normalized SMILE value.

---

# 18. Official escapes

Block content uses the same official SMILE String escape table as ordinary quoted Strings:

| Source | Value |
|---|---|
| `\\\\` | backslash |
| `\\"` | double quote |
| `\\n` | line feed |
| `\\r` | carriage return |
| `\\t` | horizontal tab |
| `\\0` | NUL |
| `\\b` | backspace |
| `\\f` | form feed |

No new Block-only escape language should be introduced.

Unknown escapes continue to use the existing String diagnostics.

---

# 19. Quotes inside Block Strings

Ordinary quote characters inside content are data.

Example:

```smile
LET Message = "
He said "Hello".
"
```

must contain:

```text
He said "Hello".
```

Only a structural closing-delimiter line terminates the block.

Preserve the existing scanner's deterministic closing-delimiter behavior.

---

# 20. Comments inside Block Strings

Inside a Block String, these are data:

```text
REM
//
#
--
```

Example:

```smile
LET Message = "
REM not a comment
// not a comment
# not a comment
-- not a comment
"
```

No line inside the block may be reclassified as a source comment.

This rule must hold during:

- normal parsing;
- IF parsing;
- WHILE parsing;
- mixed IF/WHILE recovery;
- depth-limit recovery;
- syntax highlighting.

---

# 21. Control-flow-looking text inside Block Strings

Inside Block String content, these are data:

```text
IF
ELSE
ELSE IF
END IF
WHILE
END WHILE
```

Example:

```smile
LET Message = "
WHILE Count < 100
END WHILE
END IF
"
```

Those lines must not change parser structure.

---

# 22. Diagnostics

Reuse existing diagnostics where practical.

Do not invent duplicate codes for the same malformed construct.

Generalize SET-only wording where necessary.

Recommended meanings:

| Code | Meaning |
|---|---|
| `SMILE1003` | Unterminated ordinary String or Block String Literal |
| `SMILE1208` | Unknown/invalid String escape |
| `SMILE1209` | Unterminated String escape |
| `SMILE1306` | A Block String Literal is valid only as the complete value of LET or SET |
| `SMILE1307` | Unexpected content follows a closing Block String delimiter |
| `SMILE1308` | The opening quote of a Block String must end the physical LET or SET line |
| `SMILE1414` | LET is prohibited inside IF under the current no-scope rules |
| `SMILE1610` | LET is prohibited lexically inside WHILE under the current no-scope rules |

Preserve current span conventions unless a focused improvement is required.

---

# 23. Parser requirements

The current parser now supports:

```text
IF
WHILE
mixed IF/WHILE nesting
shared control-flow depth
iterative over-limit recovery
```

Do not regress any of that.

## 23.1 ParseLetStatement

The current parser rejects a Block String after LET.

Replace that rejection with successful handling equivalent to the SET path.

Required sequence:

1. recognize a valid Block String opening after LET `=`;
2. call the shared Block String scanner;
3. consume through the closing delimiter;
4. update `lineIndex`;
5. create `BlockStringLiteralExpressionSyntax`;
6. make the complete LET statement span include the closing delimiter;
7. return a normal `LetStatementSyntax`.

## 23.2 ParseSetStatement

Retain current SET behavior, but route it through generalized Block String names/API.

## 23.3 Invalid placements

For PRINT and other invalid positions:

1. recognize the misplaced Block opening;
2. consume the complete block safely;
3. emit one placement diagnostic;
4. resume after the closing delimiter.

Do not reinterpret block content as statements.

---

# 24. Mixed IF/WHILE parser recovery

SMILE v0.8.0 has one combined control-flow nesting limit:

```text
128
```

Entering depth 129 produces:

```text
SMILE1416
```

for IF, and:

```text
SMILE1611
```

for WHILE.

The parser's bounded recovery must not recurse into the rejected subtree.

Block String content must remain opaque to this recovery.

The new generalized LET Block scanner must preserve this property.

Add tests where Block String content in or near rejected deep structures contains:

```text
IF
ELSE
END IF
WHILE
END WHILE
REM
//
#
--
```

and verify recovery still finds the correct real outer terminator.

---

# 25. Binder requirements

The binder should not need a new semantic String type or Block-specific bound node.

A parsed:

```text
BlockStringLiteralExpressionSyntax
```

must bind to the existing:

```text
BoundStringLiteralExpression
```

with the already-normalized String value.

Do not add:

```text
BoundBlockStringLiteralExpression
WasBlockString
OriginalBlockText
OriginalIndentation
OriginalDelimiter
```

to the bound tree.

The bound tree represents program meaning, not source spelling.

---

# 26. LET binding

Preserve the current LET binding order:

1. confirm the name is not already declared;
2. bind the complete initializer while the new symbol is still absent;
3. infer the initializer type;
4. create the symbol;
5. apply static initialization;
6. add the symbol to the environment.

That naturally preserves:

- declaration-before-use;
- self-reference rejection;
- duplicate detection;
- fixed type.

A LET Block String should simply behave like any other String literal initializer after parsing.

---

# 27. IF and WHILE LET restrictions

Do not move scope policy into the Block String scanner.

The scanner should be able to consume a Block String anywhere the parser encounters one.

The binder continues to decide whether the containing LET statement is permitted.

Preserve current:

```text
SMILE1414
SMILE1610
```

behavior.

This separation is important for safe parser recovery.

---

# 28. Evaluator

The evaluator must not contain Block String-specific runtime logic.

After binding:

```smile
LET Message = "
Hello
World
"
```

is simply equivalent to initialization from a known String value:

```text
Hello\\nWorld
```

Do not make the evaluator inspect:

- source delimiters;
- indentation;
- source line endings;
- original syntax form.

---

# 29. WHILE fixed-point analysis must be preserved

SMILE v0.8.0 introduced real loop analysis.

Do not bypass, weaken, or duplicate it.

The compiler currently models loops as zero-or-more executions and solves loop-head facts conservatively.

Block String changes must preserve:

- loop-head fixed-point analysis;
- Known/Unknown merging;
- runtime-failure facts;
- Integer range widening;
- deterministic loop ordinals;
- zero-iteration path;
- genuine runtime loops.

---

# 30. Loop-carried String bounds must remain valid

SMILE v0.8.0 requires every String assigned through WHILE to retain a finite compile-time maximum UTF-8 byte length.

Unbounded recurrence is rejected with:

```text
SMILE1612
```

The LET Block feature must not weaken that rule.

Example:

```smile
LET Message = "
Hello
"

LET Count = 0

WHILE Count < 3
    SET Message = "
Hello again
"
    SET Count = Count + 1
END WHILE
```

must participate in the existing loop String-size planning exactly like an ordinary SET String value.

A Block String must not create a hidden dynamically unbounded storage path.

---

# 31. Analysis and simplification

Do not regress:

- statement-order propagation;
- SET mutation;
- INPUT invalidation;
- IF branch merging;
- WHILE back-edge merging;
- direct runtime storage reads;
- exact String-length planning;
- possible-NUL planning;
- checked arithmetic;
- short-circuit reachability;
- source layout ordering.

A direct LET Block literal is a known String value.

A SET Block literal inside WHILE is handled by the same loop-aware analysis as any other SET String value.

---

# 32. Target-generation principle

The front end owns SMILE Block String syntax.

Target generators receive normalized semantic String values.

Generators must not inspect:

- Block delimiters;
- original source indentation;
- opening-line placement;
- physical source line endings.

A target renderer decides how to represent a String based on:

```text
value
target language
semantic context
required runtime behavior
```

not on original SMILE spelling.

---

# 33. Important consequence: semantic multiline values

These two SMILE forms can produce the same value:

```smile
LET Message = "Hello\\nWorld"
```

and:

```smile
LET Message = "
Hello
World
"
```

Both become:

```text
Hello LF World
```

after the front end.

Therefore, high-level target generators may use the same idiomatic multiline target representation for either form when the bound expression is a direct String literal containing LF.

This is intentional.

The target generator should optimize for the clearest exact target-language representation of the semantic value.

However:

- do not fold explicit concatenation into one literal;
- do not fold explicit interpolation into one literal;
- preserve expression intent.

---

# 34. Expression-intent rule

This direct literal:

```smile
LET A = "First\\nSecond"
```

may generate a native target multiline literal.

This explicit concatenation:

```smile
LET B = "First\\n" + "Second"
```

should remain explicit concatenation where that is natural.

This interpolation:

```smile
LET C = $"First {Name}"
```

should remain interpolation where that is natural.

Do not use known-value analysis to erase the programmer's expression form.

---

# 35. When native multiline rendering applies

At minimum, consider native multiline rendering when:

1. the bound node is a direct `BoundStringLiteralExpression`;
2. the value contains at least one logical LF;
3. the destination has a clear multiline literal mechanism;
4. that mechanism can preserve the value exactly and readably.

This can apply in:

```text
LET
SET
PRINT evaluated expression
```

including SET inside:

```text
IF
WHILE
nested IF/WHILE combinations
```

Do not special-case only top-level statements.

---

# 36. Exactness has priority over multiline prettiness

A native multiline representation is preferred only if it preserves exact semantics.

Fallback to escaped or byte-oriented representations when necessary.

Fallback cases may include:

- embedded NUL;
- backspace;
- form feed;
- carriage return;
- problematic delimiters;
- difficult target escape interactions;
- trailing whitespace that native syntax would strip;
- source-encoding hazards;
- target compiler limitations.

A fallback is correct behavior, not a failure.

---

# 37. Literal-internal LF safeguard

SMILE Block String line boundaries normalize to logical LF.

When a generated target multiline literal contains physical line breaks that become value characters, emit those literal-internal line breaks deterministically as LF.

Do not blindly use:

```csharp
Environment.NewLine
```

inside a target literal renderer.

Do not later apply a whole-generated-file line-ending conversion that changes LF characters which are semantically part of the literal.

This is especially important for:

```text
C# raw strings
Java text blocks
JavaScript templates
Swift multiline strings
Python triple strings
C++ raw strings
```

Add exact runtime tests on Windows.

---

# 38. Implementation shape

Keep this KISS.

Do not create:

- a new compiler framework;
- a template language;
- a generic AST printer;
- a cross-target DSL;
- runtime libraries solely for multiline strings.

Preferred approach:

1. keep existing target escape helpers;
2. add focused multiline rendering helpers;
3. share only genuine common utilities;
4. keep target-specific policy target-specific.

Possible file:

```text
src/SMILE.Engine/Generation/TargetMultilineLiterals.cs
```

Possible shared helpers:

- split a String on LF while retaining empty edge segments;
- count maximum quote runs;
- choose deterministic delimiters;
- detect unsafe controls;
- emit literal-internal LF;
- preserve exact trailing whitespace.

The exact API is Codex's choice.

---

# 39. Ten target languages

SMILE's current ten destination languages are:

1. C#
2. C
3. MASM x64
4. JavaScript
5. Java
6. COBOL
7. Objective-C
8. Swift
9. Python
10. C++

Do not add another target.

---

# 40. C# strategy

For a normal multiline direct String literal, prefer C# raw String literals.

Canonical target form:

```csharp
string MultilineText = \"""
    Hello World!
    This is SMILE!
        How are you?
\""";

Console.WriteLine(MultilineText);
```

Inside generated `Main`, the source may be structurally indented:

```csharp
internal static class Program
{
    private static void Main()
    {
        string MultilineText = \"""
            Hello World!
            This is SMILE!
                How are you?
        \""";

        Console.WriteLine(MultilineText);
    }
}
```

The renderer must understand C# raw-string indentation rules so the runtime value remains exactly:

```text
    Hello World!
    This is SMILE!
        How are you?
```

## 40.1 C# delimiter length

Choose a raw delimiter with enough `"` characters to avoid collision with the content.

Use at least:

```text
"""
```

and increase deterministically when needed.

## 40.2 C# controls

Use the existing escaped C# String fallback when exact raw representation is inappropriate, including cases such as embedded NUL or difficult control characters.

## 40.3 C# spaces/tabs

Never replace source spaces with `\\t`.

---

# 41. JavaScript strategy

Prefer a template literal for a direct multiline String:

```javascript
let MultilineText = `    Hello World!
    This is SMILE!
        How are you?`;

console.log(MultilineText);
```

Escape target-significant sequences exactly:

```text
`
${
backslash
controls
```

Prevent literal `${` data from becoming interpolation.

Do not use the plain-literal renderer for a bound interpolated SMILE String.

Explicit SMILE interpolation remains JavaScript interpolation.

---

# 42. Java strategy

Prefer a Java text block when exactness is straightforward.

Canonical style for a value with no terminal LF:

```java
String MultilineText = \"""
    Hello World!
    This is SMILE!
        How are you?\\
\""";

System.out.println(MultilineText);
```

The final backslash suppresses the otherwise added terminal newline.

The renderer must understand Java text block:

- incidental indentation;
- newline normalization;
- escape processing;
- closing-delimiter behavior;
- trailing whitespace behavior.

Do not repair output with:

```java
.trim()
.strip()
.stripIndent()
```

because that changes runtime semantics.

For intended trailing spaces/tabs, use exact Java escapes where clear and compile-tested.

Otherwise fall back to adjacent ordinary Java String literals.

Example fallback:

```java
String MultilineText =
    "    Hello World!\\n"
    + "    This is SMILE!\\n"
    + "        How are you?";
```

---

# 43. Swift strategy

Prefer Swift multiline String literals:

```swift
let MultilineText: String = \"""
    Hello World!
    This is SMILE!
        How are you?
\"""

print(MultilineText)
```

Use structural indentation correctly.

Preserve the existing mutation analysis:

```text
let
var
```

Do not change that as part of multiline rendering.

For collision-prone data, use Swift extended String delimiters where clearer.

Example concept:

```swift
let Text = #\"""
literal \\n
literal \\(Name)
literal \"""
\"""#
```

Choose the number of `#` characters deterministically.

Fallback to the existing escaped form when needed.

---

# 44. Python strategy

Prefer a triple-quoted String where it remains clear and exact:

```python
MultilineText = \"""    Hello World!
    This is SMILE!
        How are you?\"""

print(MultilineText)
```

Important:

Python preserves indentation inside triple-quoted Strings.

Do not indent continuation content merely to align it visually with:

```python
def main() -> None:
```

because those spaces become String data.

If the exact literal would be visually confusing inside the generated function, prefer parenthesized adjacent ordinary literals:

```python
MultilineText = (
    "    Hello World!\\n"
    "    This is SMILE!\\n"
    "        How are you?"
)
```

Adjacent Python literals concatenate at compile time.

Do not import `textwrap`.

Do not emit:

```python
.dedent()
.strip()
.lstrip()
```

---

# 45. C++ strategy

Prefer a C++20 raw String literal:

```cpp
std::string MultilineText = R"SMILE(    Hello World!
    This is SMILE!
        How are you?)SMILE";

std::cout << MultilineText << '\\n';
```

Choose a deterministic custom delimiter that does not collide with:

```text
)delimiter"
```

inside the content.

C++ raw literal delimiters are limited in length and permitted characters.

Try deterministic candidates.

If no simple collision-free delimiter is available, fall back to adjacent escaped String literals.

Preserve:

- `std::string`;
- length-aware embedded-NUL construction;
- stream output;
- native String equality;
- RAII;
- current C++20 header planning.

---

# 46. C strategy

C has no true raw multiline String literal.

Use adjacent ordinary String literal fragments:

```c
const char *MultilineText =
    "    Hello World!\\n"
    "    This is SMILE!\\n"
    "        How are you?";

printf("%s\\n", MultilineText);
```

Preserve the existing C String model:

- compiler-generated safe format strings;
- `%` escaping;
- exact pointer/length semantics;
- exact NUL behavior;
- text after NUL;
- `fwrite` when `%s` is not valid;
- runtime mutation.

Never create a nonstandard quote-spanning C literal.

---

# 47. Objective-C strategy

SMILE's current Windows Objective-C backend is Foundation-free.

Do not introduce Foundation or NSString for this feature.

Use the same conventional adjacent C literal representation:

```objective-c
const char *MultilineText =
    "    Hello World!\\n"
    "    This is SMILE!\\n"
    "        How are you?";

printf("%s\\n", MultilineText);
```

Preserve existing exact-length and NUL semantics.

---

# 48. COBOL strategy

Do not force a fake high-level triple-quoted syntax into COBOL.

For multiline/control-containing values, exact UTF-8 byte-oriented storage is appropriate for the current GnuCOBOL backend.

Conceptual form:

```cobol
01 MultilineText PIC X(56)
   VALUE X"2020202048656C6C6F20576F726C64210A202020205468697320697320534D494C45210A2020202020202020486F772061726520796F753F".
01 MultilineText-LENGTH PIC 9(9) COMP-5 VALUE 56.
```

Preserve:

- fixed storage;
- logical byte length;
- LF as byte `0A`;
- embedded NUL;
- text after NUL;
- direct mutable storage reads;
- warning-free GnuCOBOL output.

The exact layout may differ according to current generator conventions.

Do not add a runtime dependency merely for prettier source.

---

# 49. MASM x64 strategy

Explicit bytes are natural MASM.

Prefer printable byte chunks plus numeric LF:

```asm
MultilineTextValue BYTE "    Hello World!", 10, "    This is SMILE!", 10, "        How are you?"
MultilineTextValueLength EQU $ - MultilineTextValue
```

Preserve:

- exact UTF-8;
- pointer/length storage;
- numeric controls;
- embedded NUL;
- deterministic labels;
- existing Win32 output behavior.

Do not add a high-level runtime just for String literals.

---

# 50. Canonical target acceptance table

For:

```smile
LET MultilineText = "
    Hello World!
    This is SMILE!
        How are you?
"

PRINT {MultilineText}
```

the normal control-free path should use:

| Target | Preferred representation |
|---|---|
| C# | raw multiline literal |
| JavaScript | template literal |
| Java | text block |
| Swift | multiline String literal |
| Python | triple-quoted literal or adjacent-literal fallback when indentation clarity requires it |
| C++ | raw String literal |
| C | adjacent ordinary String literals |
| Objective-C | adjacent C String literals |
| COBOL | exact byte/hex storage |
| MASM x64 | printable BYTE chunks plus numeric LF |

For the canonical sample, C#, JavaScript, Java, Swift, and C++ should not collapse to one escaped `"\\n"` line unless a real toolchain/compiler constraint discovered in the current repository requires a documented fallback.

Python may choose the adjacent-literal form if that is objectively clearer inside the current generated `main()` indentation while preserving idiomatic Python.

---

# 51. SET inside WHILE must use the same renderer

Example:

```smile
LET Message = ""
LET Count = 0

WHILE Count < 2
    SET Message = "
Hello
World
"
    SET Count = Count + 1
END WHILE

PRINT {Message}
```

High-level target generators must be able to use their native multiline assignment form inside the generated loop body.

Do not make the multiline renderer top-level only.

Do not bypass current loop-aware storage planning.

---

# 52. SET inside IF/WHILE combinations

Test:

```smile
LET Message = ""
LET Count = 0
LET Ready = TRUE

WHILE Count < 2
    IF Ready = TRUE THEN
        SET Message = "
Hello
World
"
    END IF

    SET Count = Count + 1
END WHILE

PRINT {Message}
```

The generated code must preserve:

- genuine WHILE;
- genuine IF;
- exact String value;
- current runtime storage;
- target-native multiline representation where safe.

---

# 53. Exact edge-case corpus

Test Block String values containing:

## Line structure

- one LF;
- several LFs;
- leading LF;
- trailing LF;
- two trailing LFs;
- empty first line;
- empty middle line;
- empty last line;
- consecutive blank lines.

## Whitespace

- leading spaces;
- leading tabs;
- mixed spaces and tabs;
- trailing spaces;
- trailing tabs;
- lines containing only spaces;
- lines containing only tabs;
- structural-margin match;
- structural-margin mismatch.

## Delimiters

- `"`;
- `""`;
- `"""`;
- longer quote runs;
- backticks;
- `${`;
- `\\(`;
- `\\#(`;
- C++ `)SMILE"`-shaped content;
- Python triple-double quotes;
- Python triple-single quotes.

## Controls

- backslash;
- tab;
- LF;
- CR;
- NUL;
- backspace;
- form feed;
- Unicode;
- text after NUL.

---

# 54. Source-line-ending tests

Use equivalent SMILE source authored with:

```text
LF
CRLF
standalone CR
```

All must bind to the same String value.

Run the generated targets on Windows where installed and compare exact runtime output with `SmileEvaluator`.

Do not accidentally let generated-file CRLF conversion change literal-internal LF values.

---

# 55. Generalize existing Block String tests

The current repository contains SET-specific Block String tests.

Generalize them.

A suitable new name may be:

```text
BlockStringConformanceTests
```

but use the best fit for the current test organization.

Do not delete existing data rows.

For each applicable normalization case, test both:

```smile
LET Value = "
...
"
```

and:

```smile
LET Value = ""

SET Value = "
...
"
```

The resulting bound String values must match exactly.

---

# 56. Parser tests

Add tests proving:

- LET Block parses;
- initializer syntax is `BlockStringLiteralExpressionSyntax`;
- exact source span is preserved;
- LET statement span reaches through the closing delimiter;
- following statement starts correctly;
- line/column data remain correct;
- CRLF/LF/CR normalization remains correct;
- malformed LET and SET blocks recover identically;
- invalid PRINT block placement consumes the full block;
- mixed IF/WHILE recovery ignores Block content.

---

# 57. Binder tests

Add tests proving:

- LET Block infers String;
- bound node is ordinary `BoundStringLiteralExpression`;
- duplicate LET still fails;
- forward references still fail;
- self-reference still fails;
- LET inside IF still fails with current rule;
- LET inside WHILE still fails with current rule;
- LET lexically inside WHILE through nested IF preserves current diagnostic behavior;
- malformed Block does not create a symbol.

---

# 58. Evaluator tests

Canonical:

```smile
LET MultilineText = "
    Hello World!
    This is SMILE!
        How are you?
"

PRINT {MultilineText}
```

Expected exact output:

```text
    Hello World!
    This is SMILE!
        How are you?
```

plus PRINT's line ending.

Also test:

```smile
LET MultilineText = "
Hello
"

PRINT MultilineText
PRINT {MultilineText}
```

Expected:

```text
MultilineText
Hello
```

with the normal line endings.

---

# 59. WHILE runtime tests

Test Block Strings assigned inside loops.

Example:

```smile
LET Message = ""
LET Count = 0

WHILE Count < 3
    SET Message = "
Iteration text
"
    PRINT {Message}
    SET Count = Count + 1
END WHILE
```

Compare evaluator and each installed target.

Also test a loop with a branch:

```smile
LET Message = ""
LET Count = 0

WHILE Count < 2
    IF Count = 0 THEN
        SET Message = "
First
"
    ELSE
        SET Message = "
Second
"
    END IF

    PRINT {Message}
    SET Count = Count + 1
END WHILE
```

---

# 60. Loop String-bound tests

Add focused regressions proving Block String integration does not break:

```text
SMILE1612
```

or current bounded String analysis.

A loop-carried fixed-size Block assignment should be accepted.

A pre-existing unbounded String recurrence must remain rejected.

Do not weaken analysis just because one participating String originated from a Block literal.

---

# 61. Structural generation tests

For each target, assert the expected construct.

Do not assert only that generated code contains the words:

```text
Hello World
```

Check the actual representation.

Examples:

- C#: raw delimiter;
- JavaScript: backticks;
- Java: text block;
- Swift: multiline delimiter;
- Python: triple quote or approved adjacent-literal path;
- C++: raw delimiter;
- C/Objective-C: adjacent literal fragments;
- COBOL: byte/hex storage;
- MASM: byte chunks and numeric `10`.

Also test the same form inside:

```text
WHILE
IF inside WHILE
WHILE inside IF
```

for SET.

---

# 62. Exact runtime conformance

Use `SmileEvaluator` as the semantic reference.

For every installed target, compare:

```text
stdout
stderr
exit code
```

exactly.

Do not trim output.

Do not discard:

```text
NUL
backspace
form feed
carriage return
tab
spaces
blank trailing lines
```

Use exact bytes where existing target tests already do so.

---

# 63. Generated warning hygiene

Preserve:

```text
SMILE_REQUIRE_ZERO_TARGET_WARNINGS
```

and current strict release validation.

Every compiler-backed target must remain warning-free for the new tests where the current suite requires it.

Do not introduce compiler warnings merely to obtain a prettier literal.

---

# 64. Syntax highlighting

Update the SMILE highlighter so a LET Block is highlighted as String content from opening delimiter through closing delimiter.

Inside the block:

- comments stay String-colored;
- IF/ELSE/WHILE/END text stays String-colored;
- blank lines remain content;
- tabs/spaces are preserved.

Preserve all current v0.8.0 keyword highlighting, including:

```text
WHILE
END
```

---

# 65. Desktop behavior

Do not regress the asynchronous WPF Desktop.

Preserve:

- debounce;
- cancellation;
- no UI-thread blocking;
- recoverable invalid intermediate syntax;
- first-paint behavior;
- target selector behavior;
- live preview generation.

Typing an unfinished:

```smile
LET Message = "
```

must not crash or freeze the app.

---

# 66. Target-editor hardening must remain intact

SMILE v0.7.0.1 made target panes independent editable build units.

This task must preserve:

- per-pane current source;
- duplicate target language selections;
- deterministic pane build order;
- target-pane revision ownership;
- stale generation result protection;
- generated cache behavior;
- learner edit preservation;
- target title `*` divergence marker;
- Maximize/Restore;
- Save Source;
- Build & Run;
- New;
- later authoritative SMILE edits.

Add or extend regressions only where this feature touches live generated output.

Do not simplify Desktop state management as part of this task.

---

# 67. Source layout preservation

Current comments and blank source lines are ordered non-semantic source items.

Do not regress them.

A Block String owns its internal blank lines.

Blank lines inside the block are not source-layout items.

Blank lines outside the block remain source-layout items.

The closing delimiter must return the parser to normal layout classification correctly.

---

# 68. Documentation updates

Update current living/normative documentation that describes the form as SET-only.

At minimum inspect and update:

```text
AGENTS.md
README.md
docs/Architecture.md
docs/Roadmap.md
docs/Toolchains.md
docs/SMILE Target Code Generation Standard v1.0.md
docs/SMILE Language Specification/001 - SMILE - SET Statement Official Specification v1.0.md
docs/SMILE Language Specification/003 - SMILE - String Literals Official Specification v1.0.md
docs/SMILE Language Specification/005 - SMILE - LET Statement Official Specification v1.0.md
docs/SMILE Language Specification/006 - SMILE - IF Statement Official Specification v1.0.md
docs/SMILE Language Specification/007 - SMILE - Full-Line Comments and Source Layout Preservation Official Specification v1.0.md
docs/SMILE Language Specification/009 - SMILE - WHILE Statement Official Specification v1.0.md
examples/language.smile
```

Also inspect the legacy consolidated specification if it is still intentionally maintained:

```text
docs/SMILE-Language-Specification-v0.1.md
```

Do not blindly update a file solely because it exists; follow the current repository's living-document policy.

---

# 69. String specification should become the shared home

The String Literal specification is the best shared normative home for generalized Block String semantics.

Move or consolidate shared rules there:

- purpose;
- LET/SET placement;
- opening delimiter;
- closing delimiter;
- indentation margin;
- newline normalization;
- escapes;
- comments inside block;
- exact target-generation semantics;
- diagnostics shared by LET/SET.

The LET and SET specs should reference that shared definition.

Avoid maintaining two copied normative Block String definitions that can drift.

---

# 70. LET specification

Remove stale statements equivalent to:

```text
All LET forms fit on one physical line.
Block String is SET-only.
LET Block produces SMILE1306.
```

Replace with the new initializer form.

Include canonical beginner examples.

---

# 71. SET specification

Preserve SET assignment semantics.

Update terminology from SET-specific Block String ownership to generalized Block String placement.

SET remains:

```text
change an existing variable
preserve its type
```

---

# 72. IF specification

Update wording that currently lists:

```text
SET Block String Literals
```

to generalized:

```text
Block String Literals as SET values
```

or equivalent precise wording.

Do not imply LET is now allowed inside IF.

---

# 73. WHILE specification

This is mandatory.

Update specification 009 so it remains synchronized.

Current WHILE bodies permit SET Block Strings.

Change terminology to the generalized feature while preserving:

- only SET is legal inside WHILE today;
- LET remains prohibited;
- Block content is inert to loop parsing;
- mixed depth recovery;
- finite String bounds;
- genuine loop execution.

Do not accidentally imply LET Block declarations are legal inside WHILE.

---

# 74. AGENTS.md

Update permanent project guidance to reflect:

```text
Block String Literal
valid complete value for LET or SET
```

while retaining all v0.8.0 permanent rules.

Update SET-specific scanner wording where appropriate.

Do not delete WHILE guidance.

Do not remove target-editor hardening guidance.

---

# 75. README and examples

Update `examples/language.smile` cumulatively.

Do not delete earlier valid examples.

Add:

1. a top-level LET Block declaration;
2. a later SET Block reassignment;
3. if useful, a SET Block inside WHILE while preserving bounded analysis.

Keep the cumulative teaching value of the file.

README should explain the beginner benefit.

---

# 76. Roadmap/versioning

Follow current repository versioning conventions.

Do not invent a release number without first inspecting how the project currently records incremental language changes.

If this work is assigned a release version by current project practice, synchronize:

- README;
- Roadmap;
- About dialog/version;
- package identity;
- examples;
- specs;
- tests.

Do not silently call it v0.8.1 unless that matches the actual project's chosen release plan.

---

# 77. Historical requirement files

Do not rewrite old dated requirement files merely to make them use new terminology.

Historical files describe the project state when they were authored.

If Sin requests this new instruction brief to be committed, store it under the repository's current `Requirements` naming convention without modifying prior history.

---

# 78. Search checklist

Before editing, search current repository text for:

```text
SET Block String
SET-only
valid only as the complete value of SET
SetBlockString
SMILE1306
SMILE1307
SMILE1308
BlockStringLiteral
```

Also inspect all locations that enumerate statement kinds for:

```text
BoundLetStatement
BoundSetStatement
BoundIfStatement
BoundWhileStatement
```

so multiline literal generation works recursively.

---

# 79. Generator recursion checklist

Every target generator currently traverses structured source.

Ensure new literal rendering applies to String literals found in:

```text
top-level LET
top-level SET
SET in IF
SET in ELSE IF
SET in ELSE
SET in WHILE
SET in nested WHILE
SET in IF inside WHILE
SET in WHILE inside IF
```

Do not forget any target's recursive statement emitter.

---

# 80. Bound-tree traversal checklist

Preserve current helpers that recursively enumerate:

```text
IF
WHILE
```

Do not reintroduce flat top-level-only scans for:

- mutation;
- headers;
- String planning;
- NUL planning;
- target identifier mapping;
- warning checks;
- required includes/helpers.

---

# 81. Strict target toolchains

Preserve all ten current toolchain profiles.

Expected families include:

```text
C#           .NET SDK
C            MSVC
MASM x64     ml64/link
JavaScript   Node.js
Java         JDK
COBOL        GnuCOBOL
Objective-C  Clang
Swift        Swift Windows toolchain
Python       Python 3.10+
C++          MSVC C++20
```

Use the current `docs/Toolchains.md` and toolchain classes as authoritative.

---

# 82. Validation order

Use this order:

1. read current `AGENTS.md`;
2. inspect current `main`;
3. inspect Block String scanner and current tests;
4. inspect new WHILE parser/binder/analysis;
5. generalize scanner/result names;
6. make LET parse Block values;
7. preserve scope rejection;
8. add LET parser/binder/evaluator tests;
9. generalize Block tests;
10. implement target multiline literal helpers;
11. integrate high-level targets;
12. validate C/Objective-C;
13. validate COBOL/MASM;
14. add WHILE/IF nested SET tests;
15. run exact target runtime tests;
16. update syntax highlighting;
17. update Desktop regressions;
18. update all normative/living docs;
19. run full normal validation;
20. run full strict validation;
21. inspect diff for accidental broad changes;
22. stop before commit/push unless Sin explicitly requested publication.

---

# 83. Normal validation commands

Run from repository root in PowerShell:

```powershell
$ErrorActionPreference = 'Stop'

git status --short
git rev-parse HEAD

dotnet restore SMILE.sln

dotnet build SMILE.sln -c Debug --no-restore -nologo
dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo

dotnet build SMILE.sln -c Release --no-restore -nologo
dotnet test SMILE.sln -c Release --no-build --no-restore -nologo
```

---

# 84. Strict all-target validation

Use the current repository's strict gate.

At the observed v0.8.0 baseline that includes:

```powershell
$env:SMILE_REQUIRE_JAVA = '1'
$env:SMILE_REQUIRE_ALL_TARGETS = '1'
$env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS = '1'

dotnet build SMILE.sln -c Debug --no-restore -nologo
dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo

dotnet build SMILE.sln -c Release --no-restore -nologo
dotnet test SMILE.sln -c Release --no-build --no-restore -nologo

Remove-Item Env:SMILE_REQUIRE_JAVA
Remove-Item Env:SMILE_REQUIRE_ALL_TARGETS
Remove-Item Env:SMILE_REQUIRE_ZERO_TARGET_WARNINGS
```

If the current repository has added another strict environment switch since this brief was written, follow current `AGENTS.md` and `docs/Toolchains.md`.

Do not treat environment-skipped target tests as strict completion.

---

# 85. Final repository hygiene

Run:

```powershell
git status --short
git diff --check
```

Inspect every changed file.

Do not commit:

```text
bin
obj
out
temporary generated target programs
toolchain output
logs
```

unless a file is intentionally version-controlled by existing repository policy.

---

# 86. Manual Desktop smoke test

Enter:

```smile
LET MultilineText = "
    Hello World!
    This is SMILE!
        How are you?
"

PRINT {MultilineText}
```

Verify:

- valid SMILE;
- String syntax highlighting;
- exact spaces;
- no automatic tabs;
- all three visible target panes update;
- target editor divergence markers remain correct;
- editable target panes remain editable;
- no target edit is overwritten by stale generation;
- C# uses raw multiline representation;
- high-level target panes use intended multiline representation;
- Build & Run output matches evaluator;
- UI remains responsive.

---

# 87. Manual WHILE smoke test

Enter:

```smile
LET Message = ""
LET Count = 0

WHILE Count < 2
    SET Message = "
Hello
World
"
    PRINT {Message}
    SET Count = Count + 1
END WHILE
```

Verify:

- loop highlights correctly;
- Block content does not affect `END WHILE`;
- generated code contains genuine target loop;
- SET uses native multiline form where safe;
- output repeats exactly twice;
- no warning;
- no UI freeze.

---

# 88. Manual invalid-scope smoke test

Test:

```smile
WHILE Count < 1
    LET Message = "
Hello
"
END WHILE
```

and:

```smile
IF Ready = TRUE THEN
    LET Message = "
Hello
"
END IF
```

Verify current LET scope diagnostics remain correct.

Also verify Block content is consumed safely rather than generating cascaded unknown-statement or mismatched-terminator errors.

---

# 89. Definition of done

The task is complete only when all of these are true:

- LET directly accepts a Block String initializer.
- SET still accepts Block String values.
- LET and SET use one scanner.
- The feature is normatively generalized to `Block String Literal — The SMILE Way`.
- Block String normalization remains front-end-only.
- Bound representation remains ordinary String literal.
- LET remains prohibited in IF/WHILE according to current scope rules.
- WHILE fixed-point analysis remains correct.
- `SMILE1612` behavior remains correct.
- mixed IF/WHILE depth recovery remains correct.
- spaces are never converted to tabs.
- tabs are never converted to spaces.
- LF normalization remains exact.
- blank lines remain exact.
- trailing spaces/tabs remain exact.
- official escapes remain exact.
- embedded NUL remains exact.
- text after NUL remains exact.
- `PRINT Variable` remains literal template text.
- `PRINT {Variable}` reads target storage.
- C# uses an idiomatic raw multiline literal for the canonical safe sample.
- JavaScript uses a template literal for the canonical safe sample.
- Java uses a text block for the canonical safe sample.
- Swift uses a multiline literal for the canonical safe sample.
- Python uses a clear idiomatic multiline or adjacent-literal representation.
- C++ uses a raw String literal for the canonical safe sample.
- C and Objective-C use adjacent literals.
- COBOL and MASM preserve exact byte-oriented representations.
- every target retains a safe fallback.
- multiline rendering works recursively inside SET in IF/WHILE structures.
- generated output stays deterministic.
- all target compiler warnings remain clean.
- normal Debug/Release suites pass.
- strict Debug/Release all-target suites pass.
- exact evaluator-versus-target tests pass.
- syntax highlighting remains correct.
- target-editor hardening remains correct.
- documentation and implementation agree.
- no unrelated changes are made.
- no commit/push occurs without Sin's explicit instruction.

---

# 90. Required completion report

When finished, report:

1. starting commit SHA;
2. ending working-tree SHA if unchanged/no commit;
3. every source file changed;
4. every test file changed;
5. every documentation/spec file changed;
6. exact generalized Block String grammar;
7. final canonical generated form for all ten targets;
8. number of tests added/changed;
9. normal Debug results;
10. normal Release results;
11. strict Debug results;
12. strict Release results;
13. exact target toolchains exercised;
14. generated compiler-warning results;
15. exact evaluator-versus-target result;
16. WHILE fixed-point/String-bound regression results;
17. mixed IF/WHILE parser recovery results;
18. Desktop highlighting result;
19. target-editor race/ownership regression result;
20. manual canonical LET Block smoke result;
21. manual WHILE SET Block smoke result;
22. `git status --short`;
23. confirmation that nothing was committed or pushed unless Sin explicitly requested it.

Do not report complete while:

- a strict target is skipped;
- a generated compiler warning remains;
- evaluator output differs from a target;
- Block whitespace differs;
- WHILE analysis regresses;
- Desktop race tests regress.

---

# 91. Primary repository references to inspect

Before implementation, inspect the current versions of at least:

```text
AGENTS.md

src/SMILE.Engine/Lexer.cs
src/SMILE.Engine/Parser.cs
src/SMILE.Engine/Binder.cs
src/SMILE.Engine/Analysis.cs
src/SMILE.Engine/Evaluation.cs
src/SMILE.Engine/Language.cs
src/SMILE.Engine/SyntaxKind.cs

src/SMILE.Engine/Generation/BoundProgramSimplifier.cs
src/SMILE.Engine/Generation/BoundStatementTree.cs
src/SMILE.Engine/Generation/TargetExpression.cs
src/SMILE.Engine/Generation/TargetEscapes.cs
src/SMILE.Engine/Generation/TargetRuntimeFacts.cs
src/SMILE.Engine/Generation/CSharpCodeGenerator.cs
src/SMILE.Engine/Generation/CCodeGenerator.cs
src/SMILE.Engine/Generation/MasmX64CodeGenerator.cs
src/SMILE.Engine/Generation/JavaScriptCodeGenerator.cs
src/SMILE.Engine/Generation/JavaCodeGenerator.cs
src/SMILE.Engine/Generation/CobolCodeGenerator.cs
src/SMILE.Engine/Generation/ObjectiveCCodeGenerator.cs
src/SMILE.Engine/Generation/SwiftCodeGenerator.cs
src/SMILE.Engine/Generation/PythonCodeGenerator.cs
src/SMILE.Engine/Generation/CppCodeGenerator.cs

tests/SMILE.Tests/SetBlockStringConformanceTests.cs
tests/SMILE.Tests/IfStatementConformanceTests.cs
tests/SMILE.Tests/WhileStatementConformanceTests.cs
tests/SMILE.Tests/WhileAnalysisHardeningTests.cs
tests/SMILE.Tests/WhileTargetConformanceTests.cs
tests/SMILE.Tests/SyntaxHighlightingTests.cs
tests/SMILE.Tests/DesktopCommandTests.cs

src/SMILE.Desktop/Highlighting/SMILE.xshd
src/SMILE.Desktop/MainWindowViewModel.cs
src/SMILE.Desktop/TargetPaneViewModel.cs
```

The actual current file set is authoritative if names have changed.

---

# 92. Language references for target multiline syntax

Use official/primary documentation when implementing target rendering.

## C#

Microsoft C# language specification and String documentation:

```text
https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/lexical-structure
https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/strings/
```

## Java

OpenJDK text block specification/JEP:

```text
https://openjdk.org/jeps/378
```

## JavaScript

ECMAScript lexical grammar/template literal rules:

```text
https://tc39.es/ecma262/multipage/ecmascript-language-lexical-grammar.html
```

## Swift

The Swift Programming Language:

```text
https://docs.swift.org/swift-book/documentation/the-swift-programming-language/stringsandcharacters/
https://docs.swift.org/swift-book/documentation/the-swift-programming-language/lexicalstructure/
```

## Python

Python lexical analysis:

```text
https://docs.python.org/3/reference/lexical_analysis.html
```

## C++

C++ working draft String literal rules:

```text
https://eel.is/c++draft/lex.string
```

For C, Objective-C, COBOL, and MASM, preserve and extend the current repository's proven toolchain-compatible exact literal/data generation rather than replacing it with an unverified stylistic experiment.

---

# 93. Final design principle

SMILE is a teaching language.

The generated target source is part of the lesson.

Therefore:

> A multiline SMILE String should look like natural multiline text in destination languages that have a safe, idiomatic multiline String syntax.

At the same time:

> Semantic correctness is absolute. When a destination's pretty multiline syntax cannot preserve the exact SMILE String value safely, use the clearest exact fallback.

And for SMILE itself:

> A beginner should be able to declare a multiline String directly with LET instead of declaring an empty placeholder and immediately reassigning it with SET.

That is the desired language behavior.
