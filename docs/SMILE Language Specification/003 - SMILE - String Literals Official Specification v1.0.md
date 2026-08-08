# SMILE - String Literals Official Specification v1.0

This specification was introduced in SMILE v0.4.1 and remains normative for SMILE v0.8.0 and later unless superseded by a newer official specification.

## Purpose

SMILE has two non-interpolated String literal source forms:

- an ordinary one-line String literal; and
- the multiline **Block String Literal — The SMILE Way**.

Both forms bind to the same ordinary SMILE `String` type and the same `BoundStringLiteralExpression`. The block form is source convenience, not a second runtime String kind.

Ordinary String literals may appear wherever the shared expression grammar permits a String literal. A Block String Literal is deliberately narrower: it is valid only as the complete value of `LET` or `SET`. It is not a general expression primary and is not legal directly in `PRINT`, interpolation, concatenation, parentheses, or another nested expression.

SMILE v0.7.0 [INPUT](008%20-%20SMILE%20-%20INPUT%20Statement%20Official%20Specification%20v1.0.md) reads a runtime String value rather than a String literal. Source escapes are not decoded during INPUT.

SMILE v0.8.0 [WHILE](009%20-%20SMILE%20-%20WHILE%20Statement%20Official%20Specification%20v1.0.md) retains these complete-value String rules. LET remains prohibited lexically inside WHILE v1.0, so a Block String inside a WHILE body can currently occur only as a SET value. Every loop-carried String assignment must retain a finite compile-time maximum UTF-8 byte length under the WHILE specification.

## Ordinary String Literal

An ordinary String literal begins with `"` and ends with the next unescaped `"` on the same physical source line.

```basic
"Hello"
"She said \"Hello\"."
"C:\\SMILE"
```

SMILE accepts legacy left and right smart quote characters as double quotes for beginner-friendly recovery, but official and generated examples use ASCII quotes.

## Block String Literal — The SMILE Way

The complete-value grammar is:

```text
let-statement -> LET hspace+ identifier hspace* '=' hspace* let-value
let-value     -> expression | block-string-literal

set-statement -> SET hspace+ identifier hspace* '=' hspace* set-value
set-value     -> expression | block-string-literal
```

The block form does not change the ordinary expression grammar.

### Canonical LET form

```smile
LET MultilineText = "
    Hello World!
    This is SMILE!
        How are you?
"
```

The exact value is `    Hello World!\n    This is SMILE!\n        How are you?`. There is no automatic leading or trailing line feed.

### Canonical SET form

```smile
LET MultilineText = ""

SET MultilineText = "
First line
Second line
"
```

SET continues to change existing storage without changing the type established by LET.

### Opening delimiter

A block begins only when all of these are true:

1. the parser is reading the complete value of LET or SET;
2. the value begins with `"`;
3. only spaces or tabs follow that quote on its physical statement line; and
4. the line then ends.

Horizontal whitespace after the opening quote is structural and is not part of the value. An opening quote followed by other content on the same LET or SET line is not a block opener and reports `SMILE1308` when it has the block-opening shape.

### Closing delimiter

The closing delimiter is a physical line whose only non-horizontal-whitespace character is `"`. Its leading spaces and tabs are the structural indentation margin. Its trailing spaces and tabs are structural. Neither the quote nor its surrounding structural whitespace becomes part of the value.

Ordinary quotes on content lines are data. A content line such as `"Hello"` does not close the block because the line contains non-whitespace text in addition to its quotes.

### Content and logical line feeds

Only physical lines between the delimiters are content. Boundaries between adjacent content lines become one logical LF (`U+000A`). The front end normalizes source CRLF, LF, and standalone CR boundaries to that same logical LF before binding.

The delimiters do not add a line feed. Therefore:

```smile
LET Value = "
First
Second
"
```

has value `First\nSecond`, while:

```smile
LET Value = "

First

"
```

has value `\nFirst\n`.

Empty first, middle, and last content lines, consecutive blank lines, lines containing only spaces or tabs, leading whitespace, and trailing spaces or tabs are significant String data.

### Exact structural-margin removal

Let the closing delimiter's leading sequence of spaces and tabs be the margin. Remove that exact sequence from the start of each content line that begins with the complete same sequence. Leave every nonmatching content line unchanged.

This is exact prefix removal, not common-indent calculation or dedenting:

- a space never matches a tab;
- a tab never matches spaces;
- no minimum indentation is computed;
- mismatching lines are not partly adjusted; and
- remaining spaces and tabs are never converted.

For example:

```smile
LET Value = "
    First
      Second
  Third
    "
```

has exact value `First\n  Second\n  Third`.

### Ownership during parsing and tooling

Once a valid block opener is recognized, the shared Block String scanner owns every physical line through its closing delimiter. During that interval:

- `REM`, `//`, `#`, and `--` remain String data;
- `IF`, `ELSE`, `END IF`, `WHILE`, and `END WHILE` text remains String data;
- blank lines remain String content, not source-layout items; and
- recovery and syntax highlighting must not reinterpret block content.

Normal comment and blank-line classification resumes after the closing delimiter.

## Official Escape Sequences

Ordinary and Block String literals decode the same exact table:

| Escape | Meaning |
|---|---|
| `\\` | Backslash |
| `\"` | Double quote |
| `\n` | Line feed |
| `\r` | Carriage return |
| `\t` | Horizontal tab |
| `\0` | NUL character |
| `\b` | Backspace |
| `\f` | Form feed |

No other escape is valid in v1.0. For example, `\q`, `\a`, and `\v` are source errors even if a destination language recognizes them. Quotes on ordinary block content lines remain literal unless written as the official `\"` escape; both spellings produce the same quote value.

Raw PRINT template text and String INPUT do not decode source escapes. Entering or printing the two characters `\` and `n` keeps those two characters rather than creating LF.

## Binding, Evaluation, And Scope

Block normalization and escape decoding belong entirely to the front end. A successful block produces one `BlockStringLiteralExpressionSyntax`, then binds to the existing ordinary `BoundStringLiteralExpression` with the normalized value.

For LET, the declared name remains absent while the complete block is scanned, normalized, decoded, and bound. Duplicate declarations, declaration-before-use, forward-reference, and self-reference rules therefore remain unchanged. A malformed initializer creates no variable symbol.

The evaluator and target generators receive only the ordinary normalized String value. They do not receive delimiters, indentation metadata, or original physical line endings.

IF v1.0 still rejects every LET in a branch with `SMILE1414`. WHILE v1.0 still rejects LET recursively anywhere lexically inside its body with `SMILE1610`, including a LET whose initializer is a Block String. The scanner consumes the complete block before normal scope diagnostics are applied, so block content cannot cause cascaded structure errors.

## PRINT Semantics

Block Strings do not change PRINT's deterministic distinction:

```smile
PRINT MultilineText
```

prints the literal template text `MultilineText`, while:

```smile
PRINT {MultilineText}
```

reads and prints the variable's current String value followed by PRINT's normal line ending.

## Diagnostics

| Code | Meaning |
|---|---|
| `SMILE1003` | Unterminated ordinary String literal or Block String Literal |
| `SMILE1208` | Unknown or invalid String escape sequence |
| `SMILE1209` | Unterminated String escape sequence |
| `SMILE1306` | A Block String Literal is valid only as the complete value of LET or SET |
| `SMILE1307` | Unexpected content follows a closing Block String delimiter |
| `SMILE1308` | The opening quote of a Block String must end the physical LET or SET line |

Diagnostics retain exact source spans. Invalid placements that have a recognizable block opener consume through the complete closing delimiter before parsing resumes.

## Target Generation

Target generators render the normalized semantic String value, not the source form. An ordinary one-line literal containing `\n` and a Block String with the same value are eligible for the same target representation. Explicit concatenation remains concatenation, and explicit interpolation remains interpolation.

For a direct control-free literal containing LF, prefer the clearest exact form supported by the destination:

| Target | Preferred exact representation |
|---|---|
| C# | Raw multiline String literal |
| JavaScript | Template literal |
| Java | Text block |
| Swift | Multiline String literal, with an extended delimiter when useful |
| Python | Triple-quoted literal or clear adjacent-literal fallback |
| C++ | Raw String literal |
| C | Adjacent ordinary String literal fragments with explicit `\n` |
| Objective-C | Adjacent C String literal fragments with explicit `\n` |
| COBOL | Exact byte/hex storage |
| MASM x64 | Printable `BYTE` chunks plus numeric LF bytes |

Semantic exactness is absolute. A generator must use its deterministic escaped or byte-oriented fallback when native multiline syntax cannot preserve delimiters, embedded NUL, carriage return, backspace, form feed, tabs, trailing whitespace, Unicode, or text after NUL safely and warning-free. Literal-internal physical line breaks must represent logical LF even when the generated file's ordinary formatting uses Windows line endings.

Destination representations that normally terminate at NUL must carry an exact length whenever a value may contain NUL. C and Objective-C may use `%s` or `strcmp` only when analysis proves those operations valid for the complete value. COBOL and MASM retain exact logical byte lengths. No target may call trim, strip, dedent, replace-line-endings, or another runtime cleanup operation to repair generated literal text.

## Conformance

Conformance tests compare bound values and runtime output exactly. They cover CRLF, LF, and standalone CR source; leading, middle, and trailing blank lines; exact space/tab margins and mismatches; trailing whitespace; delimiter-shaped content; every official escape; Unicode; NUL; and text after NUL.

Focused generated-target tests use `SmileEvaluator` as the semantic oracle for the three active targets: C#, C, and Windows x64 MASM. Tests never trim NUL, backspace, form feed, carriage return, tab, spaces, or meaningful trailing blank lines. Paused targets receive catch-up conformance when re-enabled; strict multi-toolchain and warning gates belong to explicit milestone or release validation rather than routine Velocity Mode.
