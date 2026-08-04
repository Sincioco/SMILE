# SMILE LET Statement — Official Language Specification

**Specification version:** 1.0
**Status:** Official
**Applies to:** SMILE language
**Primary statement:** `LET`
**Companion statement:** `PRINT`
**Companion specification:** `docs/SMILE Language Specification/SMILE - PRINT Statement Official Specification v1.0.md`
**Repository destination:** `docs/SMILE Language Specification/SMILE - LET Statement Official Specification v1.0.md`

---

## 1. Purpose

`LET` declares a named SMILE variable and assigns its initial value.

The official SMILE statement keywords through v0.5.0 are:

| Keyword | Purpose |
|---|---|
| `LET` | Declares and initializes a variable |
| `PRINT` | Writes text or an evaluated value to standard output |
| `SET` | Changes the current value of an existing variable without changing its type |

Together, these statements support the first useful SMILE programs:

```basic
LET Name = "Sin"

PRINT Hello {Name}!
```

Output:

```text
Hello Sin!
```

This specification is intentionally compatible with the official SMILE `PRINT` Statement Specification version 1.0.

---

## 2. Normative terminology

The words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** describe requirements of this specification.

---

## 3. Relationship between LET and PRINT

`LET` creates a variable binding.

`PRINT` may evaluate and display that variable through an expression or interpolation hole.

Given:

```basic
LET Name = "Sin"
```

these statements have different meanings:

```basic
PRINT Name
PRINT {Name}
```

Output:

```text
Name
Sin
```

This distinction is required by the official `PRINT` specification:

- Bare text after `PRINT` is literal template text.
- An expression inside `{` and `}` is evaluated.
- Declaring a variable MUST NOT change the meaning of an existing bare `PRINT` template.

Therefore, even after this declaration:

```basic
LET Name = "Sin"
```

this statement:

```basic
PRINT Name
```

MUST continue to print the literal word:

```text
Name
```

To print the variable value, use:

```basic
PRINT {Name}
```

---

## 4. Case-insensitivity

SMILE is case-insensitive.

The following keywords are equivalent:

```basic
LET
Let
let
lEt
```

The official `PRINT` keyword follows the same rule:

```basic
PRINT
Print
print
pRiNt
```

Identifiers are also case-insensitive:

```basic
LET Name = "Sin"

PRINT {Name}
PRINT {name}
PRINT {NAME}
```

All three references identify the same variable.

The compiler MUST use culture-independent, ordinal case-insensitive comparison for keywords and identifiers.

The compiler SHOULD preserve the spelling used in the declaration for:

- Diagnostics.
- Generated code.
- Educational displays.
- Debugging information.
- Future source mapping.

String data remains case-sensitive:

```basic
LET First = "Hello"
LET Second = "HELLO"
```

The two values are different strings.

---

## 5. Reserved keywords

`LET`, `PRINT`, and `SET` are official reserved SMILE statement keywords.

They cannot be used as variable names in any case combination.

Invalid:

```basic
LET LET = "Value"
LET Print = "Value"
LET set = "Value"
```

A longer identifier that merely contains the character sequence is not the keyword.

Valid identifiers include:

```basic
LET Letter = "A"
LET Reprint = "Again"
LET Printable = "Yes"
LET LetValue = "Value"
```

Future SMILE specifications may add additional reserved keywords.

Target-language reserved words do not automatically become SMILE reserved words. A target generator MUST safely escape or map a valid SMILE identifier when the corresponding target language reserves that spelling.

---

## 6. Identifier syntax

Version 1.0 identifiers use the following portable form:

```text
identifier
    ::= identifier-start identifier-part*

identifier-start
    ::= ASCII letter
      | '_'

identifier-part
    ::= ASCII letter
      | ASCII digit
      | '_'
```

Valid:

```basic
LET Name = "Sin"
LET FirstName = "Sin"
LET first_name = "Sin"
LET Name2 = "Sin"
LET _temporary = "Sin"
```

Invalid:

```basic
LET 2Name = "Sin"
LET First Name = "Sin"
LET First-Name = "Sin"
LET First.Name = "Sin"
LET $Name = "Sin"
```

Identifier comparison is case-insensitive even though declaration spelling is preserved.

---

## 7. One statement per line

A newline normally terminates a SMILE statement.

A physical source line MUST NOT contain more than one SMILE statement.

Valid:

```basic
LET Name = "Sin"
PRINT Hello {Name}!
```

Invalid:

```basic
LET Name = "Sin"; PRINT Hello {Name}!
LET Name = "Sin" PRINT Hello {Name}!
LET First = "Sin" LET Last = "Cioco"
```

SMILE does not use semicolons to terminate or separate statements.

A semicolon inside a string remains part of the string value:

```basic
LET Steps = "First; second; third"
PRINT {Steps}
```

Output:

```text
First; second; third
```

All forms of `LET` defined by this specification fit on one physical source line. The only current multiline statement form is the SET Block String Literal defined by the official SET specification; it is not a LET initializer.

Future SMILE versions may permit visibly incomplete expressions to continue onto another line, but that does not permit multiple statements on one line.

---

## 8. Required whitespace after LET

At least one space or tab MUST separate the `LET` keyword from the variable name.

Valid:

```basic
LET Name = "Sin"
LET    Name = "Sin"
LET	Name = "Sin"
```

Invalid:

```basic
LETName = "Sin"
```

`LETName` is not the `LET` keyword followed by an identifier. It is a single identifier-like token and does not begin a valid version 1.0 statement.

---

## 9. Assignment symbol and spacing

A `LET` declaration uses one equals sign:

```basic
LET Name = "Sin"
```

Spaces around `=` are optional.

All of these are equivalent:

```basic
LET Name = "Sin"
LET Name= "Sin"
LET Name ="Sin"
LET Name="Sin"
```

The recommended style is:

```basic
LET Name = "Sin"
```

The equals sign in a `LET` statement means:

> Evaluate the initializer and bind its value to the newly declared variable.

This specification does not define equality comparison.

---

## 10. Required initializer

A `LET` declaration MUST provide an initializer expression.

Valid:

```basic
LET Name = "Sin"
```

Invalid:

```basic
LET Name
LET Name =
LET Name = <spaces only>
```

A variable cannot be declared without an initial value in version 1.0.

---

## 11. Version 1.0 value type

Version 1.0 of `LET` officially supports the SMILE `String` type.

The type is inferred from the initializer; no type annotation is written:

```basic
LET Name = "Sin"
```

Conceptually:

```text
Name : String
```

Valid version 1.0 initializers include:

- An ordinary string literal.
- A previously declared string variable.
- String concatenation.
- An interpolated quoted string.

Examples:

```basic
LET FirstName = "Sin"
LET CopyOfName = FirstName
LET FullName = FirstName + " Cioco"
LET Greeting = $"Hello {FirstName}!"
```

Future specifications may add:

```basic
LET Age = 49
LET Enabled = TRUE
LET Price = 12.50
```

Those future additions can use the same `LET` syntax without changing this declaration form.

Until their types are officially specified, implementations MUST NOT silently treat unsupported values as strings.

---

## 12. LET declares a new variable

Version 1.0 `LET` declares and initializes a new variable.

It does not update an existing variable.

Valid:

```basic
LET Name = "Sin"
```

Invalid in the same scope:

```basic
LET Name = "Sin"
LET Name = "Joy"
```

Also invalid because identifiers are case-insensitive:

```basic
LET Name = "Sin"
LET NAME = "Joy"
```

The compiler MUST report a duplicate-variable diagnostic.

SMILE v0.5.0 defines reassignment through the separate `SET` statement:

```basic
SET Name = "Joy"
```

`LET` declares the variable, determines its fixed SMILE type, evaluates its initializer, and stores its initial runtime value. `SET` changes the current value only after an earlier successful declaration and only when the new value has exactly the same type. The complete assignment rules are defined by the [SMILE SET Statement Official Specification v1.0](SMILE%20-%20SET%20Statement%20Official%20Specification%20v1.0.md).

---

## 13. Declaration order and visibility

A variable becomes visible only after its `LET` initializer has been successfully evaluated and the declaration is complete.

Valid:

```basic
LET FirstName = "Sin"
LET Greeting = "Hello " + FirstName

PRINT {Greeting}
```

Invalid forward reference:

```basic
LET Greeting = "Hello " + FirstName
LET FirstName = "Sin"
```

Invalid self-reference:

```basic
LET Name = Name + "!"
```

In both invalid examples, the referenced variable is undefined at the point where it is used.

Version 1.0 defines one program-level scope.

A variable remains available from the statement after its declaration through the end of the program.

Future specifications may introduce nested scopes, functions, and separately defined shadowing rules.

---

## 14. Evaluation order

The initializer expression is evaluated from left to right.

For example:

```basic
LET Name = "Sin"
LET Greeting = "Hello " + Name + "!"
```

is evaluated conceptually as:

```text
concatenate(
    concatenate("Hello ", Name),
    "!"
)
```

The variable is created only after the initializer succeeds.

If the initializer contains an error, no usable variable binding is produced.

---

## 15. Ordinary string literals

An ordinary string literal begins and ends with `"` on one physical source line:

```basic
LET Name = "Sin"
LET Greeting = "Hello World!"
```

The string's contents preserve:

- Character case.
- Internal spaces.
- Punctuation.
- Semicolons.
- Braces.
- Dollar signs.

Braces inside an ordinary string do not interpolate:

```basic
LET Name = "Sin"
LET Greeting = "Hello {Name}!"

PRINT {Greeting}
```

Output:

```text
Hello {Name}!
```

This matches the official `PRINT` specification.

Smart opening and closing double-quotation marks MAY be accepted as source delimiters for beginner convenience:

```basic
LET Name = “Sin”
```

Generated code MUST use valid target-language string syntax.

Embedded quotation-mark escaping is outside version 1.0 unless separately defined by the general SMILE string specification.

The multiline SET Block String Literal is deliberately SET-only. It is not a valid LET initializer:

```basic
LET Name ="
S
 I
 N
"
```

This produces `SMILE1306`. Ordinary LET String expressions remain one-line.

---

## 16. Quote-free text is not allowed in LET

The quote-free raw-template convenience belongs only to `PRINT`.

Valid `PRINT`:

```basic
PRINT Hello World!
```

Invalid `LET`:

```basic
LET Greeting = Hello World!
```

The invalid statement MUST NOT silently create the string `"Hello World!"`.

An unquoted single identifier is a variable reference:

```basic
LET Name = "Sin"
LET Copy = Name
```

If that identifier has not already been declared, the compiler reports an undefined-variable error:

```basic
LET Name = Sin
```

The compiler MUST NOT guess that `Sin` is intended to be literal text.

To store literal text, use:

```basic
LET Name = "Sin"
```

This rule is required to keep the SMILE expression grammar deterministic as the language grows.

---

## 17. String concatenation

The `+` operator concatenates version 1.0 string expressions.

Examples:

```basic
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName

PRINT {FullName}
```

Output:

```text
Sin Cioco
```

Another example:

```basic
LET Name = "Sin"
LET Greeting = "Hello " + Name + "!"

PRINT {Greeting}
```

Output:

```text
Hello Sin!
```

All operands must evaluate to strings in version 1.0.

`+` is left-associative.

The following is invalid because the expression is incomplete:

```basic
LET Greeting = "Hello " +
```

The following is invalid if `MissingName` has not been declared:

```basic
LET Greeting = "Hello " + MissingName
```

String concatenation in `LET` uses the same expression semantics as the quoted-expression form of `PRINT`.

These are semantically related:

```basic
LET Greeting = "Hello " + Name + "!"
PRINT {Greeting}
```

and:

```basic
PRINT "Hello " + Name + "!"
```

---

## 18. Interpolated quoted strings

`LET` supports the same explicit interpolated quoted string syntax used by `PRINT`:

```basic
LET Name = "Sin"
LET Greeting = $"Hello {Name}!"

PRINT {Greeting}
```

Output:

```text
Hello Sin!
```

In an interpolated string:

- Ordinary text is copied as text.
- `{expression}` evaluates an expression.
- `{{` produces a literal `{`.
- `}}` produces a literal `}`.
- The closing quotation mark MUST occur on the same physical source line.

Example:

```basic
LET Placeholder = $"Use {{Name}} as a placeholder."

PRINT {Placeholder}
```

Output:

```text
Use {Name} as a placeholder.
```

Malformed interpolation is a syntax error:

```basic
LET Greeting = $"Hello {
LET Greeting = $"Hello {}
LET Greeting = $"Hello {Name"
LET Greeting = $"Hello Name}"
```

The interpolation rules MUST be the same rules used by the official `PRINT` specification.

---

## 19. Shared expression behavior with PRINT

`LET` initializers and the quoted-expression form of `PRINT` share one normal SMILE string-expression grammar.

Given:

```basic
LET Name = "Sin"
```

these use the same concatenation semantics:

```basic
LET Greeting = "Hello " + Name + "!"
PRINT {Greeting}
```

and:

```basic
PRINT "Hello " + Name + "!"
```

Both output:

```text
Hello Sin!
```

These use the same interpolated-string semantics:

```basic
LET Greeting = $"Hello {Name}!"
PRINT {Greeting}
```

and:

```basic
PRINT $"Hello {Name}!"
```

Both output:

```text
Hello Sin!
```

The following `PRINT` form is intentionally different:

```basic
PRINT Hello {Name}!
```

It is a `PRINT` raw template.

A `LET` initializer has no equivalent raw-template mode.

This is invalid:

```basic
LET Greeting = Hello {Name}!
```

Use one of these instead:

```basic
LET Greeting = $"Hello {Name}!"
LET Greeting = "Hello " + Name + "!"
```

---

## 20. PRINT behavior does not change after LET

A `LET` declaration never changes how the `PRINT` parser classifies bare text.

Given:

```basic
LET Name = "Sin"
LET Greeting = "Hello"
```

these output literal words:

```basic
PRINT Name
PRINT Greeting
```

Output:

```text
Name
Greeting
```

These evaluate the variables:

```basic
PRINT {Name}
PRINT {Greeting}
```

Output:

```text
Sin
Hello
```

The compiler MUST NOT use the symbol table to decide whether bare `PRINT` text should become an expression.

This compatibility rule prevents source meaning from changing when declarations are added or removed.

---

## 21. Semicolons

A semicolon is not a SMILE statement separator.

Invalid:

```basic
LET Name = "Sin"; PRINT {Name}
LET First = "Sin"; LET Last = "Cioco"
LET Name = "Sin";
```

A semicolon inside an ordinary or interpolated string remains string data:

```basic
LET Steps = "First; second; third"
LET Message = $"Hello; {Name}"

PRINT {Steps}
PRINT {Message}
```

A semicolon outside a string or interpolation expression is invalid in a version 1.0 `LET` initializer.

This is compatible with `PRINT`, where a semicolon in raw-template mode is printable text rather than a statement separator.

---

## 22. Comments

Version 1.0 does not define trailing inline comments for `LET`.

The complete initializer expression consumes the remainder of its physical source line.

When comments become official, their syntax will be defined separately and MUST preserve the one-statement-per-line rule.

Until then, comment-like character sequences inside strings are ordinary string data:

```basic
LET WebAddress = "https://example.com"
LET Message = "// This is stored text"
```

---

## 23. Formal parsing rule

After recognizing the case-insensitive `LET` keyword, the compiler performs these steps:

1. Require at least one space or tab after `LET`.
2. Require one valid identifier.
3. Reject the identifier if it is a reserved SMILE keyword.
4. Require one `=` symbol after optional horizontal whitespace.
5. Require an initializer expression after optional horizontal whitespace.
6. Parse the initializer using the normal SMILE expression grammar.
7. Require the initializer to end at the physical newline or end-of-file.
8. Reject a second statement on the same physical line.
9. Bind identifiers using ordinal case-insensitive comparison.
10. Reject undefined variable references.
11. Reject a duplicate declaration in the same scope.
12. Create the new variable only after successful initializer evaluation.

The compiler MUST NOT reinterpret an invalid expression as quote-free literal text.

---

## 24. Informal grammar

```text
let-statement
    ::= LET hspace+ identifier hspace* '=' hspace* initializer

initializer
    ::= string-expression

string-expression
    ::= string-term (hspace* '+' hspace* string-term)*

string-term
    ::= string-literal
      | identifier
      | interpolated-quoted-expression

interpolated-quoted-expression
    ::= '$"' interpolated-part* '"'

interpolated-part
    ::= interpolated-text
      | interpolation
      | '{{'
      | '}}'

interpolation
    ::= '{' string-expression '}'

identifier
    ::= identifier-start identifier-part*

identifier-start
    ::= ASCII-letter
      | '_'

identifier-part
    ::= ASCII-letter
      | ASCII-digit
      | '_'

hspace
    ::= space
      | tab
```

The general SMILE expression grammar may expand in future versions.

---

## 25. Semantic model

A conforming compiler SHOULD represent `LET` through language-neutral syntax and semantic nodes.

Conceptually:

```text
LetStatement
    VariableName
    InitializerExpression
```

Recommended expression categories shared with `PRINT` include:

```text
StringLiteralExpression
IntegerLiteralExpression
BooleanLiteralExpression
NameExpression
UnaryExpression
BinaryExpression
InterpolatedStringExpression
```

A semantic binding phase SHOULD:

- Create case-insensitive symbols.
- Resolve variable references.
- Detect undefined variables.
- Detect duplicate declarations.
- Associate the initializer with its SMILE type.
- Preserve one canonical declaration spelling.

Target-language generators MUST consume the language-neutral semantic representation rather than reparsing SMILE source.

`BoundLetStatement` carries the variable symbol and its bound initializer. It MUST NOT permanently own the variable's current value. The evaluator environment is the source of truth for current runtime state, while a separate statement-order execution analysis may record known values for diagnostics, simplification, Integer profiling, and low-level target planning. `AND` and `OR` evaluation follows the normative left-to-right short-circuit rules in the core expression specification even though binding and type checking still examine both operands.

---

## 26. Runtime semantics

Executing:

```basic
LET Name = "Sin"
```

performs these conceptual steps:

1. Evaluate the initializer `"Sin"`.
2. Produce the string value `Sin`.
3. Create the program-scope variable `Name`.
4. Store the value as that variable's initial current value in the evaluator environment.
5. Continue to the next statement.
6. Produce no output.

Executing:

```basic
PRINT {Name}
```

then:

1. Resolves `Name` case-insensitively.
2. Reads its current stored string value, including any value established by an earlier `SET`.
3. Writes the value.
4. Appends one newline.

`LET` itself does not write to standard output.

---

## 27. Target-language generation guidance

This section is informative rather than a requirement for exact source formatting.

Given:

```basic
LET Name = "Sin"
PRINT Hello {Name}!
```

a generator should normally preserve a visible native variable declaration so students can compare language concepts.

Possible target-language shapes include:

```csharp
string Name = "Sin";
Console.WriteLine($"Hello {Name}!");
```

```javascript
const Name = "Sin";
console.log(`Hello ${Name}!`);
```

```java
String Name = "Sin";
System.out.println("Hello " + Name + "!");
```

```c
const char *Name = "Sin";
printf("Hello Sin!\n");
```

```swift
let Name = "Sin"
print("Hello \(Name)!")
```

Exact generated structure may differ, but every target MUST preserve the observable SMILE behavior.

A target generator MUST safely map a valid SMILE identifier when the target language:

- Is case-sensitive.
- Reserves that spelling.
- Treats the spelling as a contextual or restricted identifier.
- Uses different identifier rules.
- Owns the spelling for generated runtime APIs or helper names.
- Reserves an identifier pattern, not only an exact word.
- Requires escaping or name mangling.

All references to the same case-insensitive SMILE variable MUST map to the same generated target symbol.

The identifier map MUST be symbol-based, deterministic, collision-safe, and target-specific. It MUST NOT be implemented as source-text replacement. A readable generated name such as `_smile_class` is acceptable when a valid SMILE variable such as `class` conflicts with a target keyword. If another SMILE declaration already uses that mapped spelling, the generator MUST choose a deterministic distinct spelling such as `_smile_class_2`.

For C and Objective-C, generators SHOULD map implementation-reserved identifier patterns such as names beginning with `__` or names beginning with `_` followed by an uppercase ASCII letter.

For Java and Swift, a single SMILE identifier `_` MUST be mapped to a usable destination-language local variable name.

Low-level targets such as C, Objective-C, COBOL, and MASM MAY use the initializer value known at the LET statement when creating declaration storage:

```c
const char *FullName = "Sin Cioco";
int Age = 49;
bool Adult = true;
```

This remains a valid declaration strategy when it preserves the complete value. If the variable is later targeted by `SET`, the destination storage must be mutable enough for the required update, and every SET statement must emit an actual storage update at its source position.

When MASM emits an empty string constant, it MAY use placeholder storage:

```asm
variable0Value BYTE 0
```

but the logical length MUST be zero:

```asm
variable0ValueLength EQU 0
```

This ensures `PRINT {Empty}` writes only the normal `PRINT` newline and does not emit a NUL byte.

---

## 28. Compatibility contract with PRINT and SET version 1.0

The official `LET`, `PRINT`, and `SET` specifications are jointly consistent under these rules:

1. `LET`, `PRINT`, and `SET` are case-insensitive keywords.
2. Identifiers referenced by any of these statements are case-insensitive.
3. All ordinary expression positions use the same ordinary string-literal rules.
4. All ordinary expression positions use the same `$"..."` interpolation rules.
5. All ordinary expression positions use the same string-concatenation semantics.
6. Ordinary quoted strings do not interpolate in any statement.
7. `{{` and `}}` produce literal braces in interpolated strings.
8. A newline normally terminates each statement.
9. Semicolons do not separate statements.
10. A physical source line normally contains one statement.
11. Quote-free raw templates exist only in `PRINT`.
12. `LET Name = Hello` means a variable reference, not literal text.
13. `PRINT Name` means literal template text, not a variable reference.
14. `PRINT {Name}` evaluates the variable's current value.
15. A variable must be declared before `PRINT`, `SET`, or another `LET` expression can use it.
16. `SET` changes the current value without changing the type established by `LET`.
17. A SET Block String Literal is a complete SET value only; it is not legal in LET or PRINT.
18. Adding or removing a `LET` declaration does not change how bare `PRINT` text is parsed.
19. All three statements use the same language-neutral expression model where expressions are allowed.
20. Target generators do not independently reinterpret statement source text or block delimiters.

An implementation that violates any of these rules does not conform to the combined specifications.

---

## 29. Combined LET and PRINT examples

### 29.1 Basic variable output

```basic
LET Name = "Sin"

PRINT Hello {Name}!
```

Output:

```text
Hello Sin!
```

### 29.2 Case-insensitive reference

```basic
let CustomerName = "Sin"

print Hello {CUSTOMERNAME}!
```

Output:

```text
Hello Sin!
```

### 29.3 Store a concatenated value

```basic
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName

PRINT Full name: {FullName}
```

Output:

```text
Full name: Sin Cioco
```

### 29.4 Store an interpolated value

```basic
LET Name = "Sin"
LET Greeting = $"Hello {Name}!"

PRINT {Greeting}
```

Output:

```text
Hello Sin!
```

### 29.5 Ordinary braces remain literal

```basic
LET Name = "Sin"
LET LiteralMessage = "Hello {Name}!"

PRINT {LiteralMessage}
```

Output:

```text
Hello {Name}!
```

### 29.6 Literal versus evaluated PRINT

```basic
LET Name = "Sin"

PRINT Name
PRINT {Name}
```

Output:

```text
Name
Sin
```

### 29.7 Equivalent ways to construct output

```basic
LET Name = "Sin"
LET Greeting = "Hello " + Name + "!"

PRINT {Greeting}
PRINT "Hello " + Name + "!"
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
```

Output:

```text
Hello Sin!
Hello Sin!
Hello Sin!
Hello Sin!
```

---

## 30. Required errors

A conforming compiler MUST reject:

```basic
LETName = "Sin"
LET = "Sin"
LET 2Name = "Sin"
LET First Name = "Sin"
LET LET = "Sin"
LET PRINT = "Sin"
LET SET = "Sin"
LET Name
LET Name =
LET Name = Hello World!
LET Name = "Sin" +
LET Name = MissingName
LET Name = $"Hello {
LET Name = $"Hello {}
LET Name = $"Hello {MissingName}!"
LET Name = "Sin"; PRINT {Name}
LET Name = "Sin" LET Other = "Joy"
```

It MUST also reject duplicate declarations:

```basic
LET Name = "Sin"
LET Name = "Joy"
```

and:

```basic
LET Name = "Sin"
LET NAME = "Joy"
```

Diagnostics MUST include:

- A stable diagnostic code.
- A human-readable message.
- One-based line.
- One-based column.
- A source span when available.

Expected source errors MUST NOT terminate the compiler, CLI, or desktop application.

---

## 31. Recommended diagnostic categories

The implementation SHOULD distinguish at least:

- Missing whitespace after `LET`.
- Missing or invalid identifier.
- Reserved keyword used as an identifier.
- Missing `=`.
- Missing initializer.
- Invalid initializer expression.
- Unterminated string.
- Unterminated interpolation.
- Empty interpolation.
- Unexpected closing brace.
- Undefined variable.
- Duplicate variable declaration.
- Unsupported value type.
- Unexpected text after initializer.
- Multiple statements on one line.

The repository's central diagnostic catalog, when present, is authoritative for exact numeric codes.

Current stable diagnostic codes for this specification include:

| Code | Meaning |
|---|---|
| `SMILE1001` | Unknown statement or keyword |
| `SMILE1003` | Unterminated string literal |
| `SMILE1005` | Invalid or unexpected character |
| `SMILE1103` | Unterminated interpolation expression |
| `SMILE1104` | Unexpected closing brace in template |
| `SMILE1105` | Interpolation expression cannot be empty |
| `SMILE1106` | Undefined variable |
| `SMILE1107` | Duplicate variable declaration |
| `SMILE1109` | Semicolons cannot separate SMILE statements |
| `SMILE1110` | Unterminated interpolated string |
| `SMILE1111` | Unexpected text after a string expression |
| `SMILE1112` | `LET` requires a valid variable name |
| `SMILE1113` | `LET` requires `=` before its initializer |
| `SMILE1115` | Reserved SMILE keyword used as a variable name |
| `SMILE1116` | `LET` requires an initializer expression |
| `SMILE1201` | Invalid or unexpected token in expression |
| `SMILE1202` | Integer literal is outside the signed 64-bit range |
| `SMILE1203` | Unary operator is not defined for the operand type |
| `SMILE1204` | Binary operator is not defined for the operand types |
| `SMILE1205` | Missing closing parenthesis |
| `SMILE1206` | Integer arithmetic overflow |
| `SMILE1207` | Division by zero |
| `SMILE1208` | Unknown or invalid string escape sequence |
| `SMILE1209` | Unterminated string escape sequence |
| `SMILE1306` | A SET Block String Literal is valid only as the complete value of SET |

---

## 32. Future compatibility

This specification is designed to remain valid as SMILE gains:

- Decimal variables.
- Dates and times.
- Arrays.
- Objects.
- Functions.
- Function-local variables.
- Nested scopes.
- Constants.
- Explicit type annotations.
- Type inference across more types.
- User-defined types.

SMILE v0.4.1 uses the same form for the official `String`, `Integer`, and `Boolean` core types:

```basic
LET Age = 49
LET Enabled = TRUE
```

The official v0.4.1 expression grammar and short-circuit evaluation rules are defined in:

- [SMILE - Core Types and Expressions Official Specification v1.0](SMILE%20-%20Core%20Types%20and%20Expressions%20Official%20Specification%20v1.0.md)

Future expression features may expand valid initializers beyond this core:

```basic
LET Total = Price * Quantity
LET DisplayName = GetDisplayName(User)
```

Future specifications MUST preserve these version 1.0 rules:

- `LET` begins a declaration.
- A name is compared case-insensitively.
- An initializer is required.
- Quote-free text is not automatically a string.
- The initializer uses the normal expression grammar.
- A declaration does not change the meaning of bare `PRINT` text.
- One statement normally occupies one line.

---

## 33. Summary

The official SMILE `LET` rules are:

1. `LET`, `PRINT`, and `SET` are official SMILE statement keywords.
2. SMILE keywords and identifiers are case-insensitive.
3. `LET` declares and initializes a new variable.
4. Version 1.0 originally introduced string variables; SMILE v0.4.1 supports official `LET` initializers for `String`, `Integer`, and `Boolean` through the core expression specification.
5. An initializer is required.
6. A variable becomes visible only after its declaration succeeds.
7. A variable must be declared before use.
8. Duplicate declarations are errors, including case-only duplicates.
9. `SET` is the only assignment statement in v0.5.0; it changes the current value without changing the type established by `LET`.
10. Ordinary quoted strings do not interpolate.
11. `$"..."` strings interpolate using the same rules as `PRINT`.
12. `+` concatenates strings and adds integers according to the core expression type rules.
13. Quote-free raw templates are exclusive to `PRINT`.
14. Ordinary LET String expressions remain one-line; SET Block String Literals are not valid LET initializers.
15. `LET Name = Hello` is a variable reference, not literal text.
16. `PRINT Name` prints literal text.
17. `PRINT {Name}` evaluates the variable's current value.
18. A newline normally terminates one statement; a SET Block String Literal is one explicit logical multiline exception.
19. Semicolons do not separate statements.
20. Target generators consume the shared lexer/parser/binder/evaluator semantic representation and statement-order analysis.
21. `LET`, `SET`, and `PRINT` MUST preserve identical expression and identifier behavior when used together.

---

## 34. License

This specification is part of the SMILE project and follows the repository's GNU Affero General Public License v3.0-only licensing.
