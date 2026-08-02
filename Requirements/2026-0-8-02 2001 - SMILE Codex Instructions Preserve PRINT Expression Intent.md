# Codex Implementation Instructions — Preserve SMILE Expression Intent in Generated Target Code

## Objective

Update the SMILE compiler/transpiler so that generated target-language code preserves the programmer's original expression intent whenever the target language has a clear and idiomatic equivalent.

The current implementation correctly preserves runtime behavior, but it normalizes interpolation, friendly raw interpolation, and explicit concatenation into the same flattened sequence of string and variable segments.

For example, all of these SMILE statements currently generate C# concatenation:

```smile
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
```

Current generated C#:

```csharp
Console.WriteLine("Hello " + Name + "!");
```

This loses the distinction between interpolation and explicit concatenation.

The corrected implementation must preserve that distinction.

---

## Required language-design rule

Add and follow this compiler rule:

> SMILE target generators SHOULD preserve the source expression form when the target language provides a clear and idiomatic equivalent. When no close native equivalent exists, the generator MUST use a semantically equivalent fallback.

This means:

- Explicit interpolation should generate native interpolation where supported.
- Friendly raw interpolation should generate native interpolation where supported.
- Explicit concatenation should remain concatenation where supported.
- Plain literals should remain plain literals.
- Targets without a close native syntax may lower the expression to concatenation or output segments.
- Runtime output must remain identical across all targets.

---

## Required behavior

Given:

```smile
LET Name = "Sin"
```

### 1. Explicit interpolated string

SMILE:

```smile
PRINT $"Hello {Name}!"
```

Expected C#:

```csharp
Console.WriteLine($"Hello {Name}!");
```

Expected JavaScript:

```javascript
console.log(`Hello ${Name}!`);
```

Expected Swift:

```swift
print("Hello \(Name)!")
```

Expected Java fallback:

```java
System.out.println("Hello " + Name + "!");
```

C, Objective-C, and MASM may continue lowering this to literal and variable output segments.

---

### 2. Friendly raw interpolation

SMILE:

```smile
PRINT Hello {Name}!
```

Expected C#:

```csharp
Console.WriteLine($"Hello {Name}!");
```

Expected JavaScript:

```javascript
console.log(`Hello ${Name}!`);
```

Expected Swift:

```swift
print("Hello \(Name)!")
```

Expected Java fallback:

```java
System.out.println("Hello " + Name + "!");
```

The friendly syntax is semantically interpolation and should therefore use the closest target-language interpolation syntax where available.

---

### 3. Explicit concatenation

SMILE:

```smile
PRINT "Hello " + Name + "!"
```

Expected C#:

```csharp
Console.WriteLine("Hello " + Name + "!");
```

Expected JavaScript:

```javascript
console.log("Hello " + Name + "!");
```

Expected Java:

```java
System.out.println("Hello " + Name + "!");
```

Expected Swift should preserve concatenation if valid for the current supported SMILE string types:

```swift
print("Hello " + Name + "!")
```

Do not rewrite explicit concatenation into interpolation.

---

### 4. Plain string literal

SMILE:

```smile
PRINT "Hello World!"
```

Expected C#:

```csharp
Console.WriteLine("Hello World!");
```

Equivalent plain literal syntax should be used in the other targets.

---

### 5. Blank PRINT

SMILE:

```smile
PRINT
```

Preferred C# output:

```csharp
Console.WriteLine();
```

This is more idiomatic than:

```csharp
Console.WriteLine("");
```

Apply similarly idiomatic blank-line output in other targets where practical, without changing behavior.

---

## Architectural requirement

Do not implement this as a C#-only string-rewriting heuristic.

The current design flattens bound expressions too early through logic equivalent to:

```csharp
BoundStringExpression.Flatten(expression)
```

and later joins the resulting segments with:

```csharp
" + "
```

That destroys source-form intent before target generation.

Refactor the syntax and/or bound representation so expression form survives until the target generator runs.

The bound model should distinguish at least:

```text
BoundStringLiteralExpression
BoundVariableExpression
BoundInterpolatedStringExpression
BoundConcatenationExpression
BoundRawInterpolatedTextExpression
```

Exact names may differ, but the semantic distinction must remain available to every code generator.

A possible design is:

```csharp
internal abstract record BoundExpression;

internal sealed record BoundStringLiteralExpression(
    string Value) : BoundExpression;

internal sealed record BoundVariableExpression(
    VariableSymbol Variable) : BoundExpression;

internal sealed record BoundConcatenationExpression(
    BoundExpression Left,
    BoundExpression Right) : BoundExpression;

internal sealed record BoundInterpolatedStringExpression(
    IReadOnlyList<BoundInterpolatedPart> Parts) : BoundExpression;

internal abstract record BoundInterpolatedPart;

internal sealed record BoundInterpolatedTextPart(
    string Text) : BoundInterpolatedPart;

internal sealed record BoundInterpolationPart(
    VariableSymbol Variable) : BoundInterpolatedPart;
```

Friendly raw interpolation may either use its own bound node or bind directly to `BoundInterpolatedStringExpression`, provided explicit concatenation remains distinguishable.

---

## Parsing and binding requirements

The parser and binder must retain the difference between:

```smile
PRINT Hello {Name}!
```

```smile
PRINT $"Hello {Name}!"
```

```smile
PRINT "Hello " + Name + "!"
```

The first two may bind to the same interpolation-oriented bound node because they share intent.

The third must bind to a concatenation node.

Do not reconstruct intent later by analyzing flattened segments.

---

## Target generator rules

### C#

Use:

```csharp
$"...{Name}..."
```

for interpolation-oriented bound expressions.

Use:

```csharp
"..." + Name + "..."
```

for explicit concatenation.

Use:

```csharp
Console.WriteLine();
```

for blank `PRINT`.

Correctly escape:

- `"`
- `\`
- control characters
- literal `{`
- literal `}`

Within C# interpolated strings, literal braces must be doubled.

Example SMILE:

```smile
PRINT $"Literal braces: {{Name}}"
```

Expected output:

```csharp
Console.WriteLine($"Literal braces: {{Name}}");
```

This prints:

```text
Literal braces: {Name}
```

---

### JavaScript

Use template literals for interpolation-oriented expressions:

```javascript
console.log(`Hello ${Name}!`);
```

Preserve explicit concatenation:

```javascript
console.log("Hello " + Name + "!");
```

Correctly escape:

- backticks
- backslashes
- `${` when literal
- control characters

---

### Swift

Use native interpolation for interpolation-oriented expressions:

```swift
print("Hello \(Name)!")
```

Preserve explicit concatenation where valid:

```swift
print("Hello " + Name + "!")
```

Correctly escape:

- quotation marks
- backslashes
- literal interpolation markers

---

### Java

Java does not provide direct built-in string interpolation equivalent to C#.

Use concatenation as the fallback for interpolation-oriented expressions:

```java
System.out.println("Hello " + Name + "!");
```

Explicit concatenation should remain concatenation.

---

### C and Objective-C

These targets may continue emitting expression segments using `fputs`, `puts`, `putchar`, or equivalent output calls.

The implementation must still consume the preserved bound expression tree rather than depending exclusively on a globally flattened representation.

A target-local lowering helper may flatten an expression when needed.

---

### MASM x64

MASM may continue lowering expressions to literal and variable output segments.

The target-local generator may flatten the preserved expression tree into output segments.

Runtime output and existing educational comments must remain correct.

---

## Refactoring guidance

Replace the current global behavior where multiple high-level expression forms are converted into the same segment list before target generation.

A useful pattern is:

```csharp
TargetExpression.CSharp(expression)
TargetExpression.JavaScript(expression)
TargetExpression.Java(expression)
TargetExpression.Swift(expression)
```

Each target-specific method should inspect the actual bound node kind.

For lower-level targets, provide a helper such as:

```csharp
BoundStringExpression.FlattenForOutput(expression)
```

This helper should be used only by targets that require segment-based output.

Do not make `Flatten` the canonical semantic representation.

---

## Required tests

Update or replace the existing test that expects interpolation and concatenation to generate identical C# code.

Create separate tests for each expression form.

### C# explicit interpolation

```csharp
[TestMethod]
public void Csharp_generator_preserves_explicit_interpolation()
{
    const string source = """
LET Name = "Sin"
PRINT $"Hello {Name}!"
""";

    GeneratedProgram program = Generate(source, TargetLanguage.CSharp);

    StringAssert.Contains(
        program.PrimaryFile.Content,
        """Console.WriteLine($"Hello {Name}!");""");
}
```

### C# friendly interpolation

```csharp
[TestMethod]
public void Csharp_generator_uses_interpolation_for_friendly_raw_placeholders()
{
    const string source = """
LET Name = "Sin"
PRINT Hello {Name}!
""";

    GeneratedProgram program = Generate(source, TargetLanguage.CSharp);

    StringAssert.Contains(
        program.PrimaryFile.Content,
        """Console.WriteLine($"Hello {Name}!");""");
}
```

### C# explicit concatenation

```csharp
[TestMethod]
public void Csharp_generator_preserves_explicit_concatenation()
{
    const string source = """
LET Name = "Sin"
PRINT "Hello " + Name + "!"
""";

    GeneratedProgram program = Generate(source, TargetLanguage.CSharp);

    StringAssert.Contains(
        program.PrimaryFile.Content,
        """Console.WriteLine("Hello " + Name + "!");""");
}
```

### JavaScript interpolation

Verify:

```javascript
console.log(`Hello ${Name}!`);
```

for both:

```smile
PRINT Hello {Name}!
```

and:

```smile
PRINT $"Hello {Name}!"
```

### JavaScript explicit concatenation

Verify:

```javascript
console.log("Hello " + Name + "!");
```

for:

```smile
PRINT "Hello " + Name + "!"
```

### Swift interpolation

Verify:

```swift
print("Hello \(Name)!")
```

for interpolation-oriented SMILE syntax.

### Java fallback

Verify interpolation-oriented syntax becomes:

```java
System.out.println("Hello " + Name + "!");
```

### Literal braces

Test at least:

```smile
LET Name = "Sin"
PRINT Literal braces: {{Name}}
PRINT $"Literal braces: {{Name}}"
```

Both must print:

```text
Literal braces: {Name}
```

Generated target code must contain the correct target-specific escaping.

### Multiple interpolations

Test:

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"
PRINT $"{FirstName} {LastName}"
```

Expected C#:

```csharp
Console.WriteLine($"{FirstName} {LastName}");
```

Expected JavaScript:

```javascript
console.log(`${FirstName} ${LastName}`);
```

Expected Swift:

```swift
print("\(FirstName) \(LastName)")
```

### Adjacent placeholders

Test:

```smile
LET A = "A"
LET B = "B"
PRINT $"{A}{B}"
```

Expected runtime output:

```text
AB
```

### Empty interpolation text around a variable

Test:

```smile
LET Name = "Sin"
PRINT $"{Name}"
```

Expected C#:

```csharp
Console.WriteLine($"{Name}");
```

Do not unnecessarily rewrite it to:

```csharp
Console.WriteLine(Name);
```

The source explicitly requested interpolation, so preserve that intent.

### Blank PRINT

Verify C# generates:

```csharp
Console.WriteLine();
```

and still prints exactly one newline.

---

## Regression requirements

All existing valid SMILE programs must continue producing the same runtime output.

Do not break:

- case-insensitive variable lookup
- duplicate variable diagnostics
- undefined-variable diagnostics
- literal brace escaping
- quoted literals
- raw text
- blank `PRINT`
- one statement per line
- all seven current target generators
- CLI generation
- desktop live transpilation
- Build & Run
- generated project/toolchain behavior

---

## Documentation requirements

Update the official PRINT specification to state that target generators preserve source expression intent when an idiomatic equivalent exists.

Suggested normative wording:

> Target generators SHOULD preserve the programmer's expression form when the destination language provides a clear and idiomatic equivalent. Interpolated SMILE expressions SHOULD generate native interpolation syntax where supported. Explicit concatenation SHOULD remain concatenation. If the destination language lacks a close equivalent, the generator MUST emit semantically equivalent code.

Also document that:

```smile
PRINT Hello {Name}!
```

is interpolation-oriented friendly syntax.

And:

```smile
PRINT "Hello " + Name + "!"
```

is explicit concatenation.

---

## Acceptance criteria

The task is complete only when all of the following are true:

1. C# explicit interpolation generates C# interpolation.
2. C# friendly raw interpolation generates C# interpolation.
3. C# explicit concatenation remains concatenation.
4. JavaScript interpolation generates template literals.
5. JavaScript explicit concatenation remains concatenation.
6. Swift interpolation generates native Swift interpolation.
7. Java uses concatenation as the interpolation fallback.
8. C, Objective-C, and MASM continue producing identical runtime output.
9. Literal braces are correctly escaped in every affected target.
10. Blank C# `PRINT` uses `Console.WriteLine();`.
11. Tests distinguish interpolation from concatenation.
12. Existing tests and new tests pass in Debug and Release.
13. Generated programs compile and run for every currently runnable target.
14. Documentation is updated.
15. No source-form reconstruction heuristics are used after flattening.
16. Expression intent survives through parsing, binding, and target generation.

---

## Validation commands

Run at minimum:

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

Also run CLI generation for all targets and compile/run every target currently supported by the existing validation workflow.

Use a sample containing:

```smile
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
PRINT Literal braces: {{Name}}
```

Confirm that all targets produce identical runtime output while generated source code preserves the closest matching syntax available in each target language.

---

## Commit guidance

Use a focused commit message such as:

```text
Sin and Codex: Preserve PRINT expression intent across targets
```

The commit description should mention:

- preserved interpolation versus concatenation
- bound-expression refactoring
- target-specific native interpolation
- fallback behavior for languages without interpolation
- escaping updates
- test coverage
- documentation updates
- Debug and Release validation results
