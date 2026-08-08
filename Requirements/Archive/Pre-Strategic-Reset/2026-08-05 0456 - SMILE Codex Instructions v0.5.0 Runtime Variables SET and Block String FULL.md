# Codex Implementation Instructions — SMILE v0.5.0 Runtime Variables, SET, and SET Block String Literals

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work **directly on `main` only**.
- Sin is the only developer.
- **Do not create, suggest, or use a feature branch.**
- Do not open a pull request.
- Re-read `AGENTS.md` before changing code.
- Inspect the current `main` branch and working tree before editing.
- Do not discard, reset, overwrite, or commit unrelated user work.
- Do not commit or push unless Sin explicitly authorizes it in the Codex session.
- Follow KISS and KISS v2, “The Sin Way.”
- Do not add another destination language.
- Do not add `IF`, `INPUT`, loops, functions, arrays, floating-point values, classes, or scopes.
- Do not add a parser generator, compiler framework, runtime framework, template engine, package manager, or unnecessary dependency.

The reviewed baseline when this brief was prepared was:

```text
b759c649ab05bf2a7b678b901194ce97fa9ea447
Sin and Codex: Harden final target identifiers and headers
```

Do not assume that SHA is still current. Always start from the newest `main`.

---

# 1. Companion official specification

Use the complete companion file:

```text
SMILE - SET Statement Official Specification v1.0.md
```

Publish it at:

```text
docs/SMILE Language Specification/SMILE - SET Statement Official Specification v1.0.md
```

The implementation must include and conform to:

- ordinary `SET` assignment;
- mutable runtime variable state;
- `SET Block String Literal — The SMILE Way`;
- preserved multiline content;
- structural indentation removal;
- normalized logical line feeds;
- SET-only placement;
- complete-RHS restriction;
- exact String, escape, whitespace, and embedded-NUL behavior;
- all diagnostics and normative examples.

Do not silently change the specification while coding.

If implementation reality requires a specification change:

1. update the official specification deliberately;
2. update normative examples;
3. add matching conformance tests;
4. explain the change in the completion report.

---

# 2. Milestone

Create:

> **SMILE v0.5.0 — Runtime Variables, SET, and SET Block String Literals**

Add one new statement keyword:

```smile
SET
```

Example:

```smile
LET Counter = 0
SET Counter = Counter + 1
PRINT {Counter}
```

Also add the SET-only block String form:

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

Do not add another keyword in this release.

---

# 3. Main architectural objective

SMILE v0.4.3.1 assumes every `LET` has one permanent compile-time value stored directly on:

```text
BoundLetStatement.ConstantValue
```

That model must change.

A variable now has:

```text
a declaration
a fixed type
an initial value
a current runtime value
zero or more later assignments
```

Do not treat the initial compile-time value as the variable's permanent identity.

The evaluator's mutable environment must become the source of truth for current state.

---

# 4. Do not use a nullable ConstantValue patch

Do not solve this by merely changing:

```csharp
SmileValue ConstantValue
```

to:

```csharp
SmileValue? ConstantValue
```

That would blur:

```text
compile-time known information
```

with:

```text
current runtime variable state
```

Preferred direction:

- remove permanent current-value ownership from `BoundLetStatement`;
- keep bound statements semantic and target-neutral;
- use a separate sequential analysis or execution trace for known values;
- use the evaluator's mutable environment for runtime state.

---

# 5. Required token and keyword additions

Add:

```text
SyntaxKind.SetKeyword
```

Recognize `SET` case-insensitively.

Add a dedicated token or lexical result for a SET block String.

Recommended name:

```text
SyntaxKind.BlockStringLiteralToken
```

or:

```text
SyntaxKind.SetBlockStringLiteralToken
```

Requirements:

- ordinary one-line String tokenization remains unchanged;
- recognize a block opening quote only when the quote is followed by optional spaces/tabs and then a physical newline;
- consume through a valid closing delimiter line;
- retain exact source span and line information;
- retain or produce the normalized String value;
- produce `SMILE1003` for an unterminated block;
- preserve source text exactly, including trailing spaces and tabs.

The lexer may recognize this source form outside SET so the parser can give a precise placement diagnostic, but only the SET parser may accept it.

Do not make all quoted String expressions multiline-capable.

---

# 6. Opening-delimiter recognition

A block begins only when the opening quote is the final non-whitespace character on the physical line.

Valid:

```smile
SET Name ="
```

```smile
SET Name = "
```

The scanner must recognize:

```text
double quote
optional spaces or tabs
physical newline
```

as the block opening sequence.

This remains an ordinary one-line String:

```smile
SET Name = "Sin"
```

This is not a block opening:

```smile
SET Name ="Sin
```

Report the appropriate ordinary unterminated String or SET block-opening diagnostic.

---

# 7. Closing-delimiter recognition

The closing delimiter is a physical line whose only non-whitespace character is one ordinary double quote.

Examples:

```text
"
```

```text
    "
```

```text
	"
```

Allow spaces or tabs after the quote.

Reject any non-whitespace content after it.

The leading spaces/tabs before the closing quote form the structural indentation margin.

The lexer or block scanner must not terminate on quotes embedded in ordinary content lines.

Example content:

```text
He said "Hello".
```

does not close the block.

---

# 8. Block String normalization algorithm

Implement one shared front-end normalization routine.

Conceptual inputs:

```text
raw content lines
closing delimiter indentation margin
source line-ending forms
```

Conceptual algorithm:

```text
1. Identify the physical content lines strictly between the delimiter lines.
2. Read the exact leading whitespace sequence before the closing quote.
3. For each content line:
       if it begins with that exact margin:
           remove exactly that margin
       otherwise:
           preserve the line exactly
4. Join the resulting content lines with one logical '\n'.
5. Do not add a newline before the first line.
6. Do not add a newline after the final content line.
7. Decode official SMILE escapes.
8. Return one ordinary String value.
```

Important:

- top-level margin is normally empty;
- additional indentation beyond the margin is preserved;
- trailing spaces/tabs are preserved;
- blank content lines are preserved as empty lines;
- a whitespace-only content line may retain whitespace after margin removal;
- CRLF, LF, and accepted CR source separators normalize to logical `\n`;
- a backslash followed by a physical newline is not a line-continuation escape.

Do not call `.Trim()`, `.TrimStart()`, or `.TrimEnd()` on content lines or the complete value.

---

# 9. Empty, leading-newline, and trailing-newline behavior

Required:

## Empty String

```smile
SET Message ="
"
```

Value:

```text
""
```

## Leading newline

```smile
SET Message ="

Hello
"
```

Value:

```text
\nHello
```

## No automatic trailing newline

```smile
SET Message ="
Hello
"
```

Value:

```text
Hello
```

## Intentional trailing newline

```smile
SET Message ="
Hello

"
```

Value:

```text
Hello\n
```

Add exact tests for all four cases.

---

# 10. Quotes and escapes in block content

Quotes inside ordinary content lines are literal.

Example:

```smile
SET Message ="
He said "Hello".
"
```

Value:

```text
He said "Hello".
```

A line that would otherwise be only a closing quote can be represented as content with:

```smile
\"
```

Decode all official escapes:

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

Escape decoding must use the same canonical String semantics as ordinary String literals.

Do not create a second incompatible escape implementation.

---

# 11. Required syntax node

Add one canonical SET statement syntax node:

```text
SetStatementSyntax
```

Conceptual shape:

```csharp
public sealed record SetStatementSyntax(
    string Name,
    TextSpan NameSpan,
    ExpressionSyntax Value,
    TextSpan Span)
    : StatementSyntax(Span);
```

A dedicated block String syntax node is optional if it helps preserve source form and placement diagnostics.

Possible shape:

```text
BlockStringLiteralExpressionSyntax
```

However, it must bind to the ordinary String bound expression.

Do not reuse `LetStatementSyntax`.

---

# 12. Parser changes

Parse:

```text
SET hspace+ identifier hspace* '=' hspace* set-value
```

Where `set-value` is:

```text
ordinary expression
or
one complete SET block String literal
```

Dedicated diagnostics:

| Code | Meaning |
|---|---|
| `SMILE1301` | Variable name required after SET |
| `SMILE1302` | Missing equals sign |
| `SMILE1303` | Missing SET value |
| `SMILE1304` | Undefined SET target |
| `SMILE1305` | Assignment type mismatch |
| `SMILE1306` | SET Block String Literal is valid only as complete SET value |
| `SMILE1307` | Unexpected content follows closing block delimiter |
| `SMILE1308` | Block opening quote must end the physical SET line |

Parser diagnostics cover:

```text
SMILE1301
SMILE1302
SMILE1303
SMILE1306
SMILE1307
SMILE1308
```

Binder diagnostics cover:

```text
SMILE1304
SMILE1305
```

---

# 13. SET-only placement enforcement

Accept:

```smile
SET Name ="
S
 I
 N
"
```

Reject:

```smile
LET Name ="
S
 I
 N
"
```

Reject:

```smile
PRINT "
S
 I
 N
"
```

Reject:

```smile
SET Name ="
S
 I
 N
" + Suffix
```

Reject:

```smile
SET Name = Prefix + "
S
 I
 N
"
```

Reject:

```smile
SET Name = ("
S
 I
 N
")
```

Reject:

```smile
SET Message =$"
Hello {Name}
"
```

Reject any non-whitespace after the closing quote before newline or EOF.

Do not silently reinterpret extra content as another statement.

---

# 14. One logical statement spanning physical lines

The parser is currently line-oriented.

Add one explicit exception:

```text
A SET Block String Literal creates one logical SET statement spanning multiple physical lines.
```

The block token or syntax must consume its internal physical newlines so they do not become statement terminators.

The physical newline after the closing delimiter terminates the SET statement normally.

Maintain accurate:

- absolute spans;
- line numbers;
- columns;
- diagnostic locations;
- editor highlighting boundaries.

---

# 15. Required bound representation

Add:

```text
BoundSetStatement
```

Conceptual shape:

```csharp
public sealed record BoundSetStatement(
    VariableSymbol Variable,
    BoundExpression Value)
    : BoundStatement;
```

Remove `SmileValue ConstantValue` from `BoundLetStatement`.

Preferred shape:

```csharp
public sealed record BoundLetStatement(
    VariableSymbol Variable,
    BoundExpression Initializer)
    : BoundStatement;
```

A SET Block String Literal must bind to:

```text
BoundStringLiteralExpression
```

containing the complete normalized String value.

Do not create target-specific block bound nodes.

---

# 16. Binder rules

The binder must:

1. resolve the SET target case-insensitively;
2. require an earlier successful LET declaration;
3. bind the ordinary expression or normalized block String;
4. allow the target variable on an ordinary expression RHS;
5. require exact type equality;
6. produce `BoundSetStatement` only when valid.

A block String always has type `String`.

Therefore:

```smile
LET Counter = 0

SET Counter ="
1
2
"
```

must produce `SMILE1305`.

---

# 17. LET semantics update

Update implementation and official LET specification.

`LET` now means:

```text
declare variable
determine fixed type
evaluate initializer
store initial runtime value
```

It no longer means:

```text
create a permanently immutable compile-time constant
```

Preserve:

- declaration-before-use;
- no redeclaration;
- no self-reference in a LET initializer;
- failed declarations do not leak symbols;
- case-insensitive lookup;
- type inference from initializer;
- one-line String expressions in LET.

SET Block String Literals remain invalid in LET.

---

# 18. Runtime expression evaluator

Evolve the expression evaluator so it evaluates against:

```text
IReadOnlyDictionary<VariableSymbol, SmileValue>
```

The existing `BoundConstantEvaluator` likely contains most required expression behavior.

Rename only when clarity materially improves.

A name such as:

```text
BoundExpressionEvaluator
```

is reasonable.

Avoid duplicate semantic evaluators.

---

# 19. Reference evaluator

Execute statements sequentially.

```text
values = empty mutable environment

for each statement:
    LET:
        value = evaluate initializer using current values
        values[variable] = value

    SET:
        newValue = evaluate right side using current values
        values[variable] = newValue

    PRINT:
        value = evaluate expression using current values
        append canonical display text
        append newline
```

A block String arrives as an ordinary normalized String literal expression.

Important:

- evaluate the complete SET value before updating;
- the old value is visible on ordinary RHS expressions;
- assignment is atomic;
- PRINT reads the latest value.

---

# 20. Sequential bound-program analysis

Create one small shared sequential analysis facility.

Suggested responsibilities:

```text
execute the bound program symbolically with current known values
record each LET result
record each SET result
record each PRINT result
record statement-local values before and after statements where needed
collect current compile-time diagnostics
```

A reasonable name:

```text
BoundProgramExecutionTrace
```

or:

```text
BoundProgramStateTrace
```

It should provide the information needed for:

- simplification;
- Integer profiling;
- C/Objective-C exact String lowering;
- COBOL storage sizing;
- MASM pointer/length data;
- Python safe f-string lowering;
- conformance tests.

Do not store permanent current values on bound nodes.

---

# 21. Current diagnostics remain compile-time

SMILE v0.5.0 still has no:

- input;
- branch;
- loop;
- function;
- external runtime data.

Therefore all current statement values remain determinable in source order.

Continue reporting overflow, division by zero, invalid escape, and type errors before target generation.

Use the sequential analysis instead of permanent LET constant fields.

---

# 22. Simplifier must become mutation-aware

Required order:

## LET

```text
simplify initializer using current environment
evaluate initializer
record current value
```

## SET

```text
simplify right side using old environment
evaluate complete right side
update known value only after evaluation
```

## PRINT

```text
simplify using current environment
```

Never propagate an old value past SET.

A block String is already an ordinary String literal at this stage.

---

# 23. Mutation-aware short circuit

Required:

```smile
LET Flag = FALSE
SET Flag = TRUE

PRINT {Flag OR (1 / 0 = 0)}
```

Output:

```text
TRUE
```

Also:

```smile
LET Flag = TRUE
SET Flag = FALSE

PRINT {Flag AND (1 / 0 = 0)}
```

Output:

```text
FALSE
```

Preserve:

- left-to-right evaluation;
- binder validation of both operands;
- no simplification or evaluation of unreachable right operands.

---

# 24. Integer profile analysis

The per-program Integer profile must inspect:

- LET values;
- SET values;
- operands;
- intermediate arithmetic results;
- PRINT expressions;
- interpolation holes;
- values at the correct statement position.

If any reachable Integer requires wide storage, preserve the whole-program target policy:

```text
C / Objective-C -> int64_t
C++              -> std::int64_t
C# / Java        -> long
JavaScript       -> BigInt
Swift            -> Int64
Python           -> int
```

Ordinary programs continue using natural small Integer types.

---

# 25. Variable mutation analysis

Add:

```text
ISet<VariableSymbol> MutatedVariables
```

containing every variable targeted by at least one SET.

Use it where declaration syntax differs.

Most importantly:

```smile
LET Counter = 0
SET Counter = 1
```

must generate Swift:

```swift
var Counter: Int = 0
Counter = 1
```

A never-assigned Swift variable may remain `let`.

---

# 26. Target generation — general rules

Every target generator handles:

```text
BoundLetStatement
BoundSetStatement
BoundPrintStatement
```

High-level targets preserve natural assignment.

Low-level targets may lower a statically known SET value when required, but must emit an actual storage update at the SET position.

Do not omit SET merely because the final result is known.

The front end fully normalizes block Strings.

No target generator may:

- inspect source text;
- detect block delimiters;
- remove indentation;
- normalize block newlines;
- decode block-specific syntax.

---

# 27. C# generation

Ordinary assignment:

```csharp
int Counter = 0;
Counter = Counter + 1;
```

String assignment:

```csharp
string Name = "Sin";
Name = "Louiery";
```

Block String:

```smile
SET Name ="
S
 I
 N
"
```

generates an ordinary target String preserving logical newlines:

```csharp
Name = "S\n I\n N";
```

The generator may choose the clearest valid C# literal form while preserving the exact value.

Preserve:

- Integer profile;
- canonical display;
- interpolation;
- exact control characters.

---

# 28. C++ generation

Ordinary assignment:

```cpp
int Counter = 0;
Counter = Counter + 1;
```

String:

```cpp
std::string Name = "Sin";
Name = "Louiery";
```

A block String generates as an ordinary normalized `std::string` value.

Preserve:

- owned `std::string`;
- embedded NUL with length-aware construction;
- native String equality;
- `std::cout`;
- facility-driven minimal headers;
- macro-safe identifiers;
- Integer profile.

Do not introduce C++ raw String syntax solely to mirror the SMILE source form unless it is clearly simpler and preserves all escape semantics. Ordinary escaped output is acceptable.

---

# 29. JavaScript generation

Use:

```javascript
let Counter = 0;
Counter = Counter + 1;
```

A block String may generate as:

```javascript
Name = "S\n I\n N";
```

or another clear exact ordinary String form.

Preserve:

- Number/BigInt whole-program profile;
- `Math.trunc` Number division;
- BigInt division;
- canonical Boolean display;
- existing template literals for ordinary interpolation.

---

# 30. Java generation

Generate:

```java
int Counter = 0;
Counter = Counter + 1;
```

String:

```java
String Name = "Sin";
Name = "Louiery";
```

A block String generates as an ordinary escaped Java String value.

Preserve:

- int/long profile;
- `.equals` String equality;
- canonical display.

Do not use Java text blocks merely because the SMILE source was a block String. The target receives only the normalized value.

---

# 31. Python generation

Generate:

```python
Counter = 0
Counter = Counter + 1
```

A block String may generate as an ordinary escaped Python String:

```python
Name = "S\n I\n N"
```

No block-specific runtime helper is needed.

Preserve:

- `_smile_div` only when needed;
- `_smile_text` only when needed;
- f-string safety;
- Python 3.10+ compatibility;
- identifier mapping;
- deterministic output.

---

# 32. Swift generation

Use mutation analysis.

```swift
var Counter: Int = 0
Counter = Counter + 1
```

A String assigned by SET must use `var`.

A block String generates as an ordinary exact Swift String value.

Preserve:

- Int/Int64 profile;
- interpolation;
- exact control characters;
- canonical display.

Do not use Swift multiline delimiters solely to mirror SMILE source syntax.

---

# 33. C and Objective-C generation

## Integer and Boolean

```c
int Counter = 0;
Counter = Counter + 1;
```

```c
bool Ready = false;
Ready = true;
```

## Ordinary NUL-free Strings

Pointer reassignment is acceptable:

```c
const char *Name = "Sin";
Name = "Louiery";
```

A normalized block String may assign:

```c
Name = "S\n I\n N";
```

## Complex String SET

Because all v0.5.0 values remain statically traceable, complex String SET values may be lowered to exact current values.

## Embedded NUL

If a variable can contain embedded NUL at any point, maintain exact byte-length metadata.

Conceptual:

```c
const char *Data = "A\000B";
size_t DataLength = 3;
```

SET:

```c
Data = "A\000C";
DataLength = 3;
```

PRINT must use exact length-aware output whenever the statement-local current value contains NUL.

All decisions must use statement-local values.

---

# 34. COBOL generation

COBOL must emit a real `MOVE` for SET.

Because current values remain traceable, complex SET right sides may lower to exact canonical text.

Example:

```smile
SET Counter = Counter + 1
```

may become a MOVE of the proven result.

## Storage sizing

Analyze every value assigned by LET or SET.

String storage must fit the maximum UTF-8 byte length that the variable can hold.

Block Strings use their normalized UTF-8 value, including logical newline bytes.

## Logical length

Preserve exact output without fixed-width padding.

Maintain a logical length field or another exact mechanism.

Empty Strings, logical newlines, and embedded NUL must remain exact.

Do not omit SET from generated COBOL.

---

# 35. MASM x64 generation

Use the current pointer-plus-length strategy.

For each LET and SET value:

1. emit deterministic static exact UTF-8 data;
2. update runtime pointer;
3. update runtime length.

Conceptual SET:

```asm
lea rax, nameSet1Value
mov QWORD PTR [namePtr], rax
mov DWORD PTR [nameLength], nameSet1ValueLength
```

A block String is stored as its normalized exact UTF-8 bytes, including logical line feeds.

A direct variable PRINT after SET should use the variable's current runtime pointer and length.

Do not omit the runtime update.

---

# 36. Statement-local generator facts

Refactor helpers that assume one permanent LET value.

Replace concepts equivalent to:

```text
ConstantValues(BoundProgram)
```

with statement-aware trace information.

Review every use in:

- C;
- Objective-C;
- C++;
- Python;
- COBOL;
- MASM;
- Integer profiling;
- NUL detection;
- String equality;
- String interpolation;
- tests.

Never use final state for an earlier PRINT or SET.

---

# 37. Source text preservation

Trailing spaces and tabs can be meaningful block content.

The following must preserve the space after `Hello`:

```smile
SET Message ="
Hello 
World
"
```

Resulting value:

```text
Hello \nWorld
```

Do not add automatic trimming to:

- editor text;
- file loading;
- file saving;
- CLI source reading;
- test source normalization;
- requirements/example copying.

Add regression tests that inspect the exact bound String value and output bytes.

---

# 38. Syntax highlighting

Update the SMILE highlighter so:

- `SET` is highlighted as a keyword;
- a SET Block String Literal remains highlighted as one String across physical lines;
- quotes inside ordinary content lines do not terminate highlighting;
- the closing delimiter line ends String highlighting;
- highlighting resumes normally after the block;
- unterminated blocks do not crash the editor;
- rapid typing and target switching remain responsive.

If AvalonEdit XSHD cannot model the delimiter rule safely, use the smallest maintainable solution.

Do not run full compiler parsing on the UI thread merely to color text.

---

# 39. CLI and desktop sample

Update the learning sample without making it cluttered.

Suggested sample:

```smile
LET Name = ""
LET Counter = 0

SET Name ="
S
 I
  N
"

PRINT Hello:
PRINT {Name}
PRINT Counter={Counter}

SET Counter = Counter + 1
SET Name = "Louiery"

PRINT Hello {Name}. Counter={Counter}
```

Update:

- About version;
- syntax highlighting;
- CLI examples;
- diagnostics descriptions;
- current-feature documentation.

Preserve all ten target selectors and responsive live preview.

---

# 40. Official documentation updates

Publish:

```text
docs/SMILE Language Specification/SMILE - SET Statement Official Specification v1.0.md
```

Update:

- LET official specification;
- PRINT official specification;
- String Literals official specification;
- Core Types and Expressions specification;
- README;
- AGENTS;
- Architecture;
- Roadmap;
- Target Code Generation Standard;
- requirements/history;
- desktop version/About metadata.

## LET specification

Clarify:

```text
LET declares and initializes.
SET changes current value.
Type remains fixed.
SET Block String Literals are not allowed in LET.
```

## PRINT specification

Clarify:

```text
PRINT reads current runtime values.
SET Block String Literals are not allowed directly in PRINT.
```

## String specification

Clarify:

```text
Ordinary String expressions remain one-line.
SET has one special block String source form defined by the SET specification.
This does not make general String expressions multiline-capable.
```

## Core expressions

Clarify:

```text
Variable references read current environment state.
Assignment is a statement, not an expression.
SET Block String syntax is normalized before binding and is not a general expression feature.
```

---

# 41. AGENTS.md additions

Preserve all existing rules.

Add wording equivalent to:

> LET declares and initializes a variable. SET is the only assignment statement in v0.5.0 and changes an existing variable without changing its type.

Add:

> Current runtime state belongs to the evaluator environment, not permanently to BoundLetStatement.

Add:

> Compile-time propagation must be statement-order and mutation aware. Never reuse an old known value after SET.

Add:

> Low-level targets may lower a provably known SET value, but they must emit an actual target storage update.

Add:

> A SET Block String Literal is a SET-only complete-value source form. Its delimiter lines are excluded, content-line boundaries become logical line feeds, and the closing delimiter's indentation margin is removed from matching content lines.

Add:

> Source tooling must not trim trailing spaces or tabs because block String content may depend on them.

Add:

> Block String normalization belongs entirely to the front end. Target generators receive only the normalized ordinary String value.

Add:

> Destination-language expansion remains frozen at ten targets.

---

# 42. Version

Update to:

```text
SMILE v0.5.0 — Runtime Variables, SET, and SET Block String Literals
```

Keep all project, README, roadmap, About, and assembly/package versions aligned.

---

# 43. Required ordinary SET parser and binder tests

Add valid tests:

```smile
LET Counter = 0
SET Counter = 1
```

```smile
LET Counter = 0
SET counter = Counter + 1
```

```smile
LET Name = "Sin"
SET Name = Name + " Cioco"
```

```smile
LET Ready = FALSE
SET Ready = NOT Ready
```

Add invalid tests:

```smile
SET = 1
```

Expected `SMILE1301`.

```smile
LET Counter = 0
SET Counter 1
```

Expected `SMILE1302`.

```smile
LET Counter = 0
SET Counter =
```

Expected `SMILE1303`.

```smile
SET Counter = 1
```

Expected `SMILE1304`.

```smile
LET Counter = 0
SET Counter = "One"
```

Expected `SMILE1305`.

```smile
LET SET = 1
```

must fail as a reserved identifier.

---

# 44. Required block String normalization tests

## Basic example

```smile
LET Name = ""

SET Name ="
S
 I
  N
"

PRINT {Name}
```

Expected value:

```text
S\n I\n  N
```

Expected visible output:

```text
S
 I
  N
```

## Opening whitespace

Both forms must be equivalent:

```smile
SET Name ="
```

```smile
SET Name = "
```

## Top-level indentation preservation

Verify one and two leading spaces in content remain exactly.

## Structural indentation

```smile
    LET Name = ""

    SET Name ="
    S
     I
      N
    "
```

Expected:

```text
S\n I\n  N
```

## Line without full margin

Verify it remains unchanged.

## Blank line

Verify `First\n\nThird`.

## Empty block

Verify empty String.

## Leading newline

Verify `\nHello`.

## No automatic trailing newline

Verify `Hello`.

## Intentional trailing newline

Verify `Hello\n`.

## Two trailing newlines

Verify `Hello\n\n`.

## Trailing spaces

Verify exact spaces before logical newline.

## Tabs

Test tabs in structural margin and content.

## Quotes

Verify quotes in content lines remain literal.

## Content line containing only a quote

Use `\"` and verify one quote character.

## Escapes

Verify all official escapes.

## Embedded NUL

Verify exact bytes.

## CRLF and LF

Feed logically equivalent sources with different physical line endings and verify identical bound String values.

---

# 45. Required invalid block String tests

## LET placement

```smile
LET Name ="
S
 I
 N
"
```

Expected `SMILE1306`.

## PRINT placement

```smile
PRINT "
S
 I
 N
"
```

Expected `SMILE1306`.

## Concatenation after block

```smile
LET Name = ""

SET Name ="
S
 I
 N
" + "!"
```

Expected `SMILE1307`.

## Prefix expression

```smile
LET Name = ""
LET Prefix = "X"

SET Name = Prefix + "
S
 I
 N
"
```

Expected `SMILE1306` or the precisely documented block-opening diagnostic.

## Parenthesized form

Reject.

## Interpolated opening

```smile
SET Message =$"
Hello {Name}
"
```

Reject.

## Opening quote not at physical line end

Reject under documented ordinary String or `SMILE1308` behavior.

## Unterminated block

Expected `SMILE1003`.

## Text after closing quote

Expected `SMILE1307`.

---

# 46. Required evaluator tests

## Sequential Integer mutation

```smile
LET Counter = 0
PRINT {Counter}
SET Counter = Counter + 1
PRINT {Counter}
SET Counter = Counter + 2
PRINT {Counter}
```

Expected:

```text
0
1
3
```

## String mutation

```smile
LET Name = "Sin"
PRINT {Name}
SET Name = "Louiery"
PRINT {Name}
```

Expected:

```text
Sin
Louiery
```

## Boolean mutation

Expected `FALSE`, then `TRUE`.

## Later LET sees current state

Expected `15`.

## Case-insensitive assignment

Expected updated value.

## Old value on RHS

Expected incremented value.

---

# 47. Required mutation and simplification tests

## Updated value replaces old propagated value

```smile
LET Flag = FALSE
SET Flag = TRUE
PRINT {Flag}
```

Expected `TRUE`.

## Short circuit after SET

```smile
LET Flag = FALSE
SET Flag = TRUE
PRINT {Flag OR (1 / 0 = 0)}
```

Expected `TRUE`.

## Reverse short circuit

```smile
LET Flag = TRUE
SET Flag = FALSE
PRINT {Flag AND (1 / 0 = 0)}
```

Expected `FALSE`.

## Earlier PRINT differs from later PRINT

Expected `1`, then `2`.

---

# 48. Required block String tests across all ten targets

Run every installed target.

## Basic block

Use `S\n I\n  N`.

## Structurally indented block

Verify exact margin removal.

## Blank line

Verify exact double newline.

## Leading newline

Verify exact first byte is line feed.

## Intentional trailing newline

Verify exact trailing line feed before PRINT's own newline.

## Trailing spaces

Compare exact bytes.

## Quotes

Verify exact quotes.

## Embedded NUL

Compare exact bytes.

## Reassignment

Assign an ordinary String, then a block String, then another ordinary String.

Every statement-local output must match `SmileEvaluator`.

---

# 49. Required String hardening tests with SET

## NUL assignment

```smile
LET Data = "A\0B"
PRINT {Data}

SET Data = "A\0C"
PRINT {Data}
```

## NUL-free to NUL

```smile
LET Data = "ABC"
SET Data = "A\0B"
PRINT {Data}
```

## NUL to NUL-free

```smile
LET Data = "A\0B"
SET Data = "XYZ"
PRINT {Data}
```

## Equality after SET

Expected `FALSE`.

## Block String with NUL

Preserve exact bytes and logical newlines.

---

# 50. Required Integer-profile tests with SET

## SET introduces wide value

```smile
LET Value = 1
SET Value = 5000000000
PRINT {Value}
```

## SET intermediate introduces wide result

```smile
LET Value = 1
SET Value = 50000 * 50000
PRINT {Value}
```

## JavaScript BigInt due to SET

```smile
LET Value = 1
SET Value = 3100000000 * 3000000
PRINT {Value}
```

The complete JavaScript program must use BigInt consistently.

---

# 51. Required generated-code structural tests

Verify natural assignment forms.

## C#

```csharp
int Counter = 0;
Counter = Counter + 1;
```

## C

```c
int Counter = 0;
Counter = Counter + 1;
```

## C++

```cpp
int Counter = 0;
Counter = Counter + 1;
```

## JavaScript

```javascript
let Counter = 0;
Counter = Counter + 1;
```

## Java

```java
int Counter = 0;
Counter = Counter + 1;
```

## Python

```python
Counter = 0
Counter = Counter + 1
```

## Swift

```swift
var Counter: Int = 0
Counter = Counter + 1
```

## Objective-C

```objc
int Counter = 0;
Counter = Counter + 1;
```

## COBOL

Generated source must contain a real `MOVE` for SET.

## MASM

Generated source must update runtime pointer and length at the SET statement.

For a block String, generated code must contain only the normalized value. It must not contain SMILE delimiter detection or indentation-removal logic.

---

# 52. All-ten-target acceptance program

Use:

```smile
LET Counter = 0
LET Name = ""
LET Ready = FALSE

SET Name ="
S
 I
  N
"

PRINT Counter={Counter}, Ready={Ready}
PRINT Name:
PRINT {Name}

SET Counter = Counter + 1
SET Name = "Louiery"
SET Ready = TRUE

PRINT Counter={Counter}, Name={Name}, Ready={Ready}

SET Counter = Counter + 2
LET Message = $"{Name} finished with {Counter}."
PRINT {Message}
```

Expected:

```text
Counter=0, Ready=FALSE
Name:
S
 I
  N
Counter=1, Name=Louiery, Ready=TRUE
Louiery finished with 3.
```

For every installed target:

1. evaluate with `SmileEvaluator`;
2. generate source;
3. build/run;
4. verify exit code zero;
5. compare exact stdout except CRLF/LF normalization where appropriate;
6. use byte comparisons for NUL and trailing-whitespace cases.

---

# 53. Deterministic generation

Generate the same SET and block String programs twice for all ten targets.

Compare every generated file byte-for-byte.

Source spans, labels, storage names, support names, and assignment lowering must remain deterministic.

---

# 54. Desktop validation

1. Launch SMILE Desktop.
2. Enter the all-ten-target acceptance program.
3. Confirm `SET` highlighting.
4. Confirm block String highlighting spans physical lines.
5. Confirm quotes inside content do not terminate highlighting.
6. Confirm the closing delimiter ends highlighting.
7. Confirm trailing spaces remain in source.
8. Confirm all generated panes update.
9. Inspect C, C++, Python, Swift, COBOL, and MASM.
10. Confirm Swift uses `var` only for mutated variables.
11. Build/run every installed target.
12. Confirm exact output.
13. Test structural indentation.
14. Test blank content line.
15. Test intentional trailing newline.
16. Test trailing spaces.
17. Test embedded NUL.
18. Test wide Integer introduced by SET.
19. Rapidly switch targets.
20. Confirm responsiveness.
21. Test cancellation.
22. Confirm recoverable failure keeps the IDE open.
23. Confirm About shows v0.5.0.

---

# 55. Performance and responsiveness

Do not run target toolchains on the WPF UI thread.

Preserve:

- debounced live transpilation;
- source-revision caching;
- visible-target generation;
- cancellation;
- timeouts;
- bounded output;
- failure containment.

Block scanning must be linear in block/source length.

Do not repeatedly rescan the complete source for each content line.

---

# 56. Scope exclusions

Do not implement:

- `IF`;
- `THEN`;
- `ELSE`;
- `INPUT`;
- loops;
- functions;
- scopes;
- arrays;
- floating-point;
- comments;
- assignment expressions;
- `+=`;
- `++`;
- multiple assignment;
- destructuring;
- general block Strings;
- block Strings in LET;
- block Strings in PRINT;
- block Strings in function arguments;
- block interpolation;
- block concatenation;
- another destination language;
- Rust;
- Zig;
- Go;
- a feature branch.

---

# 57. Acceptance criteria

The task is complete only when all are true:

1. `SET` is a case-insensitive keyword.
2. SET has one canonical syntax node.
3. SET has one canonical bound node.
4. SET does not declare variables.
5. undefined target produces `SMILE1304`.
6. type mismatch produces `SMILE1305`.
7. missing target produces `SMILE1301`.
8. missing equals produces `SMILE1302`.
9. missing value produces `SMILE1303`.
10. SET is reserved as a variable name.
11. LET declares and initializes.
12. variable type remains fixed.
13. RHS sees old value.
14. assignment updates after full evaluation.
15. PRINT sees current state.
16. later LET sees updated state.
17. case-insensitive assignment works.
18. BoundLetStatement no longer owns permanent current state.
19. runtime state lives in evaluator/environment.
20. shared sequential trace exists.
21. simplification is mutation-aware.
22. short-circuit uses current state.
23. Integer profiles include SET values and intermediates.
24. SET Block String token or source representation exists.
25. opening quote must end SET line.
26. closing quote must be only non-whitespace on its line.
27. delimiter lines are excluded.
28. content line boundaries become logical `\n`.
29. no leading newline is automatic.
30. no trailing newline is automatic.
31. intentional leading/trailing newlines work through empty content lines.
32. structural indentation margin comes from closing delimiter.
33. matching margin is removed exactly.
34. additional indentation is preserved.
35. nonmatching content lines are preserved.
36. trailing spaces/tabs are preserved.
37. quotes inside content are preserved.
38. official escapes work.
39. embedded NUL works.
40. block Strings do not interpolate.
41. block String is complete SET value only.
42. block String is rejected in LET.
43. block String is rejected in PRINT.
44. block concatenation is rejected.
45. block interpolation is rejected.
46. unterminated block reports `SMILE1003`.
47. unexpected content after closing delimiter reports `SMILE1307`.
48. source tooling does not trim meaningful whitespace.
49. block value binds as ordinary String.
50. no target generator parses block syntax.
51. C# emits real assignment.
52. C emits real assignment.
53. C++ emits real assignment.
54. JavaScript emits real assignment.
55. Java emits real assignment.
56. Python emits real assignment.
57. Swift uses `var` for mutated symbols.
58. Objective-C emits real assignment.
59. COBOL emits real MOVE.
60. MASM updates runtime storage.
61. embedded-NUL SET is exact.
62. block embedded-NUL SET is exact.
63. String equality after SET is exact.
64. all ten targets remain supported.
65. destination-language expansion remains frozen.
66. specifications are synchronized.
67. Debug build has zero warnings.
68. Release build has zero warnings.
69. Debug tests pass.
70. Release tests pass.
71. all installed targets run with exit code zero.
72. all installed targets match `SmileEvaluator`.
73. generation remains deterministic.
74. desktop remains responsive.
75. no unrelated feature is added.
76. no unapproved dependency is added.
77. no build artifacts are committed.
78. all work is performed directly on `main`.

---

# 58. Suggested implementation sequence

1. Confirm the v0.4.3.1 baseline.
2. Publish the complete official SET specification.
3. Add SET keyword and highlighting keyword.
4. Add block String lexical recognition.
5. Add normalization tests before parser integration.
6. Add SetStatementSyntax.
7. Parse ordinary SET.
8. Parse block SET as complete RHS.
9. Add placement and delimiter diagnostics.
10. Add BoundSetStatement.
11. Remove permanent ConstantValue from BoundLetStatement.
12. Implement binder rules.
13. Create sequential execution/state trace.
14. Update SmileEvaluator.
15. Make simplifier mutation-aware.
16. Update Integer profiling.
17. Update C#.
18. Update C++.
19. Update JavaScript.
20. Update Java.
21. Update Python.
22. Update Swift mutation declarations.
23. Update C and Objective-C.
24. Update COBOL.
25. Update MASM.
26. Refactor statement-local generator helpers.
27. Add exact block String all-target tests.
28. Run Debug validation.
29. Run Release validation.
30. Run all ten targets.
31. Perform desktop smoke testing.
32. Update documentation.
33. Commit directly to `main` only when Sin explicitly authorizes it.

---

# 59. Validation commands

Run from the repository root:

```bat
cmd /c git status --short --branch
```

Confirm:

```text
main
```

```bat
cmd /c dotnet restore SMILE.sln
```

```bat
cmd /c dotnet build SMILE.sln -c Debug -nologo
```

```bat
cmd /c dotnet test SMILE.sln -c Debug --no-build -nologo
```

```bat
cmd /c dotnet build SMILE.sln -c Release -nologo
```

```bat
cmd /c dotnet test SMILE.sln -c Release --no-build -nologo
```

Generate all ten targets:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- <SET_AND_BLOCK_STRING_ACCEPTANCE_EXAMPLE.smile> --target all
```

Run each installed target explicitly with `--run`.

Before an authorized commit:

```bat
cmd /c git diff --check
```

```bat
cmd /c git diff --stat
```

```bat
cmd /c git status --short --branch
```

---

# 60. Completion report

Report:

- exact baseline commit;
- exact files changed;
- official SET specification path;
- block String token and syntax design;
- opening and closing delimiter recognition;
- exact structural-margin algorithm;
- CRLF/LF normalization;
- exact leading/trailing newline behavior;
- exact trailing-whitespace preservation proof;
- quote and escape behavior;
- placement diagnostics;
- bound representation;
- removal/replacement of `BoundLetStatement.ConstantValue`;
- runtime environment;
- execution trace;
- simplifier mutation handling;
- Integer-profile mutation handling;
- per-target assignment strategy;
- Swift `var` analysis;
- C/Objective-C String strategy;
- COBOL storage and MOVE strategy;
- MASM pointer/length strategy;
- exact Debug test count;
- exact Release test count;
- zero-warning results;
- all-ten-target runtime results;
- exact-byte block results;
- desktop smoke results;
- documentation updates;
- unresolved concerns.

Do not claim completion if:

- delimiter lines leak into the value;
- structural indentation is removed incorrectly;
- trailing spaces are normalized away;
- block Strings work outside SET;
- a target generator contains block parsing;
- low-level targets omit SET updates.

---

# 61. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

Suggested subject:

```text
Sin and Codex: Add runtime variables and SET
```

Suggested commit-body topics:

- official SET statement;
- SET Block String Literal — The SMILE Way;
- mutable evaluator environment;
- BoundSetStatement;
- statement-aware execution trace;
- mutation-aware simplification;
- all-ten-target assignments;
- exact block newline, indentation, quote, whitespace, and embedded-NUL behavior;
- Swift mutable declarations;
- exact Debug/Release validation totals.
