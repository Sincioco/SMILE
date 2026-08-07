# 008 - SMILE - INPUT Statement Official Specification v1.0

## Status

This document is the complete official language specification for the SMILE `INPUT` statement.

It is introduced by:

> **SMILE v0.7.0 — INPUT**

This specification works together with:

- `001 - SMILE - SET Statement Official Specification v1.0.md`
- `002 - SMILE - PRINT Statement Official Specification v1.0.md`
- `003 - SMILE - String Literals Official Specification v1.0.md`
- `004 - SMILE - Core Types and Expressions Official Specification v1.0.md`
- `005 - SMILE - LET Statement Official Specification v1.0.md`
- `006 - SMILE - IF Statement Official Specification v1.0.md`
- `007 - SMILE - Full-Line Comments and Source Layout Preservation Official Specification v1.0.md`
- `009 - SMILE - WHILE Statement Official Specification v1.0.md`

When this specification introduces runtime-value behavior that did not exist before `INPUT`, this specification is normative for SMILE v0.7.0 and later.

---

# 1. Purpose

`INPUT` reads one line from standard input and assigns the entered value to an already-declared SMILE variable.

Example:

```smile
LET Name = ""

PRINT Enter your name:
INPUT Name

PRINT Hello {Name}!
```

A learner declares the variable and establishes its type with `LET`, then changes its value at runtime with `INPUT`.

---

# 2. Canonical syntax

The only official v1.0 syntax is:

```text
INPUT variable
```

Examples:

```smile
INPUT Name
INPUT Age
INPUT Continue
```

`INPUT` is case-insensitive:

```smile
input Name
Input Age
iNpUt Continue
```

The target variable is resolved using SMILE's existing ordinal case-insensitive identifier lookup.

---

# 3. Formal grammar

```text
input-statement ->
    INPUT hspace+ identifier hspace* statement-end

hspace ->
    space
    | tab

statement-end ->
    line-end
    | end-of-file
```

The statement accepts exactly one identifier.

The following are not v1.0 syntax:

```smile
INPUT
INPUT Name, Age
INPUT "Prompt", Name
INPUT Name AS String
INPUT {Name}
INPUT GetName()
```

---

# 4. `INPUT` is a statement, not an expression

`INPUT` cannot appear inside an expression, interpolation, assignment expression, IF condition, or WHILE condition.

Invalid:

```smile
LET Age = INPUT
```

```smile
IF INPUT Age >= 18 THEN
END IF
```

```smile
PRINT {INPUT Name}
```

The permanent IF rule remains unchanged: condition evaluation cannot invoke an input operation, function, or procedure.

---

# 5. Declaration and fixed type

The target variable must already have been successfully declared by `LET`.

Valid:

```smile
LET Age = 0
INPUT Age
```

Invalid:

```smile
INPUT Age
```

because `Age` has not been declared.

The variable's existing SMILE type determines how the input line is interpreted:

| Existing variable type | Input interpretation |
|---|---|
| `String` | The entered line becomes the String value |
| `Integer` | The entered line is parsed as a signed 64-bit decimal Integer |
| `Boolean` | The entered line is parsed as `TRUE` or `FALSE` |

`INPUT` never changes the variable's type.

---

# 6. Relationship among LET, SET, and INPUT

SMILE gives the three statements distinct responsibilities:

```text
LET    declares a variable and gives it its initial value and type
SET    changes an existing variable using a SMILE expression
INPUT  changes an existing variable using one line read at runtime
```

Example:

```smile
LET Score = 0
SET Score = 10
INPUT Score
```

After `INPUT`, `Score` remains an `Integer`.

---

# 7. `INPUT` is a reserved keyword

Beginning with SMILE v0.7.0, `INPUT` is a case-insensitive reserved SMILE keyword.

It cannot be used as a variable name:

```smile
LET INPUT = 1
```

The existing reserved-keyword diagnostic applies.

The contextual full-line comment marker `REM` remains unaffected and may still be used as a variable name outside first-position comment syntax:

```smile
LET REM = ""
INPUT REM
```

---

# 8. Where INPUT is permitted

`INPUT` is permitted:

- at the top level of a SMILE program;
- inside an `IF` body;
- inside an `ELSE IF` body;
- inside an `ELSE` body;
- inside a nested `IF`;
- inside a `WHILE` body, including a WHILE nested in IF or another WHILE.

Example:

```smile
LET HasAccount = TRUE
LET Age = 0

IF HasAccount = TRUE THEN
    PRINT Enter your age:
    INPUT Age
END IF
```

Only an executed branch or reached loop iteration consumes input.

`INPUT` does not introduce a block scope.

---

# 9. Standard input

Each executed `INPUT` statement consumes exactly one logical line from standard input.

A logical line ends at:

- CRLF;
- LF;
- standalone CR; or
- end-of-file after at least one character has been received.

The line terminator is consumed but is not part of the value.

A final non-empty line without a terminating newline is valid input.

End-of-file before any character is available for the requested line is a runtime error.

---

# 10. Input encoding

SMILE input is Unicode.

For redirected byte streams, the normative encoding is UTF-8 without requiring a byte-order mark.

Interactive destination runtimes may obtain Unicode from their native console APIs, but the resulting logical characters must match UTF-8 SMILE semantics.

Malformed redirected UTF-8 input is a runtime input error.

All targets must produce the same logical String value for the same valid UTF-8 input bytes.

---

# 11. Maximum input-line size

One logical input line may contain at most:

```text
4096 UTF-8 bytes
```

The count is measured:

- after removing the line terminator;
- before trimming Integer or Boolean surrounding spaces or tabs.

A longer line is a runtime error.

This common limit gives every current SMILE target, including C, COBOL, Objective-C, and Windows x64 MASM, one deterministic cross-platform contract.

A future specification may raise the limit without changing valid v1.0 programs.

---

# 12. String INPUT semantics

For a `String` target, the complete input line becomes the new String value.

Example:

```smile
LET Name = ""
INPUT Name
PRINT [{Name}]
```

Input:

```text
  Sin  
```

Output:

```text
[  Sin  ]
```

String input preserves:

- leading spaces;
- trailing spaces;
- tabs;
- Unicode characters;
- an empty line;
- embedded NUL when supplied through redirected input;
- every character other than the consumed line terminator.

No escape sequences are decoded during input.

For example, entering the two characters `\` and `n` stores those two characters; it does not create a line feed.

---

# 13. Empty String input

An empty physical input line is valid for a String variable.

Example:

```smile
LET Value = "Before"
INPUT Value
PRINT [{Value}]
```

Input consists of an immediate line ending.

Output:

```text
[]
```

---

# 14. Integer INPUT syntax

For an `Integer` target:

1. remove leading and trailing ASCII spaces and tabs;
2. parse the remaining text using the grammar below.

```text
input-integer ->
    sign? decimal-digit+

sign ->
    '+'
    | '-'

decimal-digit ->
    '0' through '9'
```

Valid examples:

```text
0
49
-49
+49
  49
-49    
```

Invalid examples:

```text
(empty)
+
-
1.5
1,000
1_000
0x31
49 years
```

Parsing is invariant and independent of the user's locale.

The resulting value must be within:

```text
-9223372036854775808 through 9223372036854775807
```

`-0` and `+0` are accepted and produce `0`.

---

# 15. Boolean INPUT syntax

For a `Boolean` target:

1. remove leading and trailing ASCII spaces and tabs;
2. compare the remaining text ordinal case-insensitively.

The only accepted values are:

```text
TRUE
FALSE
```

Valid examples:

```text
TRUE
true
False
  TrUe  
```

Invalid examples:

```text
1
0
YES
NO
ON
OFF
T
F
```

SMILE does not introduce truthy or falsy input values.

---

# 16. Atomic assignment

`INPUT` changes the target variable only after:

1. one complete line has been read;
2. its UTF-8 size has been validated;
3. its type-specific parsing has succeeded.

If reading or conversion fails, the assignment does not occur.

The program then terminates according to the runtime-error rules in this specification.

---

# 17. Multiple INPUT statements

Executed `INPUT` statements consume lines in execution order.

Example:

```smile
LET First = ""
LET Second = ""

INPUT First
INPUT Second

PRINT {First}
PRINT {Second}
```

Input:

```text
Alpha
Beta
```

Output:

```text
Alpha
Beta
```

Extra unread input lines do not affect the program.

---

# 18. Branch-dependent input consumption

Only the selected branch consumes input.

Example:

```smile
LET UseExisting = TRUE
LET Name = "Default"

IF UseExisting = FALSE THEN
    INPUT Name
END IF

PRINT {Name}
```

No line is consumed because the IF body does not execute.

Short-circuit and first-successful-clause rules remain unchanged.

---

# 19. Comments and source layout

The ordered source-layout rules from specification `007` apply to `INPUT`.

Example:

```smile
LET Age = 0

// Read the learner's age.
INPUT Age

PRINT {Age}
```

The blank lines and comment remain non-semantic, retain their source order, and are preserved in generated target source.

Marker-looking text inside a SET Block String Literal remains String data, not an `INPUT` statement or comment.

---

# 20. Compile-time value knowledge

After an `INPUT` statement, the target variable's type remains known, but its value is runtime-unknown.

Example:

```smile
LET Age = 0
INPUT Age
```

After `LET`, the compiler may know:

```text
Age = 0
```

After `INPUT`, the compiler knows only:

```text
Age has type Integer
Age may contain any valid signed 64-bit input value
```

The compiler must not reuse the pre-input value.

Invalid optimization:

```csharp
// Wrong: the INPUT result was ignored.
long Age = 0;
ReadInput();
Console.WriteLine(0);
```

Required behavior:

```csharp
long Age = 0;
Age = ReadInputInteger("Age");
Console.WriteLine(Age);
```

---

# 21. Static analysis after INPUT

For a variable assigned by `INPUT`, analysis must use these conservative facts:

| Type | Required possible-value facts |
|---|---|
| `String` | Unknown value, 0–4096 UTF-8 bytes, may contain NUL |
| `Integer` | Unknown value, full signed 64-bit range |
| `Boolean` | Unknown value, either `TRUE` or `FALSE` |

`INPUT` is a mutation for statement-order, branch-merge, and target-storage analysis.

After an IF merge, a value is known only when every outgoing path proves the same value under the existing branch-aware rules.

---

# 22. Integer target profiles after INPUT

An Integer variable targeted by `INPUT` can receive any signed 64-bit value.

Therefore, every destination must use a representation that preserves the full SMILE Integer range for that variable and every dependent runtime expression.

Examples include:

- C and Objective-C: `int64_t`;
- C#: `long`;
- Java: `long`;
- JavaScript: `BigInt`;
- Swift: `Int64`;
- C++: `std::int64_t`;
- Python: `int` with explicit signed-64 validation;
- MASM x64: signed 64-bit storage;
- COBOL: a representation proven to preserve the complete signed 64-bit range.

A narrower target profile is not valid merely because the variable's original LET initializer was small.

---

# 23. Runtime-dependent arithmetic

Before `INPUT`, every valid SMILE value could be known from source.

After `INPUT`, arithmetic can depend on runtime values:

```smile
LET Left = 0
LET Right = 0

INPUT Left
INPUT Right

LET Result = Left / Right
PRINT {Result}
```

All targets must preserve SMILE's checked signed 64-bit arithmetic semantics at runtime.

Runtime-dependent operations must detect:

- addition overflow;
- subtraction overflow;
- multiplication overflow;
- unary negation overflow;
- division by zero;
- `-9223372036854775808 / -1` overflow.

---

# 24. Compile-time versus runtime arithmetic errors

The existing compile-time diagnostics remain when an error is definitely evaluated from source-known values.

Example:

```smile
LET Result = 1 / 0
```

This remains compile-time diagnostic `SMILE1207`.

When evaluation depends on runtime input, the program may compile and the error occurs only if the failing operation is reached.

Example:

```smile
LET Divisor = 0
INPUT Divisor
LET Result = 1 / Divisor
```

Entering `0` produces runtime error `SMILER1207`.

Entering `2` succeeds.

---

# 25. Short-circuit and branch reachability

Runtime errors occur only when the operation is actually evaluated.

Example:

```smile
LET Divisor = 0
INPUT Divisor

LET Safe = FALSE AND (1 / Divisor = 0)
PRINT {Safe}
```

The division is not evaluated because `FALSE AND ...` short-circuits.

Output:

```text
FALSE
```

With a runtime-dependent left operand:

```smile
LET Check = FALSE
INPUT Check

LET Result = Check = TRUE AND (1 / 0 = 0)
PRINT {Result}
```

The right operand is evaluated only when `Check = TRUE`.

A compile-time validator must not report an evaluation failure for a path that is not definitely executed.

---

# 26. Runtime error output

A SMILE runtime error:

1. preserves all stdout already produced;
2. writes exactly one canonical error line to stderr;
3. appends one line ending to that stderr line;
4. terminates with exit code `1`;
5. performs no later SMILE statement.

Successful completion uses exit code `0`.

Interactive terminal echo is controlled by the host terminal and is not part of SMILE stdout.

---

# 27. INPUT runtime error codes

## SMILER1501 — End of input

```text
SMILE Runtime Error SMILER1501: Input ended before a value was received for '<Variable>'.
```

## SMILER1502 — Input line too long

```text
SMILE Runtime Error SMILER1502: Input for '<Variable>' exceeds the 4096-byte UTF-8 limit.
```

## SMILER1503 — Invalid Integer text

```text
SMILE Runtime Error SMILER1503: Input for '<Variable>' is not a valid Integer.
```

## SMILER1504 — Integer outside signed 64-bit range

```text
SMILE Runtime Error SMILER1504: Input for '<Variable>' is outside the signed 64-bit Integer range.
```

## SMILER1505 — Invalid Boolean text

```text
SMILE Runtime Error SMILER1505: Input for '<Variable>' must be TRUE or FALSE.
```

## SMILER1506 — Input decoding or read failure

```text
SMILE Runtime Error SMILER1506: Input for '<Variable>' could not be read as valid UTF-8 text.
```

The variable name uses the spelling from its declaration.

---

# 28. Runtime arithmetic error codes

## SMILER1206 — Runtime Integer overflow

```text
SMILE Runtime Error SMILER1206: Integer arithmetic overflow.
```

## SMILER1207 — Runtime division by zero

```text
SMILE Runtime Error SMILER1207: Division by zero.
```

These codes are the runtime counterparts of compile-time diagnostics `SMILE1206` and `SMILE1207`.

---

# 29. INPUT compile-time diagnostics

| Code | Meaning |
|---|---|
| `SMILE1501` | `INPUT` must be followed by whitespace |
| `SMILE1502` | `INPUT` requires a target variable |
| `SMILE1503` | `INPUT` target must be one identifier |
| `SMILE1504` | Unexpected content follows the INPUT target |
| `SMILE1505` | INPUT target variable is undefined |

Examples:

```smile
INPUTAge
```

is an ordinary unknown statement or identifier, not an `INPUT` statement.

```smile
INPUT"Age"
```

produces the missing-whitespace diagnostic.

```smile
INPUT
```

produces the missing-target diagnostic.

```smile
INPUT Age Extra
```

produces the unexpected-content diagnostic.

---

# 30. Evaluator contract

The SMILE reference evaluator must receive input through an injectable line source.

It must not read the process console directly during ordinary tests.

The evaluator must support:

- no-input evaluation for programs that do not execute `INPUT`;
- scripted input for deterministic tests;
- exact stdout;
- exact stderr;
- exit code;
- runtime-error identity.

A runtime error is not a compile diagnostic.

---

# 31. Generated target requirements

Every destination must:

- read from standard input;
- preserve one-line consumption order;
- implement the exact type rules;
- enforce the 4096-byte limit;
- preserve Unicode and String NUL semantics;
- use checked signed 64-bit runtime arithmetic;
- write canonical runtime errors to stderr;
- return the canonical exit code;
- preserve comments and blank lines around `INPUT`;
- avoid baking a test input value into generated source.

The ten targets remain:

- C#;
- C;
- Windows x64 MASM;
- JavaScript;
- Java;
- COBOL;
- Objective-C;
- Swift;
- Python;
- C++.

---

# 32. Scripted and interactive execution

Automated conformance tests supply scripted standard input.

Normal command-line execution must allow the generated program to interact with the invoking terminal.

The SMILE Desktop application must not run an input-requiring program as an invisible process with stdin immediately closed.

It must provide an interactive console execution path so the learner can:

1. see PRINT prompts as they occur;
2. enter each requested line;
3. see normal output and runtime errors;
4. observe the final exit behavior.

The exact Desktop presentation is an implementation concern, but interactive behavior is required.

---

# 33. No built-in prompt syntax in v1.0

This is intentionally not supported:

```smile
INPUT "Enter your name: ", Name
```

Use `PRINT` followed by `INPUT`:

```smile
PRINT Enter your name:
INPUT Name
```

Keeping prompting separate makes each statement perform one clear task.

A future specification may add non-newline output or prompt syntax without invalidating v1.0.

---

# 34. No automatic retry in v1.0

Invalid Integer or Boolean input does not automatically ask again.

The program reports the canonical runtime error and terminates.

Automatic retry would hide a loop inside `INPUT` before SMILE formally introduces loop syntax.

A future error-handling or validated-input feature may provide retry behavior explicitly.

---

# 35. Source layout and target comments

`INPUT` participates in the ordered source-item model.

Example:

```smile
LET Age = 0

REM Ask for the age.
INPUT Age

PRINT {Age}
```

The generated target retains:

- the blank line before the comment;
- the target-native comment;
- the `INPUT` operation;
- the blank line before PRINT.

Runtime behavior remains unaffected by layout.

---

# 36. Security and privacy

Standard input may contain sensitive information.

SMILE does not automatically echo, log, or copy entered values into generated source.

A program may explicitly print or store an entered value; that behavior belongs to the program.

Compiler diagnostics and runtime error messages include the target variable name but never include the rejected input text.

---

# 37. Normative valid program

```smile
REM SMILE v0.7.0 INPUT acceptance program

LET Name = ""
LET Age = 0
LET Ready = FALSE

PRINT Enter your name:
INPUT Name

PRINT Enter your age:
INPUT Age

PRINT Enter TRUE or FALSE:
INPUT Ready

PRINT Name=[{Name}]

IF Age >= 18 THEN
    PRINT Age group=Adult
ELSE
    PRINT Age group=Minor
END IF

IF Ready = TRUE THEN
    PRINT Ready=TRUE
ELSE
    PRINT Ready=FALSE
END IF
```

Scripted input:

```text
  Sin  
49
TrUe
```

Required stdout:

```text
Enter your name:
Enter your age:
Enter TRUE or FALSE:
Name=[  Sin  ]
Age group=Adult
Ready=TRUE
```

Required stderr is empty.

Required exit code:

```text
0
```

All ten targets must match the reference evaluator.

---

# 38. Normative invalid Integer run

Program:

```smile
LET Age = 0
PRINT Before
INPUT Age
PRINT After
```

Input:

```text
hello
```

Required stdout:

```text
Before
```

Required stderr:

```text
SMILE Runtime Error SMILER1503: Input for 'Age' is not a valid Integer.
```

Required exit code:

```text
1
```

`After` is not printed.

---

# 39. Normative EOF run

Program:

```smile
LET Name = ""
INPUT Name
```

Input reaches EOF without a line.

Required stdout is empty.

Required stderr:

```text
SMILE Runtime Error SMILER1501: Input ended before a value was received for 'Name'.
```

Required exit code:

```text
1
```

---

# 40. Backward compatibility

`INPUT` is additive except that `INPUT` becomes a reserved keyword.

Existing valid programs that do not use `INPUT` retain the same runtime behavior.

The introduction of runtime-unknown values must not weaken:

- existing compile-time type checking;
- source-known overflow diagnostics;
- source-known division-by-zero diagnostics;
- short-circuit semantics;
- exact String and NUL behavior;
- IF branch preservation;
- comment preservation;
- blank-line preservation;
- deterministic target generation.

---

# 41. Future compatibility

WHILE loops and future functions, scopes, and error handling must preserve these v1.0 rules unless an explicit later specification supersedes them:

- INPUT reads one line;
- the target must already exist;
- the target type is fixed;
- String input preserves its line content;
- Integer input is signed 64-bit decimal;
- Boolean input accepts only TRUE or FALSE;
- invalid input is not silently converted;
- INPUT is a statement, not an expression;
- runtime values remain Unknown to static propagation;
- runtime arithmetic remains checked;
- only executed paths consume input.
