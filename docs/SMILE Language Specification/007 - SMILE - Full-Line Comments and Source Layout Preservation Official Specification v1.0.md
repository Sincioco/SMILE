# SMILE — Full-Line Comments and Source Layout Preservation Official Specification v1.0

## Status

This document is the complete official language specification for:

> **SMILE v0.6.1 — Full-Line Comments and Source Layout Preservation**

It defines:

1. four equivalent SMILE full-line comment forms;
2. preservation of those comments in generated target source;
3. preservation of blank source lines in generated target source;
4. the non-semantic source-layout model needed to support both features safely.

This specification works together with the official SMILE specifications for LET, SET, INPUT, PRINT, IF, WHILE, String literals, and core expressions.

**Repository destination:** `docs/SMILE Language Specification/007 - SMILE - Full-Line Comments and Source Layout Preservation Official Specification v1.0.md`

- [001 - SMILE - SET Statement Official Specification v1.0](001%20-%20SMILE%20-%20SET%20Statement%20Official%20Specification%20v1.0.md)
- [002 - SMILE - PRINT Statement Official Specification v1.0](002%20-%20SMILE%20-%20PRINT%20Statement%20Official%20Specification%20v1.0.md)
- [003 - SMILE - String Literals Official Specification v1.0](003%20-%20SMILE%20-%20String%20Literals%20Official%20Specification%20v1.0.md)
- [004 - SMILE - Core Types and Expressions Official Specification v1.0](004%20-%20SMILE%20-%20Core%20Types%20and%20Expressions%20Official%20Specification%20v1.0.md)
- [005 - SMILE - LET Statement Official Specification v1.0](005%20-%20SMILE%20-%20LET%20Statement%20Official%20Specification%20v1.0.md)
- [006 - SMILE - IF Statement Official Specification v1.0](006%20-%20SMILE%20-%20IF%20Statement%20Official%20Specification%20v1.0.md)
- [008 - SMILE - INPUT Statement Official Specification v1.0](008%20-%20SMILE%20-%20INPUT%20Statement%20Official%20Specification%20v1.0.md)
- [009 - SMILE - WHILE Statement Official Specification v1.0](009%20-%20SMILE%20-%20WHILE%20Statement%20Official%20Specification%20v1.0.md)

---

# 1. Purpose

SMILE source code should remain readable after transpilation.

A programmer may write:

```smile
REM Store the learner's age.
LET Age = 49

// Display the current value.
PRINT {Age}
```

A C# target should preserve the author's comment and blank-line boundary using C# syntax:

```csharp
// Store the learner's age.
int Age = 49;

// Display the current value.
Console.WriteLine(Age);
```

A Python target should preserve the same source layout using Python syntax:

```python
# Store the learner's age.
Age = 49

# Display the current value.
print(Age)
```

Generated details may differ according to the destination language and the existing SMILE target-generation rules. The relative source order, comment text, and source-authored blank-line boundaries must be retained under this specification.

---

# 2. Supported SMILE full-line comment markers

SMILE accepts these four equivalent full-line comment markers:

| SMILE marker | Familiar origin or usage |
|---|---|
| `REM` | Traditional BASIC |
| `//` | C, C++, C#, Java, JavaScript, and related languages |
| `#` | Python, Ruby, Perl, shell languages, and related tools |
| `--` | SQL, Ada, Haskell, and related languages |

All four forms have identical SMILE semantics.

Example:

```smile
REM Traditional BASIC comment
// C-family comment
# Script-language comment
-- SQL/Ada/Haskell-style comment
```

---

# 3. Core recognition rule

A physical source line is a SMILE full-line comment only when a supported marker begins at the first non-horizontal-whitespace position on that line.

For this specification, horizontal whitespace means:

```text
space U+0020
tab   U+0009
```

Valid:

```smile
REM Comment at column 1
    REM Comment after spaces
	REM Comment after a tab

// Comment at column 1
    // Comment after spaces

# Comment at column 1
    # Comment after spaces

-- Comment at column 1
    -- Comment after spaces
```

Once recognized, every remaining character on that physical line belongs to the comment payload.

---

# 4. Formal source-item grammar

A SMILE statement list contains semantic statements and non-semantic source-layout items.

```text
source-item-list ->
    source-item*

source-item ->
    blank-line
    | full-line-comment
    | statement

blank-line ->
    hspace* line-end

full-line-comment ->
    hspace* rem-comment line-end-or-eof
    | hspace* slash-slash-comment line-end-or-eof
    | hspace* hash-comment line-end-or-eof
    | hspace* dash-dash-comment line-end-or-eof

rem-comment ->
    REM rem-boundary comment-payload?

rem-boundary ->
    hspace
    | line-end
    | end-of-file

slash-slash-comment ->
    '//' comment-payload?

hash-comment ->
    '#' comment-payload?

dash-dash-comment ->
    '--' comment-payload?

comment-payload ->
    zero or more characters belonging to the same physical line

hspace ->
    space
    | tab

line-end ->
    CRLF
    | LF
    | CR

line-end-or-eof ->
    line-end
    | end-of-file
```

A comment or blank line is not an executable statement.

---

# 5. Case-insensitive `REM`

`REM` is case-insensitive.

All of these are equivalent:

```smile
REM Comment
rem Comment
Rem Comment
rEm Comment
```

Case comparison is ordinal and culture-independent.

The symbolic markers are the exact character sequences:

```text
//
#
--
```

---

# 6. `REM` boundary rule

`REM` is recognized as a comment marker only when immediately followed by:

- a space;
- a tab;
- a physical line ending; or
- end-of-file.

Valid:

```smile
REM
rem
Rem A comment
rEm	A comment
```

Not comments:

```smile
REMEMBER this
REMARK this
REMOTE this
REM:this
REM#this
```

The boundary rule prevents identifiers or future words beginning with `REM` from being silently discarded.

---

# 7. `REM` remains contextual

`REM` is a comment marker only in the first-non-whitespace position defined by this specification.

It does not become a globally reserved expression keyword.

This remains valid:

```smile
LET REM = "A variable named REM"
PRINT {REM}
```

Output:

```text
A variable named REM
```

A parser, formatter, or future full-fidelity syntax tree may classify first-position `REM` as comment trivia while continuing to classify `REM` elsewhere as an identifier.

---

# 8. Symbol-marker spacing

The symbolic markers do not require whitespace after the marker.

All of these are valid comments:

```smile
//comment
//// decorative separator
#comment
## heading
--comment
---- decorative separator
```

The exact source remainder after the recognized marker becomes the comment payload.

---

# 9. Full-line comments only

SMILE Full-Line Comments v1.0 supports full-line comments only.

A marker appearing after any non-whitespace source content on the same physical line does not begin a comment.

These do not contain SMILE comments:

```smile
LET X = 1 // not an inline comment
LET Y = 2 # not an inline comment
SET X = X + 1 -- not an inline comment
IF X = 2 THEN // not an inline comment
END IF -- not an inline comment
```

They are processed by the ordinary grammar and existing diagnostics.

This version does not support:

- trailing comments after statements;
- end-of-line comments after expressions;
- block comments;
- nested comments;
- documentation comments as a distinct semantic form.

---

# 10. PRINT raw-template behavior remains unchanged

PRINT raw-template payloads begin after the PRINT keyword. A marker inside that payload is data because PRINT is the first non-whitespace source content.

Valid:

```smile
PRINT // This text is printed
PRINT # This text is printed
PRINT -- This text is printed
PRINT REM This text is printed
PRINT https://example.com
```

Output:

```text
// This text is printed
# This text is printed
-- This text is printed
REM This text is printed
https://example.com
```

---

# 11. Ordinary and interpolated Strings

Comment markers inside ordinary quoted Strings, interpolated Strings, interpolation expressions, or PRINT text are not comments.

Valid:

```smile
LET SlashText = "//"
LET HashText = "#"
LET DashText = "--"
LET RemText = "REM"

PRINT $"Markers: {SlashText} {HashText} {DashText} {RemText}"
```

Output:

```text
Markers: // # -- REM
```

---

# 12. Block String Literals

Comment and blank-line recognition is suspended inside a Block String Literal used as the complete value of LET or SET.

Every physical content line belongs to the String until the existing closing-delimiter rule ends the block.

Example:

```smile
LET Text = ""

SET Text ="
REM This is String data.

// This blank line is String data.
# This is String data.
-- This is String data.
"

PRINT {Text}
```

Marker-looking lines are preserved as String content. Blank content lines are preserved according to the existing Block String normalization rules.

The comment recognizer must not remove, classify, highlight as comments, or transpile those content lines separately.

---

# 13. Blank source lines

A blank source line contains only zero or more spaces or tabs outside a Block String Literal.

Example:

```smile
LET a = 49

PRINT a
```

The source contains one blank line between LET and PRINT.

Each source-authored blank line is a non-semantic source-layout item.

Whitespace characters on a blank line do not need to be copied to the target. The generated target line should be physically empty.

---

# 14. Consecutive, leading, and trailing blank lines

Consecutive blank lines are preserved as consecutive source-originated blank target lines.

Leading blank lines in a statement list are preserved inside the corresponding generated user-code body after required target boilerplate.

Trailing blank lines in a statement list are preserved before the corresponding generated closing construct or mandatory target epilogue.

Examples of statement lists include:

- the program body;
- an IF clause body;
- an ELSE IF clause body;
- an ELSE body;
- a WHILE body;
- a future function body.

Generator-owned formatting may add additional blank lines. It must not erase a source-authored blank line.

---

# 15. Comments and blank lines inside IF and WHILE

Comments and blank lines may appear anywhere a blank line may appear in an IF-related body.

They may likewise appear in a WHILE body without counting as an executable iteration statement:

```smile
LET Count = 0

WHILE Count < 2
    // This comment remains in the generated loop body.

    SET Count = Count + 1
END WHILE
```

Valid:

```smile
LET Score = 85
LET Grade = ""

IF Score >= 90 THEN
    REM Highest grade

    SET Grade = "A"
ELSE IF Score >= 80 THEN
    // Selected branch

    SET Grade = "B"
ELSE
    # Fallback branch
    -- Adjacent comment forms are allowed.

    SET Grade = "C"
END IF

PRINT {Grade}
```

A branch containing only comments and blank lines is semantically empty.

Target languages that require an executable placeholder for an empty body must still generate that placeholder. Comments do not count as executable statements.

Examples include:

- Python `pass`;
- COBOL `CONTINUE` when required by the existing generator.

Comments and blank lines may surround INPUT exactly as they surround PRINT or SET:

```smile
LET Age = 0

// Read one runtime line.
INPUT Age

PRINT {Age}
```

The two blank boundaries and native target comment are preserved. The INPUT remains one executable statement at the same source-order position. A comment whose payload contains `INPUT Age` does not read input, and marker-looking INPUT text inside a Block String remains String data.

---

# 16. Comment text cannot alter block structure

Keywords and delimiters inside comment payloads have no structural meaning.

Valid:

```smile
IF Ready = TRUE THEN
    // ELSE
    # END IF
    -- IF FALSE = TRUE THEN
    REM SET Text ="

    PRINT Ready
END IF
```

The comment text does not open, close, or redirect an IF or WHILE statement.

The same rule applies during parser recovery, including maximum combined IF/WHILE-depth recovery.

---

# 17. Source layout is retained as non-semantic metadata

Comments and blank lines must be retained in the ordered source tree as non-semantic layout items.

They must not:

- declare variables;
- read variables;
- change values;
- perform I/O;
- create execution-trace steps;
- affect Known/Unknown analysis;
- affect Integer profiles;
- affect String size or NUL planning;
- alter runtime output;
- satisfy a target language's requirement for an executable statement.

In particular, layout cannot consume an input line, reset a runtime error, or change which branch-local INPUT executes.

The compiler may use a shared ordered-item base internally, but comments and blank lines are not semantic statements.

---

# 18. Generated comment preservation

Each valid SMILE full-line comment is emitted in the primary generated source file using the destination language's native full-line comment syntax.

The original SMILE marker is replaced by the target marker.

The comment payload is retained in source order.

Example:

```smile
REM  Preserve these two spaces.
```

C#:

```csharp
//  Preserve these two spaces.
```

Python:

```python
#  Preserve these two spaces.
```

The source indentation before the marker is not copied verbatim. The target generator applies the indentation required by the destination construct.

The remainder after the source marker is preserved, subject only to the target-safety rules in this specification.

---

# 19. Target comment mapping

| Destination target | Generated full-line comment marker |
|---|---|
| C# | `//` |
| C | `//` |
| C++ | `//` |
| JavaScript | `//` |
| Java | `//` |
| Objective-C | `//` |
| Swift | `//` |
| Python | `#` |
| COBOL free source format | `*>` |
| Windows x64 MASM | `;` |

The mapping is fixed for this specification.

Comments are emitted only into the primary generated program file, not automatically into ancillary project or launcher files.

---

# 20. Target comment safety

A source comment must never be able to terminate the generated target comment early or change generated program behavior.

Target comment emitters must sanitize payload characters that are unsafe in a destination source file.

At minimum, account for:

- target-recognized line-separator characters such as U+2028 and U+2029;
- NUL and other unsafe control characters;
- a trailing backslash in C, C++, or Objective-C, where physical-line splicing could affect the next line;
- Java Unicode-escape sequences that are processed before ordinary comment recognition;
- destination source-encoding limitations.

A permitted safe representation is:

```text
\u{HEX}
```

using uppercase hexadecimal Unicode scalar values.

Sanitization must be deterministic and lossless at the logical-character level.

Ordinary printable text should remain ordinary readable text.

---

# 21. Long target comment lines

Some destination toolchains impose practical source-line limits.

A target generator may split one long source comment into multiple target-native comment lines when needed for compilation safety.

Requirements:

1. emitted fragments remain consecutive;
2. fragment order is preserved;
3. no payload character is dropped;
4. no payload character is invented;
5. target indentation and marker are repeated on each generated line;
6. runtime behavior is unchanged;
7. generation is deterministic.

For GnuCOBOL free source, generated comment lines must remain safely below its effective source-line limit.

Normal-length comments should remain one generated line.

---

# 22. Generated blank-line preservation

Each blank SMILE source line outside a Block String causes one explicit blank line in the corresponding generated source-order body.

Example SMILE:

```smile
LET a = 49

PRINT a
```

Representative C# layout:

```csharp
int a = 49;

Console.WriteLine("a");
```

Representative JavaScript layout:

```javascript
let a = 49;

console.log("a");
```

The exact generated statement text remains governed by existing target-generation standards.

---

# 23. Relative placement, not identical target line numbers

SMILE source statements may expand into multiple target lines.

Some targets also require:

- namespaces or classes;
- function wrappers;
- data sections;
- procedure divisions;
- declarations hoisted away from executable code;
- generated runtime helpers;
- labels and jump instructions.

Therefore, source and target physical line numbers are not required to match.

The preservation contract is:

- source comments remain in relative source order;
- source blank-line boundaries remain represented;
- comments and blank lines appear in the nearest structurally corresponding user-code region;
- one semantic statement's generated chunk is treated as one source-order unit;
- target boilerplate and helper regions remain generator-owned.

For targets that keep declarations in source order, a blank line between LET and PRINT appears directly between their generated chunks.

For structurally split targets such as COBOL, a declaration may live in WORKING-STORAGE while executable layout is emitted in PROCEDURE DIVISION. The generator must preserve each source comment once and preserve the blank-line boundary at the closest deterministic source-order location without duplicating layout items.

---

# 24. Generator-owned formatting

Generators may retain their own blank lines needed for readability or target structure.

Source-originated blank lines are additive. A generator must not claim an existing unrelated boilerplate blank line as the preserved source line unless that mapping is deterministic and tested.

Generated output may therefore contain more blank lines than the SMILE source, but not fewer source-originated layout boundaries in the corresponding user-code region.

---

# 25. Comment-only and blank-only programs

A program containing only comments and blank lines is valid.

Example:

```smile
REM This program intentionally performs no operation.

// It produces no output.
# It declares no variables.
-- It still generates valid target source.
```

The evaluator output is empty.

Every target must still generate a valid program. Required target boilerplate or no-op constructs remain allowed.

The comments and blank lines are preserved in the generated user-code body.

---

# 26. Source positions and diagnostics

Preserving or ignoring comment semantics must not change physical source positions.

Example:

```smile
REM Line 1
// Line 2

# Line 4
BROKEN
```

The diagnostic for `BROKEN` identifies physical line 5.

Valid comments and blank lines produce no diagnostics.

No dedicated malformed-comment diagnostic is introduced.

Text that does not satisfy the comment recognition rule follows existing grammar and diagnostics.

---

# 27. Public lexer behavior

The public full-source lexer should retain a full-line comment as a comment/trivia token or equivalent lexical item for tooling.

It must:

- preserve marker kind;
- preserve payload;
- preserve source span;
- preserve end-of-line accounting;
- avoid tokenizing comment payload as ordinary SMILE syntax;
- avoid treating inline markers as comments;
- avoid treating Block String content as comments.

The indexed parser remains the authority for statement-list structure.

---

# 28. Syntax-tree representation

A recommended syntax model is:

```csharp
public abstract record SourceItemSyntax(TextSpan Span)
    : SyntaxNode(Span);

public abstract record StatementSyntax(TextSpan Span)
    : SourceItemSyntax(Span);

public sealed record FullLineCommentSyntax(
    FullLineCommentMarker Marker,
    string Payload,
    TextSpan Span)
    : SourceItemSyntax(Span);

public sealed record BlankLineSyntax(TextSpan Span)
    : SourceItemSyntax(Span);
```

Program, branch, and loop bodies should preserve an ordered list of source items.

The exact public API may differ, but comments and blank lines must not be reconstructed later from source offsets or a fragile side table.

---

# 29. Bound source-layout representation

A recommended bound model is:

```csharp
public abstract record BoundSourceItem;

public abstract record BoundStatement
    : BoundSourceItem;

public sealed record BoundFullLineComment(
    FullLineCommentMarker OriginalMarker,
    string Payload)
    : BoundSourceItem;

public sealed record BoundBlankLine()
    : BoundSourceItem;
```

Bound programs, branch bodies, and WHILE bodies preserve ordered source items.

Semantic helpers may expose filtered `BoundStatement` sequences for evaluation and analysis.

Comments and blank lines receive no semantic value or execution fact.

---

# 30. Evaluation and target equivalence

Removing all full-line comments and blank source lines from a valid program must not change:

- binding results;
- evaluator output;
- variable values;
- branch selection;
- target executable behavior;
- target exit code;
- input-line consumption order;
- runtime stderr or runtime-error identity.

Generated source is expected to differ because comments and blank lines are intentionally preserved.

All ten generated executables must continue to match the SMILE evaluator when given identical scripted stdin. Exact stdout, stderr, and exit code must remain unchanged by removing layout.

---

# 31. Determinism

For the same SMILE source and target, preserved comments and blank lines must generate byte-deterministic target files.

Comment sanitization, wrapping, indentation, and blank-line placement must not depend on:

- current culture;
- operating-system source line endings;
- hash iteration order;
- toolchain installation order;
- prior generation requests.

---

# 32. Syntax highlighting

The SMILE Desktop editor should display valid full-line comments using a dedicated Comment style.

Highlighting must:

- recognize all four markers only at first non-whitespace;
- recognize `REM` case-insensitively with its boundary;
- avoid highlighting inline occurrences as comments;
- keep marker-looking text inside ordinary Strings styled as String;
- keep marker-looking lines and blank lines inside Block String Literals owned by the String span;
- highlight INPUT as a case-insensitive keyword only outside Comment and String spans;
- remain safe for incomplete source.

Blank lines require no special color.

---

# 33. Sensitive information

Because comments are intentionally copied into generated source files, programmers must not place secrets, passwords, private keys, tokens, or other sensitive information in source comments.

A future optional “strip comments” generation mode may be specified separately.

The default behavior defined here is preservation.

---

# 34. Backward compatibility

The feature is additive.

Previously valid source remains valid with the same runtime meaning, including:

```smile
LET REM = "Value"
PRINT {REM}
PRINT // literal text
PRINT # literal text
PRINT -- literal text
PRINT REM literal text
```

Lines that previously produced unknown-statement or invalid-character diagnostics become comments only when they satisfy the first-non-whitespace rule.

Previously ignored blank lines now also influence generated source formatting, but not runtime behavior.

---

# 35. Long-term lexical reservation

The four markers are permanently reserved when they begin at the first non-whitespace position under this specification.

Consequences include:

- a future first-position `#` directive would require an explicit language revision;
- a future first-position `--` decrement statement would require an explicit language revision;
- a future statement named `REM` could not use first-position `REM` followed by whitespace.

Other positions remain governed by ordinary grammar.

This is an accepted tradeoff for familiar comment syntax.

---

# 36. Normative acceptance program

```smile
REM Traditional BASIC comment
// C-family comment
# Script-language comment
-- SQL-style comment

LET REM = "REM variable"

LET Score = 85
LET Grade = ""
LET Message = ""

IF Score >= 90 THEN
    // This branch is not selected.

    SET Grade = "A"
ELSE IF Score >= 80 THEN
    # This branch is selected.

    SET Grade = "B"
ELSE
    -- Fallback branch.

    SET Grade = "C"
END IF

SET Message ="
REM String data

// String data
# String data
-- String data
"

PRINT {REM}

PRINT Grade={Grade}

PRINT // Printed raw text

PRINT {Message}
```

Required runtime output:

```text
REM variable
Grade=B
// Printed raw text
REM String data

// String data
# String data
-- String data
```

Generated source requirements:

- all source comments outside the Block String are preserved using the target mapping;
- marker-looking Block String content remains String data;
- all source-authored blank lines are represented in the generated user-code layout;
- runtime output matches the evaluator on all ten targets.

---

# 37. Future compatibility

Future language milestones must preserve these rules unless an explicit newer specification supersedes them:

- full-line comment markers remain first-non-whitespace syntax;
- `REM` remains case-insensitive, contextual, and boundary-sensitive;
- inline comments require a separate specification;
- source comments remain non-semantic;
- blank lines remain non-semantic;
- comments and blank lines remain available to formatters, source maps, documentation tools, and IDE features;
- current WHILE bodies and future loop forms, functions, and scopes use the same ordered source-item model for their bodies;
- generated comment preservation remains target-safe and deterministic.
