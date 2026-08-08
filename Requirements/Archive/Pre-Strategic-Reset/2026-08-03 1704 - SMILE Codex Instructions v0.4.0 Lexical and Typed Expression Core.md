# Codex Implementation Instructions — SMILE v0.4.0 Lexical and Typed Expression Core

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work **directly on `main` only**.
- Sin is the only developer.
- **Do not create, suggest, or use a feature branch.**
- Do not open a pull request for this work.
- Re-read `AGENTS.md` before changing code.
- Inspect the current `main` branch and working tree before editing.
- Do not assume a previously reviewed commit is still current.
- Do not discard, reset, overwrite, or commit unrelated user work.
- Do not commit or push unless Sin explicitly authorizes it in the Codex session.
- Follow KISS and KISS v2.
- Do not add a parser generator, compiler framework, third-party runtime, or unnecessary abstraction.

---

# 1. Milestone

Create the next major SMILE language milestone:

> **SMILE v0.4.0 — Lexical and Typed Expression Core**

This milestone establishes the language machinery required before introducing runtime statements such as `INPUT`, `IF`, loops, or functions.

Do not add a new statement keyword in this release.

The only official statement keywords remain:

```text
LET
PRINT
```

This milestone adds:

1. A real token-based lexer.
2. A precedence-aware expression parser.
3. A formal string-literal escape specification.
4. The official `Integer` and `Boolean` types.
5. Typed unary and binary expressions.
6. Parenthesized expressions.
7. Compile-time overflow and division-by-zero diagnostics.
8. A generalized typed constant evaluator.
9. A generalized typed SMILE reference evaluator.
10. Precedence-aware target-language expression generation.
11. Cross-target semantic conformance tests.

---

# 2. Governing design rule

Preserve this architecture:

```text
Source
  -> Lexer
  -> Tokens
  -> Parser
  -> Syntax Tree
  -> Binder
  -> Bound Program
  -> Typed Constant Evaluation
  -> Reference Evaluation
  -> Target Generator
  -> Generated Files
```

The official language specification is authoritative.

Target generators must implement SMILE semantics. They must not define SMILE semantics independently.

Generated target code should remain:

- semantically correct;
- idiomatic for the destination language;
- close to code a competent developer would naturally write;
- deterministic;
- readable;
- educational;
- dependency-light.

---

# 3. Preserve all stable `LET` and `PRINT` v1.0 behavior

Do not regress:

```smile
LET Name = "Sin"
LET Copy = Name
LET FullName = Name + " Cioco"
LET Greeting = $"Hello {FullName}!"
```

Preserve:

- case-insensitive SMILE keywords;
- case-insensitive SMILE identifiers;
- declaration spelling for diagnostics and preferred target names;
- ASCII identifier rules;
- `LET` and `PRINT` as reserved SMILE keywords;
- declaration-before-use;
- self-reference rejection;
- forward-reference rejection;
- duplicate declaration rejection;
- failed declarations not leaking symbols;
- string concatenation;
- interpolated quoted strings;
- raw `PRINT` templates;
- blank `PRINT`;
- literal braces through `{{` and `}}`;
- one statement per physical line;
- semicolons not separating statements;
- `PRINT Name` as literal text;
- `PRINT {Name}` as an evaluated expression;
- target identifier mapping;
- the reference evaluator;
- cross-target output equivalence;
- desktop responsiveness and Build & Run crash containment.

This distinction must remain:

```smile
LET Name = "Sin"

PRINT Name
PRINT {Name}
```

Output:

```text
Name
Sin
```

---

# 4. New official types

Extend `SmileType` from:

```text
String
```

to:

```text
String
Integer
Boolean
Error
```

`Error` is an internal compiler type used to suppress cascading diagnostics. It is not a user-declarable type.

## 4.1 Integer

SMILE `Integer` is a signed 64-bit integer.

Range:

```text
-9,223,372,036,854,775,808
through
 9,223,372,036,854,775,807
```

Use invariant decimal text representation.

Examples:

```smile
LET Age = 49
LET Negative = -12
LET Total = 20 + 22
```

## 4.2 Boolean

Boolean literals are case-insensitive:

```smile
TRUE
FALSE
True
False
tRuE
fAlSe
```

Canonical SMILE display text is uppercase:

```text
TRUE
FALSE
```

Examples:

```smile
LET Enabled = TRUE
LET Disabled = FALSE
LET IsAdult = Age >= 18
```

## 4.3 Type inference

`LET` continues to infer a variable's type from its initializer:

```smile
LET Name = "Sin"       // String
LET Age = 49           // Integer
LET Enabled = TRUE     // Boolean
```

Do not add type annotations in this milestone.

---

# 5. Strict typing and conversion rules

Do not introduce broad implicit type conversion.

## 5.1 `+`

`+` has two valid typed meanings:

```text
Integer + Integer -> Integer
String  + String  -> String
```

Mixed operands are invalid:

```smile
LET Age = 49
LET Message = "Age: " + Age
```

This must produce a type diagnostic.

Use interpolation instead:

```smile
LET Message = $"Age: {Age}"
```

## 5.2 Other arithmetic operators

These require Integer operands:

```text
- 
*
/
```

Results are Integer.

## 5.3 Logical operators

These require Boolean operands:

```text
NOT
AND
OR
```

Results are Boolean.

## 5.4 Equality operators

Support:

```text
=
<>
```

Operands must have the same non-error type.

Results are Boolean.

Examples:

```smile
LET SameName = Name = "Sin"
LET DifferentAge = Age <> 50
LET SameFlag = Enabled = TRUE
```

String equality is case-sensitive because string data is case-sensitive.

## 5.5 Relational operators

Support:

```text
<
<=
>
>=
```

Version 1.0 typed-expression rules permit these only for Integer operands.

Do not define string ordering yet.

## 5.6 Parentheses

Parentheses may override precedence:

```smile
LET Result = (2 + 3) * 4
```

## 5.7 Unary operators

Support:

```text
+Integer -> Integer
-Integer -> Integer
NOT Boolean -> Boolean
```

Do not define bitwise operators in this milestone.

---

# 6. Integer arithmetic semantics

## 6.1 Checked 64-bit arithmetic

All official SMILE integer operations are checked.

A compile-time expression that exceeds the signed 64-bit range is a compiler error.

Examples:

```smile
LET TooLarge = 9223372036854775807 + 1
LET TooSmall = -9223372036854775807 - 2
```

Do not wrap silently.

## 6.2 Division

`/` between Integer operands performs integer division truncated toward zero.

Examples:

```text
 7 / 2  -> 3
-7 / 2  -> -3
 7 / -2 -> -3
```

Because SMILE v0.4.0 programs have no runtime input or reassignment, every valid `LET` initializer remains compile-time evaluable.

## 6.3 Division by zero

Reject:

```smile
LET Result = 10 / 0
```

with a stable diagnostic.

## 6.4 Minimum integer literal

The parser and binder must correctly support:

```smile
LET Minimum = -9223372036854775808
```

Do not reject it merely because the positive magnitude cannot fit in `long`.

Handle unary minus and literal parsing deliberately.

---

# 7. Canonical value-to-text conversion

`PRINT` interpolation and direct evaluated output use these canonical conversions:

## String

The string value itself.

## Integer

Invariant base-10 representation with an optional leading `-`.

Examples:

```text
0
49
-12
9223372036854775807
```

## Boolean

Exactly:

```text
TRUE
FALSE
```

This conversion is part of SMILE semantics and must not depend on:

- current culture;
- operating system;
- target language default formatting;
- lowercase target boolean literals.

Examples:

```smile
LET Age = 49
LET Enabled = TRUE

PRINT {Age}
PRINT {Enabled}
PRINT $"Age={Age}, Enabled={Enabled}"
```

Output:

```text
49
TRUE
Age=49, Enabled=TRUE
```

---

# 8. String literal escape specification

Create a canonical public specification:

```text
docs/SMILE Language Specification/
    SMILE - String Literals Official Specification v1.0.md
```

Implement these escapes inside ordinary and interpolated quoted strings:

| Source | Value |
|---|---|
| `\\` | backslash |
| `\"` | double quote |
| `\n` | line feed |
| `\r` | carriage return |
| `\t` | horizontal tab |
| `\0` | NUL |
| `\b` | backspace |
| `\f` | form feed |

Examples:

```smile
LET Quote = "She said \"Hello\"."
LET Path = "C:\\SMILE"
LET TwoLines = "Line 1\nLine 2"
```

## 8.1 Unknown escapes

Reject unknown escapes:

```smile
LET Invalid = "\q"
```

Do not silently treat `\q` as `q`.

## 8.2 Unterminated escape

Reject a string ending with a lone backslash.

## 8.3 Interpolated strings

Interpolated quoted strings use the same backslash escapes plus:

```text
{{ -> literal {
}} -> literal }
```

Example:

```smile
LET Message = $"Name=\"{Name}\""
```

## 8.4 Raw PRINT templates

Backslash escape processing does not apply to raw `PRINT` template text.

This:

```smile
PRINT C:\SMILE
```

prints those characters literally.

Interpolation inside raw templates still uses normal expression parsing.

## 8.5 Smart quotation marks

Preserve the current beginner-friendly acceptance of smart opening and closing quotation marks as source delimiters where practical.

Generated target code must always use valid destination-language delimiters and escapes.

---

# 9. Introduce a real lexer

Create focused lexer files, for example:

```text
src/SMILE.Engine/Lexer.cs
src/SMILE.Engine/SyntaxKind.cs
src/SMILE.Engine/SyntaxToken.cs
```

Exact file organization may differ.

## 9.1 Required token kinds

At minimum support:

```text
BadToken
EndOfFileToken
EndOfLineToken

IdentifierToken
StringLiteralToken
IntegerLiteralToken

LetKeyword
PrintKeyword
TrueKeyword
FalseKeyword
NotKeyword
AndKeyword
OrKeyword

PlusToken
MinusToken
StarToken
SlashToken

EqualsToken
NotEqualsToken
LessToken
LessOrEqualsToken
GreaterToken
GreaterOrEqualsToken

OpenParenthesisToken
CloseParenthesisToken
```

Whitespace may be trivia rather than tokens.

## 9.2 Token data

Each token should carry:

- kind;
- source text;
- parsed value when applicable;
- source span;
- leading/trailing trivia only if needed.

Do not add a full Roslyn-style trivia system unless current requirements need it.

## 9.3 Newlines

Preserve line-oriented statements.

The lexer must emit or otherwise preserve end-of-line boundaries so the parser can enforce one statement per physical line.

## 9.4 Case-insensitive keywords

Keywords are recognized with ordinal case-insensitive comparison.

Identifier spelling remains preserved.

## 9.5 Bad tokens

Invalid characters should produce diagnostics and a `BadToken` or equivalent recovery token.

The lexer must always advance after an invalid character.

It must never enter an infinite loop.

## 9.6 Raw PRINT template mode

Raw `PRINT` templates are intentionally different from normal expressions.

Use one simple, explicit strategy:

### Recommended strategy

1. Lex normal statement prefixes and expression syntax into tokens.
2. After the parser recognizes `PRINT` and determines that the payload is neither `$"` nor `"`, parse the raw template from the original source slice for that physical line.
3. Within `{...}` holes, invoke the normal token-based expression lexer/parser on the hole text.
4. Keep raw text outside holes literal except for `{{` and `}}`.

This specialized raw-template scanner is acceptable and should be documented as a template lexical mode.

Do not force ordinary raw words and punctuation into the normal expression token grammar.

---

# 10. Precedence-aware expression parser

Replace the current string-term-only expression parser with a precedence-aware parser.

A Pratt parser or precedence-climbing parser is recommended.

Do not add a parser generator.

## 10.1 Precedence order

From highest to lowest:

```text
Primary:
    literals
    identifiers
    parenthesized expressions
    interpolated strings

Unary:
    +
    -
    NOT

Multiplicative:
    *
    /

Additive:
    +
    -

Relational:
    <
    <=
    >
    >=

Equality:
    =
    <>

Logical AND:
    AND

Logical OR:
    OR
```

## 10.2 Associativity

Binary operators are left-associative in this milestone.

Unary operators are right-associative.

## 10.3 Expression grammar

Publish and implement behavior equivalent to:

```text
expression
    ::= logical-or-expression

logical-or-expression
    ::= logical-and-expression (OR logical-and-expression)*

logical-and-expression
    ::= equality-expression (AND equality-expression)*

equality-expression
    ::= relational-expression (('=' | '<>') relational-expression)*

relational-expression
    ::= additive-expression (('<' | '<=' | '>' | '>=') additive-expression)*

additive-expression
    ::= multiplicative-expression (('+' | '-') multiplicative-expression)*

multiplicative-expression
    ::= unary-expression (('*' | '/') unary-expression)*

unary-expression
    ::= ('+' | '-' | NOT) unary-expression
      | primary-expression

primary-expression
    ::= string-literal
      | integer-literal
      | TRUE
      | FALSE
      | identifier
      | interpolated-string
      | '(' expression ')'
```

## 10.4 `LET`

`LET` parses the full expression grammar after `=`.

Examples:

```smile
LET Age = 49
LET Total = 2 + 3 * 4
LET Grouped = (2 + 3) * 4
LET Enabled = TRUE
LET IsAdult = Age >= 18
LET Message = $"Age: {Age}"
```

## 10.5 `PRINT`

Preserve the deterministic `PRINT` dispatch rule:

1. blank line;
2. explicit interpolated quoted expression if payload starts with `$"`;
3. quoted expression if payload starts with `"`;
4. otherwise raw template.

Do not reinterpret ordinary bare raw text as an expression.

Expression holes in raw templates now support the full expression grammar:

```smile
PRINT 2 + 3 = {2 + 3}
PRINT Adult: {Age >= 18}
```

Output:

```text
2 + 3 = 5
Adult: TRUE
```

---

# 11. Syntax tree changes

Add syntax nodes equivalent to:

```text
IntegerLiteralExpressionSyntax
BooleanLiteralExpressionSyntax
UnaryExpressionSyntax
BinaryExpressionSyntax
ParenthesizedExpressionSyntax
```

A possible design:

```csharp
public sealed record IntegerLiteralExpressionSyntax(
    string Text,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record BooleanLiteralExpressionSyntax(
    bool Value,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record UnaryExpressionSyntax(
    SyntaxToken OperatorToken,
    ExpressionSyntax Operand,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record BinaryExpressionSyntax(
    ExpressionSyntax Left,
    SyntaxToken OperatorToken,
    ExpressionSyntax Right,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record ParenthesizedExpressionSyntax(
    SyntaxToken OpenParenthesis,
    ExpressionSyntax Expression,
    SyntaxToken CloseParenthesis,
    TextSpan Span)
    : ExpressionSyntax(Span);
```

Exact classes may differ.

Do not encode operator semantics directly in raw strings.

Use `SyntaxKind` or an equivalent stable operator identity.

---

# 12. Bound tree and type checking

Add bound nodes equivalent to:

```text
BoundIntegerLiteralExpression
BoundBooleanLiteralExpression
BoundUnaryExpression
BoundBinaryExpression
BoundErrorExpression
```

## 12.1 Operators

Use bound operator descriptors or focused lookup methods that define:

- syntax operator;
- operand types;
- result type;
- bound operation kind.

A suitable KISS pattern is:

```csharp
BoundUnaryOperator.Bind(kind, operandType)
BoundBinaryOperator.Bind(kind, leftType, rightType)
```

Do not scatter type tables throughout the binder and generators.

## 12.2 Error type

When an expression is already invalid, return a `BoundErrorExpression`.

Avoid emitting multiple redundant errors for one root problem.

## 12.3 Variable types

`VariableSymbol.Type` must reflect the initializer type.

## 12.4 Duplicate and failed declarations

Continue adding a symbol only after:

- initializer binding succeeds;
- type checking succeeds;
- constant evaluation succeeds.

A failed typed declaration must not leak a variable.

---

# 13. Generalize constant values

Replace the string-only `ConstantValue` with a typed representation.

A small immutable discriminated representation is required.

A possible design:

```csharp
public readonly record struct SmileValue
{
    public SmileType Type { get; }
    public string? StringValue { get; }
    public long IntegerValue { get; }
    public bool BooleanValue { get; }

    public static SmileValue FromString(string value);
    public static SmileValue FromInteger(long value);
    public static SmileValue FromBoolean(bool value);
}
```

Another clear design is acceptable.

Do not store arbitrary `object` without type-safe accessors.

Update:

```csharp
BoundLetStatement.ConstantValue
```

to use the typed value.

---

# 14. Generalize the constant evaluator

Replace or evolve:

```text
BoundStringConstantEvaluator
```

into:

```text
BoundConstantEvaluator
```

It must evaluate:

- literals;
- variables;
- unary operators;
- binary arithmetic;
- string concatenation;
- equality;
- relational comparisons;
- logical expressions;
- interpolated strings;
- parentheses through their bound inner expression.

## 14.1 Checked arithmetic

Use checked arithmetic and convert overflow into diagnostics.

Do not allow a raw `OverflowException` to escape as a desktop/compiler crash.

## 14.2 Division by zero

Convert it into a source diagnostic tied to the division expression or operator.

## 14.3 Canonical text

Provide one target-neutral helper:

```csharp
SmileValue.ToDisplayText()
```

or equivalent.

Use it for:

- `PRINT`;
- interpolation;
- reference evaluator;
- low-level constant lowering;
- test expectations.

---

# 15. Generalize `SmileEvaluator`

The evaluator remains the semantic oracle.

Change its runtime environment from:

```text
VariableSymbol -> string
```

to:

```text
VariableSymbol -> SmileValue
```

It must execute:

- typed `LET`;
- typed `PRINT`;
- interpolation with all official types;
- canonical display conversion.

Example:

```smile
LET Age = 49
LET IsAdult = Age >= 18
LET Message = $"Age={Age}; Adult={IsAdult}"

PRINT {Message}
```

Expected:

```text
Age=49; Adult=TRUE
```

Invalid programs return diagnostics and no successful evaluation.

---

# 16. Target expression generation must become precedence-aware

The existing recursive renderers must not emit ambiguous code as expressions become more complex.

Add target expression writers that understand:

- parent precedence;
- child precedence;
- associativity;
- required parentheses;
- target-specific operators;
- target-specific literal syntax;
- target-specific canonical text conversion.

Do not solve this with ad hoc string replacement.

A focused design might be:

```csharp
TargetExpressionWriter.Write(
    BoundExpression expression,
    TargetIdentifierMap identifiers,
    TargetLanguage language)
```

or one writer per target.

## 16.1 Parentheses examples

SMILE:

```smile
LET Value = (2 + 3) * 4
```

Generated target code must retain necessary parentheses.

SMILE:

```smile
LET Value = 2 + 3 * 4
```

Generated target code may omit unnecessary parentheses.

SMILE:

```smile
LET Value = 10 - (3 - 1)
```

The target must not generate:

```text
10 - 3 - 1
```

---

# 17. Target-language type mapping

## 17.1 C#

Use:

```text
String  -> string
Integer -> long
Boolean -> bool
```

Examples:

```csharp
string Name = "Sin";
long Age = 49L;
bool Enabled = true;
```

Canonical Boolean `PRINT` must output `TRUE`/`FALSE`, not C#'s default `True`/`False`.

For direct Boolean output, use an idiomatic conversion such as:

```csharp
Console.WriteLine(Enabled ? "TRUE" : "FALSE");
```

For interpolation, ensure Boolean holes use SMILE canonical conversion rather than default C# formatting.

A small generated helper may be used only if repeated conversion would otherwise make output unreadable.

Keep generated code human-natural.

## 17.2 JavaScript

Represent SMILE `Integer` with `BigInt`, not `Number`.

Examples:

```javascript
let Age = 49n;
let Total = Age + 1n;
```

Reasons:

- exact signed 64-bit semantics;
- exact integer division behavior;
- no precision loss above `2^53 - 1`.

Map operators:

```text
=   -> ===
<>  -> !==
AND -> &&
OR  -> ||
NOT -> !
```

Do not mix `BigInt` and `Number`.

Canonical Integer output must omit the `n`.

Canonical Boolean output must be `TRUE`/`FALSE`.

Examples:

```javascript
console.log(Age.toString());
console.log(Enabled ? "TRUE" : "FALSE");
```

Interpolation must also use canonical conversion.

## 17.3 Java

Use:

```text
String  -> String
Integer -> long
Boolean -> boolean
```

Integer literals should use `L` where needed.

Map:

```text
=  -> == for Integer/Boolean
<> -> != for Integer/Boolean
```

String equality must use:

```java
left.equals(right)
```

String inequality must negate `.equals`.

Canonical Boolean output must be uppercase.

## 17.4 Swift

Use explicit types where needed:

```text
String  -> String
Integer -> Int64
Boolean -> Bool
```

Examples:

```swift
let Age: Int64 = 49
let Enabled: Bool = true
```

Canonical Boolean output must be uppercase.

Integer division already truncates toward zero for signed integers.

## 17.5 C

Use:

```text
String  -> const char *
Integer -> long long
Boolean -> bool
```

Include headers only when needed:

```c
#include <stdio.h>
#include <stdbool.h>
#include <string.h>
```

Integer literals may use `LL`.

String equality uses:

```c
strcmp(left, right) == 0
```

String inequality uses:

```c
strcmp(left, right) != 0
```

Canonical Boolean output uses:

```c
value ? "TRUE" : "FALSE"
```

Current string-only `LET` expressions may continue lowering to evaluated constants when runtime string construction would otherwise be required.

Integer and Boolean expressions should remain visible in generated C when practical and idiomatic.

## 17.6 Objective-C

Use:

```text
String  -> NSString *
Integer -> long long
Boolean -> BOOL or bool
```

Choose one consistent, idiomatic strategy and document it.

String equality should use Objective-C string equality, for example:

```objective-c
[left isEqualToString:right]
```

Canonical Boolean output remains `TRUE`/`FALSE`.

String-producing `LET` expressions may continue lowering to evaluated constants.

## 17.7 MASM x64

Do not add a C runtime dependency.

Because all v0.4.0 `LET` initializers remain compile-time constants, MASM may lower values into static data.

Recommended representation:

```text
String:
    UTF-8 bytes plus explicit length

Integer:
    signed QWORD constant
    plus canonical decimal UTF-8 text when needed for PRINT

Boolean:
    BYTE or DWORD value
    plus TRUE/FALSE text labels for PRINT
```

A simpler representation that stores only canonical printable bytes is acceptable for immutable values, provided generated behavior remains correct and educational comments explain the lowering.

Preserve the empty-string logical-length correction.

---

# 18. Interpolated string generation

Interpolation accepts all official types.

Example:

```smile
LET Name = "Sin"
LET Age = 49
LET Enabled = TRUE
LET Message = $"{Name}: {Age}, {Enabled}"
```

Required value:

```text
Sin: 49, TRUE
```

Target generators must not rely on default target Boolean formatting.

High-level targets should preserve native interpolation where possible, but insert target-specific canonical conversion for non-string holes when needed.

Examples may include:

### C#

```csharp
string Message = $"{Name}: {Age}, {(Enabled ? "TRUE" : "FALSE")}";
```

### JavaScript

```javascript
let Message = `${Name}: ${Age.toString()}, ${Enabled ? "TRUE" : "FALSE"}`;
```

### Swift

Use explicit conversion that produces SMILE canonical text.

Low-level targets may lower to evaluated string constants.

---

# 19. Diagnostic catalog

Allocate a stable typed-expression range, preferably beginning at:

```text
SMILE1201
```

Suggested diagnostics:

| Code | Meaning |
|---|---|
| `SMILE1201` | Invalid or unexpected token in expression |
| `SMILE1202` | Integer literal is outside the signed 64-bit range |
| `SMILE1203` | Unary operator is not defined for the operand type |
| `SMILE1204` | Binary operator is not defined for the operand types |
| `SMILE1205` | Missing closing parenthesis |
| `SMILE1206` | Integer arithmetic overflow |
| `SMILE1207` | Division by zero |
| `SMILE1208` | Unknown or invalid string escape sequence |
| `SMILE1209` | Unterminated string escape sequence |

Exact numbering may be adjusted to avoid conflicts, but it must be:

- stable;
- documented;
- tested;
- consistent across engine, CLI, and desktop.

Preserve existing codes when their meanings still apply.

Expected source errors must never terminate:

- the engine;
- CLI;
- desktop application.

---

# 20. Required official specifications

Create or update canonical documents under:

```text
docs/SMILE Language Specification/
```

At minimum:

```text
SMILE - String Literals Official Specification v1.0.md
SMILE - Core Types and Expressions Official Specification v1.0.md
```

Update:

```text
SMILE - LET Statement Official Specification v1.0.md
SMILE - PRINT Statement Official Specification v1.0.md
```

Only update `LET` and `PRINT` where they reference the shared expression and conversion rules.

## 20.1 Core expression specification must define

- types;
- literal syntax;
- operator table;
- precedence;
- associativity;
- strict typing;
- integer range;
- overflow;
- integer division;
- division by zero;
- equality;
- canonical text conversion;
- parentheses;
- compile-time evaluation in the current immutable language;
- future compatibility.

## 20.2 String specification must define

- delimiters;
- smart quote compatibility;
- supported escapes;
- invalid escapes;
- interpolation interaction;
- literal braces;
- raw template distinction;
- line restrictions.

---

# 21. Conformance test suites

Create focused test files such as:

```text
LexerTests.cs
ExpressionParserTests.cs
TypedBindingTests.cs
StringLiteralConformanceTests.cs
TypedExpressionConformanceTests.cs
TypedTargetIntegrationTests.cs
```

Exact organization may differ.

## 21.1 Lexer tests

Cover:

- every token kind;
- keyword casing;
- identifier spelling;
- integer token spans;
- two-character operators;
- line endings;
- bad characters;
- lexer always advancing;
- string escapes;
- unknown escapes;
- source line/column accuracy.

## 21.2 Precedence tests

Verify syntax or evaluation:

```smile
LET Result = 2 + 3 * 4
```

Result:

```text
14
```

```smile
LET Result = (2 + 3) * 4
```

Result:

```text
20
```

```smile
LET Result = 10 - 3 - 1
```

Result:

```text
6
```

```smile
LET Result = 10 - (3 - 1)
```

Result:

```text
8
```

```smile
LET Result = TRUE OR FALSE AND FALSE
```

Result:

```text
TRUE
```

```smile
LET Result = NOT TRUE OR TRUE
```

Verify the documented precedence.

## 21.3 Type tests

Valid:

```smile
LET Name = "Sin"
LET Age = 49
LET Enabled = TRUE
LET Total = Age + 1
LET Adult = Age >= 18
LET Same = Name = "Sin"
LET Message = $"Age={Age}, Adult={Adult}"
```

Invalid:

```smile
LET Invalid = "Age: " + 49
LET Invalid = TRUE + FALSE
LET Invalid = "A" - "B"
LET Invalid = 1 AND 2
LET Invalid = TRUE < FALSE
LET Invalid = "A" < "B"
```

## 21.4 Integer boundary tests

Cover:

```text
0
1
-1
9223372036854775807
-9223372036854775808
```

Reject:

```text
9223372036854775808
-9223372036854775809
```

## 21.5 Overflow tests

Reject:

```smile
LET A = 9223372036854775807 + 1
LET B = -9223372036854775808 - 1
LET C = 3037000500 * 3037000500
LET D = -(-9223372036854775808)
```

## 21.6 Division tests

Verify:

```smile
LET A = 7 / 2
LET B = -7 / 2
LET C = 7 / -2
```

Reject:

```smile
LET D = 1 / 0
```

## 21.7 String escape tests

Verify exact values and output:

```smile
LET Quote = "She said \"Hello\"."
LET Path = "C:\\SMILE"
LET Lines = "A\nB"
LET Tabbed = "A\tB"
```

Reject invalid escapes.

## 21.8 Boolean display tests

Every target must output:

```text
TRUE
FALSE
```

not:

```text
True
False
true
false
```

## 21.9 Cross-target evaluator tests

For every installed runnable target:

1. evaluate with `SmileEvaluator`;
2. generate;
3. compile/run;
4. compare normalized output exactly.

Do not normalize:

- casing;
- spaces;
- integer formatting;
- Boolean text;
- NUL characters.

Only normalize line endings.

---

# 22. Required acceptance programs

## 22.1 Typed declarations

```smile
LET Name = "Sin"
LET Age = 49
LET Enabled = TRUE

PRINT $"Name={Name}"
PRINT $"Age={Age}"
PRINT $"Enabled={Enabled}"
```

Expected:

```text
Name=Sin
Age=49
Enabled=TRUE
```

## 22.2 Arithmetic precedence

```smile
LET A = 2 + 3 * 4
LET B = (2 + 3) * 4
LET C = 10 / 3
LET D = -7 / 2

PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
```

Expected:

```text
14
20
3
-3
```

## 22.3 Boolean expressions

```smile
LET Age = 49
LET IsAdult = Age >= 18
LET IsSenior = Age >= 65
LET WorkingAge = IsAdult AND NOT IsSenior

PRINT {IsAdult}
PRINT {IsSenior}
PRINT {WorkingAge}
```

Expected:

```text
TRUE
FALSE
TRUE
```

## 22.4 Equality

```smile
LET Name = "Sin"
LET SameName = Name = "Sin"
LET DifferentName = Name <> "Joy"
LET SameNumber = 49 = 49
LET SameBoolean = TRUE = TRUE

PRINT {SameName}
PRINT {DifferentName}
PRINT {SameNumber}
PRINT {SameBoolean}
```

Expected:

```text
TRUE
TRUE
TRUE
TRUE
```

## 22.5 String escapes

```smile
LET Quote = "She said \"Hello\"."
LET Path = "C:\\SMILE"

PRINT {Quote}
PRINT {Path}
```

Expected:

```text
She said "Hello".
C:\SMILE
```

## 22.6 Full mixed interpolation

```smile
LET Name = "Sin"
LET Age = 49
LET Adult = Age >= 18
LET Message = $"{Name} is {Age}. Adult={Adult}"

PRINT {Message}
```

Expected:

```text
Sin is 49. Adult=TRUE
```

---

# 23. Desktop requirements

Preserve:

- AvalonEdit;
- line numbers;
- syntax highlighting;
- debounced live transpilation;
- current-source revision safety;
- rapid target switching;
- cached generated targets;
- asynchronous toolchains;
- bounded output;
- cancellation;
- timeouts;
- crash containment;
- diagnostic logging.

Update SMILE syntax highlighting for:

- integer literals;
- `TRUE`;
- `FALSE`;
- `NOT`;
- `AND`;
- `OR`;
- arithmetic operators;
- comparison operators;
- parentheses;
- string escapes.

Do not add autocomplete, semantic coloring, or diagnostic squiggles in this milestone.

The default learning sample may demonstrate String, Integer, Boolean, arithmetic, and interpolation, but keep it short.

---

# 24. CLI requirements

The CLI must:

- transpile typed programs for all seven targets;
- display diagnostics with stable codes and source positions;
- build/run installed targets;
- preserve nonzero exit behavior for invalid SMILE source;
- compare cleanly with the reference evaluator in tests.

No new CLI framework is needed.

---

# 25. Documentation updates

Update:

- `README.md`;
- `AGENTS.md`;
- `docs/Architecture.md`;
- `docs/Roadmap.md`;
- target code generation standard;
- official `LET` specification;
- official `PRINT` specification;
- new string and expression specifications;
- examples;
- Day 4 requirements/history notes if that project pattern continues.

## 25.1 AGENTS.md permanent rules

Preserve:

> All SMILE development is performed directly on `main`. Sin is the only developer. Do not create or recommend feature branches unless Sin explicitly changes this rule.

Add:

> Every expression feature must be defined once in the official core expression specification and implemented through the shared lexer, parser, binder, evaluator, and bound tree. Target generators must not invent target-specific SMILE semantics.

Add:

> Cross-target runtime tests must compare generated program output to the SMILE reference evaluator.

## 25.2 Roadmap

Record v0.4.0 as the lexical and typed expression foundation.

Keep these as future work:

- reassignment;
- `INPUT`;
- `IF`;
- loops;
- functions;
- arrays;
- decimal values;
- user-defined types;
- nested scopes;
- runtime string construction.

---

# 26. Scope exclusions

Do not implement:

- a third statement keyword;
- assignment or reassignment;
- `INPUT`;
- `IF`;
- loops;
- functions;
- arrays;
- Decimal or floating-point types;
- date/time types;
- objects;
- classes;
- multi-line expressions;
- user-defined types;
- a bytecode VM;
- a runtime library;
- a parser generator;
- a feature branch.

This milestone builds the foundation those features will use.

---

# 27. Suggested implementation sequence

Use this order:

1. Write official string-literal and typed-expression specifications.
2. Add lexer tests that initially fail.
3. Implement `SyntaxKind`, `SyntaxToken`, and the lexer.
4. Preserve raw `PRINT` template parsing as an explicit special mode.
5. Add precedence parser tests.
6. Implement the precedence-aware expression parser.
7. Add syntax nodes for typed expressions.
8. Extend `SmileType`.
9. Add bound operators and typed bound nodes.
10. Add binder type checking and error expressions.
11. Introduce `SmileValue`.
12. Generalize constant evaluation.
13. Generalize `SmileEvaluator`.
14. Add precedence-aware target expression emitters.
15. Implement each target's types, operators, and canonical text conversion.
16. Update syntax highlighting.
17. Add exact generator tests.
18. Add evaluator-versus-toolchain integration tests.
19. Update README, architecture, roadmap, AGENTS, and examples.
20. Run full Debug and Release validation.
21. Perform desktop smoke testing.
22. Commit directly to `main` only when Sin authorizes it.

Do not begin `INPUT` or `IF` before the typed expression conformance suite is green.

---

# 28. Acceptance criteria

This task is complete only when all of the following are true:

1. A token-based lexer exists.
2. Tokens have accurate source spans.
3. The lexer cannot stall on invalid input.
4. Raw `PRINT` templates remain deterministic.
5. String escapes are formally specified and implemented.
6. Unknown escapes are rejected.
7. `Integer` is signed 64-bit.
8. `Boolean` is implemented.
9. `TRUE` and `FALSE` are case-insensitive.
10. `LET` infers String, Integer, and Boolean types.
11. Parentheses work.
12. Unary `+`, unary `-`, and `NOT` work.
13. `*` and `/` have higher precedence than `+` and `-`.
14. relational operators work for Integer.
15. equality works for same-type values.
16. `AND` binds more tightly than `OR`.
17. mixed invalid operand types are rejected.
18. integer overflow is rejected.
19. division by zero is rejected.
20. integer division truncates toward zero.
21. the minimum signed 64-bit integer is accepted.
22. canonical Boolean text is `TRUE`/`FALSE`.
23. canonical integer text is invariant decimal.
24. interpolation converts all official types correctly.
25. `PRINT Name` remains literal.
26. `PRINT {Name}` remains evaluated.
27. existing `LET` string expressions remain valid.
28. existing `PRINT` v1.0 forms remain valid.
29. `SmileValue` or equivalent is type-safe.
30. constant evaluation is target-neutral.
31. `SmileEvaluator` is the semantic oracle.
32. target expression output preserves necessary parentheses.
33. C# generated typed code compiles and runs.
34. C generated typed code compiles and runs.
35. MASM generated typed code assembles, links, and runs.
36. JavaScript uses exact BigInt semantics.
37. Java generated typed code compiles and runs.
38. Objective-C generated source is structurally valid.
39. Swift generated source is structurally valid.
40. target identifier mapping still works.
41. empty strings remain correct in MASM.
42. all runnable target output matches `SmileEvaluator`.
43. desktop responsiveness remains intact.
44. Build & Run failure containment remains intact.
45. Debug build has zero warnings.
46. Release build has zero warnings.
47. Debug tests pass.
48. Release tests pass.
49. CLI generation succeeds for all seven targets.
50. documentation matches implementation.
51. no unrelated files or build artifacts are committed.
52. all work is performed directly on `main`.

---

# 29. Validation commands

Run from the repository root:

```bat
cmd /c git status --short --branch
```

Confirm:

```text
main
```

Do not create another branch.

Run:

```bat
cmd /c dotnet restore SMILE.sln
```

```bat
cmd /c dotnet build SMILE.sln -c Debug
```

```bat
cmd /c dotnet test SMILE.sln -c Debug --no-build
```

```bat
cmd /c dotnet build SMILE.sln -c Release
```

```bat
cmd /c dotnet test SMILE.sln -c Release --no-build
```

Run CLI generation for all seven targets using every acceptance program.

Run CLI Build & Run for installed targets:

```text
csharp
c
masm-x64
javascript
java
```

Compare each normalized stdout result to `SmileEvaluator`.

Before any authorized commit:

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

# 30. Manual Windows validation

In the desktop application:

1. Enter the typed declarations acceptance program.
2. Switch rapidly among C, Swift, Java, C#, and MASM.
3. Build and run visible supported targets.
4. Verify identical output.
5. Enter a type error and confirm a diagnostic appears without crashing.
6. Enter a division-by-zero program and confirm a diagnostic appears.
7. Enter an overflow program and confirm a diagnostic appears.
8. Correct the program and verify live generation recovers.
9. Press Cancel during a build and confirm the IDE remains usable.
10. Verify Boolean output is uppercase in every runnable target.

---

# 31. Completion report

Report:

- files changed;
- lexer architecture;
- raw template strategy;
- token kinds;
- expression precedence implementation;
- new syntax nodes;
- new bound nodes;
- type system changes;
- `SmileValue` design;
- constant evaluator design;
- reference evaluator changes;
- string escape rules;
- diagnostic codes;
- target type/operator mappings;
- JavaScript BigInt handling;
- canonical text conversion handling;
- exact Debug and Release test totals;
- all target generation results;
- installed target compile/run results;
- desktop smoke-test results;
- documentation changes;
- unresolved limitations.

Do not state that v0.4.0 is complete if any installed runnable target differs from the reference evaluator.

---

# 32. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

When authorized, commit directly on `main`.

Suggested subject:

```text
Sin and Codex: Add the typed expression core
```

The commit body should mention:

- token-based lexer;
- precedence-aware parser;
- String/Integer/Boolean types;
- strict operator typing;
- checked 64-bit arithmetic;
- string escape support;
- typed constant evaluation;
- generalized reference evaluator;
- target-specific typed generation;
- evaluator-versus-toolchain conformance;
- exact validation results.
