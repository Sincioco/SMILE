# SMILE PRINT Statement — Official Language Specification

**Specification version:** 1.0  
**Status:** Official  
**Applies to:** SMILE language  
**Primary statement:** `PRINT`  
**Repository destination:** `docs/SMILE Language Specification/SMILE - PRINT Statement Official Specification v1.0.md`

---

## 1. Purpose

`PRINT` writes text to the program's standard output and appends one newline.

Evaluated variable references read the variable's current runtime value at the `PRINT` statement. An earlier `SET` therefore changes what every later direct expression, raw-template hole, and interpolated String hole prints.

SMILE v0.6.0 permits PRINT in IF, ELSE IF, ELSE, and nested IF bodies. A PRINT executes only when its containing branch is selected. All PRINT source text in unselected branches is still parsed and bound, and every target retains the complete branch structure.

SMILE deliberately provides both:

1. A forgiving, beginner-friendly template form.
2. Explicit quoted and interpolated expression forms for advanced programmers.

The relaxed syntax is limited to `PRINT`. It does not make unquoted strings legal everywhere in SMILE.

These statements are valid examples:

```basic
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
```

Given:

```basic
LET Name = "Sin"
```

the final three statements above output:

```text
Hello Sin!
```

---

## 2. Normative terminology

The words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** describe requirements of this specification.

---

## 3. Case-insensitivity

SMILE is case-insensitive.

The following keywords are equivalent:

```basic
PRINT
Print
print
pRiNt
```

Identifiers are also case-insensitive:

```basic
LET Name = "Sin"

PRINT Hello {Name}!
PRINT Hello {name}!
PRINT Hello {NAME}!
```

All three references identify the same variable.

The compiler MUST use culture-independent, ordinal case-insensitive comparison for keywords and identifiers.

The compiler SHOULD preserve the spelling used at declaration time for diagnostics and generated code.

String data remains case-sensitive:

```basic
"Hello"
"HELLO"
```

These are different string values.

---

## 4. One statement per line

A newline normally terminates a SMILE statement.

A physical source line MUST NOT contain more than one SMILE statement.

SMILE does not use semicolons to separate statements.

This is valid:

```basic
PRINT Hello
PRINT World
```

This is invalid:

```basic
PRINT "Hello"; PRINT "World"
```

A semicolon inside a raw `PRINT` template is ordinary printable punctuation:

```basic
PRINT First step; second step; third step.
```

Output:

```text
First step; second step; third step.
```

All forms of `PRINT` defined by this specification must fit on one physical source line. The multiline SET Block String Literal is a SET-only source form and is not legal directly in `PRINT`.

IF headers and terminators have their own one-logical-line rules. PRINT text inside a branch does not change how ELSE, ELSE IF, or END IF is recognized on later lines.

Future SMILE versions may permit visibly incomplete expressions to continue across lines, but that does not permit multiple statements on one line.

---

## 5. Required whitespace after `PRINT`

When `PRINT` has a payload, at least one space or tab MUST separate the keyword from the payload.

Valid:

```basic
PRINT "Hello"
PRINT Hello
PRINT    Hello
PRINT	Hello
```

Invalid:

```basic
PRINT"Hello"
PRINT$"Hello"
```

`PRINT` followed only by spaces or tabs is equivalent to `PRINT` with no payload and prints one blank line.

The whitespace that separates `PRINT` from its payload is not part of the output.

---

## 6. PRINT forms

After reading the `PRINT` keyword and its separating whitespace, the compiler selects exactly one of four forms.

### 6.1 Blank-line form

```basic
PRINT
```

or:

```basic
PRINT    
```

Output:

```text

```

The statement writes one newline.

---

### 6.2 Explicit interpolated-string form

When the first two payload characters are `$"`, the payload is an interpolated string expression:

```basic
PRINT $"Hello {Name}!"
```

Ordinary text is copied as text.

An expression inside `{` and `}` is evaluated and converted to text.

Given:

```basic
LET Name = "Sin"
```

the output is:

```text
Hello Sin!
```

The closing quotation mark MUST occur on the same physical source line.

---

### 6.3 Quoted expression form

When the first payload character is `"`, the compiler parses the complete remainder of the line as a normal SMILE expression.

Examples:

```basic
PRINT "Hello World!"
PRINT "Hello " + Name + "!"
PRINT "First: " + FirstName + ", Last: " + LastName
```

This form supports explicit string concatenation and future SMILE expression features.

Ordinary quoted strings do not interpolate automatically:

```basic
LET Name = "Sin"
PRINT "Hello {Name}!"
```

Output:

```text
Hello {Name}!
```

To interpolate, use:

```basic
PRINT $"Hello {Name}!"
```

or:

```basic
PRINT Hello {Name}!
```

---

### 6.4 Raw template form

When the payload begins with anything other than `$"` or `"`, the remainder of the physical line is a raw template.

Example:

```basic
PRINT Hello World!
```

Output:

```text
Hello World!
```

Raw template text is literal except for interpolation expressions enclosed in `{` and `}`.

Example:

```basic
LET Name = "Sin"
PRINT Hello {Name}!
```

Output:

```text
Hello Sin!
```

The compiler MUST NOT guess whether a bare word is a variable.

Therefore:

```basic
PRINT Name
```

prints:

```text
Name
```

To print the value stored in `Name`, use:

```basic
PRINT {Name}
```

This deterministic rule prevents a statement from changing meaning merely because a variable is later declared.

---

## 7. Raw template whitespace

In a raw template:

- Separating whitespace immediately after `PRINT` is ignored.
- Trailing spaces and tabs before the newline are ignored.
- Internal spaces and tabs are preserved exactly.
- To preserve intentional leading or trailing whitespace, use a quoted string.

Examples:

```basic
PRINT       Hello
PRINT Hello     World
PRINT "    Indented"
PRINT "Trailing spaces    "
```

Outputs:

```text
Hello
Hello     World
    Indented
Trailing spaces    
```

---

## 8. Template interpolation

Both raw templates and `$"..."` strings support interpolation.

### 8.1 Expression holes

An interpolation expression begins with `{` and ends at the matching `}`:

```basic
PRINT Hello {Name}!
PRINT The result is {Left + Right}.
PRINT $"Hello {Name}!"
```

The expression inside the braces follows the normal SMILE expression grammar.

The current implementation may initially support a smaller expression subset, but the syntax MUST remain compatible with future full expressions.

The value is converted to text using SMILE's language-defined text conversion rules.

### 8.2 Literal braces

In a raw template or interpolated string:

```text
{{  produces {
}}  produces }
```

Examples:

```basic
PRINT Use {{Name}} to show a placeholder.
PRINT $"Use {{Name}} to show a placeholder."
```

Output:

```text
Use {Name} to show a placeholder.
Use {Name} to show a placeholder.
```

### 8.3 Invalid interpolation

These are syntax errors:

```basic
PRINT Hello {
PRINT Hello {}
PRINT Hello {Name
PRINT Hello Name}
PRINT $"Hello {Name"
```

The compiler MUST report the location of the malformed interpolation.

---

## 9. Ordinary quoted strings

An ordinary quoted string begins and ends with `"` on one physical source line.

Braces inside an ordinary quoted string are ordinary characters:

```basic
PRINT "Hello {Name}!"
```

No interpolation occurs.

Smart opening and closing double-quotation marks MAY be accepted as source delimiters for beginner convenience:

```basic
PRINT “Hello World!”
```

Generated code MUST use valid target-language quotation syntax.

Embedded quotation-mark escaping is outside version 1.0 of this `PRINT` specification unless separately defined by the general SMILE string specification.

A quote at the end of a physical line cannot begin a SET Block String Literal here. That form is valid only as the complete value of `SET` and produces the SET placement diagnostic when used directly in `PRINT`.

---

## 10. String concatenation

The quoted expression form supports string concatenation with `+`.

Example:

```basic
LET Name = "Sin"
PRINT "Hello " + Name + "!"
```

Output:

```text
Hello Sin!
```

SMILE's semantic rules determine how non-string values are converted during concatenation.

Until additional types are formally introduced, implementations may limit concatenation to string values.

The following begins with an unquoted word and is therefore a raw template, not an expression:

```basic
PRINT Name + "!"
```

It outputs literally:

```text
Name + "!"
```

To evaluate the expression, use one of these forms:

```basic
PRINT {Name + "!"}
PRINT $"{Name}!"
PRINT "" + Name + "!"
```

The final example is legal but not the recommended style.

---

## 11. Duplicate PRINT keyword rule

The initial `PRINT` keyword is the only statement-level `PRINT` keyword permitted on a physical line.

If a second standalone, case-insensitive `PRINT` keyword token is encountered on the same line outside a quoted string, the compiler MUST report a syntax error.

Invalid:

```basic
PRINT Hello PRINT World
print Hello PrInT World
PRINT "Hello"; PRINT "World"
PRINT Use PRINT to display text.
```

Valid:

```basic
PRINT "Use PRINT to display text."
PRINT Reprint this report.
PRINT PRINTABLE text.
PRINT Use "PRINT" as the command name.
PRINT Use {"PRINT"} as the command name.
```

A standalone keyword token is determined using SMILE identifier boundaries. A substring inside a longer identifier or word does not count as another keyword.

For example:

```text
Reprint
PRINTABLE
my_print_value
```

do not contain a standalone second `PRINT` keyword.

The implementation MUST use lexical context. It MUST NOT use a naive substring search such as `Contains("PRINT")`.

---

## 12. Semicolons

A semicolon is not a SMILE statement separator.

In raw template mode, it is printed literally:

```basic
PRINT A; B; C
```

Output:

```text
A; B; C
```

In quoted expression mode, a semicolon outside a quoted string is invalid syntax:

```basic
PRINT "A"; "B"
```

A semicolon inside a quoted string remains text:

```basic
PRINT "A; B; C"
```

---

## 13. Comments

A raw `PRINT` template consumes the remainder of its physical source line.

Therefore, raw `PRINT` does not have a trailing inline-comment form.

For example:

```basic
PRINT Hello // greeting
```

prints:

```text
Hello // greeting
```

When comments are supported by SMILE, comments intended to accompany a raw `PRINT` statement SHOULD be placed on their own line.

This avoids conflicts with printable apostrophes, slashes, number signs, and other punctuation.

---

## 14. Formal dispatch rule

After recognizing the case-insensitive `PRINT` keyword:

1. If the line ends after optional horizontal whitespace, create a blank-line print.
2. Otherwise, require at least one space or tab after `PRINT`.
3. If the payload begins with `$"`, parse an interpolated-string expression.
4. Else if the payload begins with `"`, parse the remainder of the line as a normal expression.
5. Else parse the remainder of the line as a raw template.
6. Reject a second standalone `PRINT` keyword token on the same physical line outside quoted text.
7. Reject a SET Block String Literal; it is not a PRINT payload form.
8. Require the complete statement to end at the physical newline or end-of-file.

The compiler MUST NOT select a form by trying one grammar and silently falling back to another.

The form is selected only from the visible leading characters described above.

---

## 15. Informal grammar

This grammar is intentionally line-oriented.

```text
print-statement
    ::= PRINT
      | PRINT hspace+ interpolated-quoted-expression
      | PRINT hspace+ quoted-expression
      | PRINT hspace+ raw-template

interpolated-quoted-expression
    ::= '$"' interpolated-part* '"'

quoted-expression
    ::= string-expression

string-expression
    ::= string-term (hspace* '+' hspace* string-term)*

string-term
    ::= string-literal
      | identifier
      | interpolated-quoted-expression
      | future-expression-term

raw-template
    ::= raw-template-part*

raw-template-part
    ::= raw-text
      | interpolation
      | '{{'
      | '}}'

interpolation
    ::= '{' expression '}'

hspace
    ::= space | tab
```

Form selection follows Section 14 and is not determined solely by this grammar.

---

## 16. Semantic equivalence

Given:

```basic
LET Name = "Sin"
```

these statements are semantically equivalent:

```basic
PRINT "Hello " + Name + "!"
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
```

All three represent:

```text
Print(
    concatenate(
        "Hello ",
        convert-to-text(Name),
        "!"
    )
)
```

Although the runtime output is the same, the programmer's expression intent is different:

- `PRINT Hello {Name}!` is interpolation-oriented friendly syntax.
- `PRINT $"Hello {Name}!"` is explicit interpolation.
- `PRINT "Hello " + Name + "!"` is explicit concatenation.

Target generators SHOULD preserve the programmer's expression form when the destination language provides a clear and idiomatic equivalent. Interpolated SMILE expressions SHOULD generate native interpolation syntax where supported. Explicit concatenation SHOULD remain concatenation. If the destination language lacks a close equivalent, the generator MUST emit semantically equivalent code.

---

## 17. Internal representation guidance

This section is normative for compatibility but not for specific class names.

A conforming compiler SHOULD represent every `PRINT` statement as:

```text
PrintStatement
    ValueExpression
```

Recommended expression categories include:

```text
StringLiteralExpression
IntegerLiteralExpression
BooleanLiteralExpression
NameExpression
UnaryExpression
BinaryExpression
InterpolatedStringExpression
```

An interpolated string SHOULD contain ordered parts:

```text
TextPart
ExpressionPart
TextPart
...
```

Raw templates and `$"..."` interpolated strings SHOULD lower to the same language-neutral semantic representation.

Target-language generators MUST consume the language-neutral representation rather than reparsing SMILE source text.

The language-neutral representation MUST preserve enough expression shape for generators to distinguish interpolation-oriented expressions from explicit concatenation. Implementations MAY flatten expressions into literal and variable output segments inside targets that need lower-level output planning, such as C, Objective-C, or assembly, but that target-local lowering MUST NOT become the canonical semantic representation used by every backend.

Generated target code SHOULD be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write. C, Objective-C, assembly, and other lower-level targets MAY lower interpolation and concatenation into target-specific output operations. However, the generator SHOULD still choose the clearest idiomatic destination-language form. For C string output, a single safe `printf` call with a compiler-generated format string is generally preferred over exposing every internal literal and variable segment as a separate output statement.

When a `PRINT` expression references a variable, the target generator MUST use the same symbol-based target identifier map as the corresponding `LET` declaration and every `SET` assignment. This keeps valid SMILE identifiers safe in target languages without changing bare `PRINT` text into variable references.

---

## 18. Examples

Assume:

```basic
LET Name = "Sin"
```

| Source | Output |
|---|---|
| `PRINT` | one blank line |
| `PRINT "Hello World!"` | `Hello World!` |
| `print Hello World!` | `Hello World!` |
| `PRINT Name` | `Name` |
| `PRINT {Name}` | `Sin` |
| `PRINT Hello {Name}!` | `Hello Sin!` |
| `PRINT $"Hello {Name}!"` | `Hello Sin!` |
| `PRINT "Hello " + Name + "!"` | `Hello Sin!` |
| `PRINT "Hello {Name}!"` | `Hello {Name}!` |
| `PRINT Literal: {{Name}}` | `Literal: {Name}` |
| `PRINT 1 + 2` | `1 + 2` |
| `PRINT 1 + 2 = {1 + 2}` | `1 + 2 = 3` when numeric expressions exist |
| `PRINT A; B; C` | `A; B; C` |

---

## 19. Required errors

A conforming compiler MUST reject:

```basic
PRINT"Hello"
PRINT$"Hello"
PRINT Hello PRINT World
PRINT Hello {
PRINT Hello {}
PRINT Hello {Name
PRINT Hello Name}
PRINT $"Hello {Name"
PRINT "Hello" + 
PRINT "Hello"; PRINT "World"
PRINT "
Block text
"
```

The multiline example produces `SMILE1306` because a SET Block String Literal is valid only as the complete value of `SET`.

Diagnostics MUST include:

- A stable diagnostic code.
- A human-readable message.
- One-based line.
- One-based column.
- A source span when available.

Expected source errors MUST NOT terminate the compiler or desktop application.

---

## 20. Quote-free text is PRINT-specific

The following is a raw template:

```basic
PRINT Hello World!
```

This rule MUST NOT automatically make quote-free strings legal in declarations, assignments, function calls, conditions, or return statements.

For example:

```basic
LET Message = "Hello World!"
```

is a string assignment.

This is not implicitly a string:

```basic
LET Message = Hello World!
```

It is invalid or follows the separately defined expression grammar.

Restricting quote omission to `PRINT` prevents ambiguity as SMILE grows.

In particular, bare IF condition text never gains PRINT's raw-template behavior. IF conditions use the ordinary expression grammar plus the explicit-comparison and call-free rules in [SMILE - IF Statement Official Specification v1.0](SMILE%20-%20IF%20Statement%20Official%20Specification%20v1.0.md).

---

## 21. Future compatibility

This specification is designed to remain valid as SMILE gains:

- Functions.
- Arrays and objects.
- Formatting specifications.
- User-defined types.
- A full semantic type system.

SMILE v0.4.1 defines official numeric and boolean expressions for these expression positions:

```basic
PRINT {expression}
PRINT $"...{expression}..."
PRINT "text" + expression
```

The official v0.4.1 expression grammar and short-circuit evaluation rules are defined in:

- [SMILE - Core Types and Expressions Official Specification v1.0](SMILE%20-%20Core%20Types%20and%20Expressions%20Official%20Specification%20v1.0.md)

Future versions may expand the expression grammar further without changing the raw-template rules.

SMILE v0.6.0 reuses these same expression, interpolation, display, and current-value rules for PRINT statements inside conditional branches. Only the selected branch produces output.

Future versions MUST preserve the deterministic distinction:

```text
bare text      = literal template text
{expression}   = evaluated value
"..."          = ordinary quoted string
$"..."         = interpolated quoted string
```

---

## 22. Summary

The official SMILE `PRINT` rules are:

1. SMILE keywords and identifiers are case-insensitive.
2. A newline normally terminates one statement.
3. Semicolons do not separate statements.
4. Only one statement is permitted per physical line.
5. `PRINT` alone writes a blank line.
6. `PRINT "..."` begins a normal quoted expression.
7. `PRINT $"..."` begins an explicit interpolated string.
8. All other `PRINT` payloads are raw templates.
9. Raw text is literal.
10. `{expression}` evaluates an expression.
11. `{{` and `}}` produce literal braces.
12. `PRINT Name` prints `Name`; `PRINT {Name}` prints the variable value.
13. Ordinary quoted strings do not interpolate.
14. A second standalone `PRINT` keyword on the same line is a syntax error.
15. Quote-free string convenience is specific to `PRINT`.
16. Every form lowers to a common language-neutral expression representation.
17. Target generators preserve expression intent when the target language has an idiomatic equivalent.
18. Evaluated variable references read the current value established by earlier `LET` and `SET` statements.
19. SET Block String Literals are not legal directly in `PRINT`.
20. PRINT is permitted in every IF v1.0 body and executes only when that branch is selected.
21. Bare PRINT templates do not change IF condition parsing or ELSE/END IF terminator recognition.

---

## 23. License

This specification is part of the SMILE project and follows the repository's GNU Affero General Public License v3.0-only licensing.
