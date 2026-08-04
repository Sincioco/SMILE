# SMILE Target Code Generation Standard v1.0

This document is the public standard for generated target-language code in SMILE.

## Core Rule

Generated target code should be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write.

## Priority Order

1. Preserve SMILE behavior exactly.
2. Preserve SMILE expression intent when the destination language has a natural equivalent.
3. Prefer conventional destination-language constructs, APIs, formatting, and program structure.
4. Keep generated code simple, deterministic, readable, dependency-light, and educational.
5. Use target-local lowering only when the destination language lacks a close native equivalent.

## Expression Intent

SMILE keeps one canonical language-neutral bound representation for each expression feature before target generation. String concatenation is the typed binary `+` operator rather than a target-specific or compatibility node. Targets must use that representation instead of reparsing source text or inventing parallel expression semantics.

One shared pass may simplify pure bound expressions before any target generator runs. It applies these Boolean identities recursively:

```text
NOT FALSE    -> TRUE
NOT TRUE     -> FALSE
x AND TRUE   -> x
TRUE AND x   -> x
x AND FALSE  -> FALSE
FALSE AND x  -> FALSE
x OR FALSE   -> x
FALSE OR x   -> x
x OR TRUE    -> TRUE
TRUE OR x    -> TRUE
```

The pass is target-independent and safe because current bound expressions are pure. Target generators consume the same simplified program.

Interpolation-oriented SMILE expressions should remain interpolation in languages where that is natural:

```smile
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
```

Examples:

```csharp
Console.WriteLine($"Hello {Name}!");
```

```javascript
console.log(`Hello ${Name}!`);
```

```swift
print("Hello \(Name)!")
```

```python
print(f"Hello {Name}!")
```

```cpp
std::cout << "Hello " << Name << "!" << '\n';
```

Explicit SMILE concatenation should remain explicit concatenation where that is natural:

```smile
PRINT "Hello " + Name + "!"
```

Examples:

```csharp
Console.WriteLine("Hello " + Name + "!");
```

```javascript
console.log("Hello " + Name + "!");
```

Typed SMILE values must display exactly like the reference evaluator. `Integer` values display as invariant decimal text. `Boolean` values display as `TRUE` or `FALSE`, even when the destination language's native boolean text would be lowercase.

Native expression intent must not make a valid SMILE program invalid in the destination compiler. After binding validates both operands, the shared simplifier uses previously declared bound constants in every expression position. It simplifies and evaluates the left operand first, decides whether the right operand is reachable, and never simplifies or evaluates an unreachable right subtree. Target generators must consume that shared result rather than duplicate short-circuit policy.

The v0.4.2.1 simplifier may use known constant values because current SMILE has no input, reassignment, functions, or side effects. When runtime values or side effects are added, optimization must preserve left-to-right evaluation and may fold only expressions proven safe.

## Semantic Integer And Target Storage

SMILE `Integer` is always a signed 64-bit semantic type. The parser, binder, checked constant evaluator, diagnostics, and reference evaluator enforce that definition regardless of how a destination stores a particular program.

Generated storage must use the most idiomatic natural Integer representation that preserves the complete simplified bound program. One per-program profile examines every remaining Integer literal, declaration value, operand, and evaluated intermediate:

- C and Objective-C use `int` when all observed values fit signed 32-bit; otherwise they use `int64_t`, `<stdint.h>`, `INT64_C(...)`, and `INT64_MIN` as needed.
- C++ uses `int` when all observed values fit signed 32-bit; otherwise it uses `std::int64_t`, `<cstdint>`, `INT64_C(...)`, and `INT64_MIN` as needed.
- C# uses `int` when signed 32-bit is sufficient; otherwise it uses `long` consistently.
- Java uses `int` when signed 32-bit is sufficient; otherwise it uses `long` consistently.
- JavaScript uses `Number` while every observed value is within `-9007199254740991` through `9007199254740991`; otherwise every Integer literal and operation uses `BigInt` consistently.
- Swift uses `Int` for the ordinary safe profile and `Int64` only when the program requires explicit signed 64-bit storage.
- Python uses normal `int`.

An ordinary target Integer profile must not carry wide suffixes such as `L`, `LL`, or `n`:

```c
int Age = 49;
bool Adult = Age >= 18;
bool WorkingAge = Adult;
```

```csharp
int Age = 49;
```

```java
int Age = 49;
```

```javascript
let Age = 49;
```

```cpp
int Age = 49;
bool Adult = Age >= 18;
bool WorkingAge = Adult;
```

Wide profiles must be exact and consistent. For example, a C program that requires a value above signed 32-bit uses `int64_t Age = INT64_C(2147483648);`, while a JavaScript program that exceeds the safe Integer range uses `2147483648n` for otherwise small literals in that same program as well. JavaScript `Number` division must use `Math.trunc(left / right)` to preserve SMILE's truncation-toward-zero quotient; `BigInt` division already has the required behavior.

## Python

Python generation produces one dependency-free `Program.py` compatible with Python 3.10 or newer. It uses a conventional `main()` function and `if __name__ == "__main__":` guard. SMILE `String`, `Integer`, and `Boolean` values map to Python `str`, `int`, and `bool` values.

Python interpolation remains interpolation through f-strings. Literal braces in f-string text are doubled, official backslash and control-character escapes are preserved, and String concatenation remains `+`.

Python's normal Boolean text is not SMILE's canonical display text, so a generated helper is used only when a non-String value becomes text:

```python
def _smile_text(value: object) -> str:
    if isinstance(value, bool):
        return "TRUE" if value else "FALSE"

    return str(value)
```

Python `/` produces floating point and Python `//` floors negative results, while SMILE Integer division truncates toward zero. Python therefore emits this helper only when the bound program contains Integer division:

```python
def _smile_div(left: int, right: int) -> int:
    quotient = abs(left) // abs(right)
    return -quotient if (left < 0) != (right < 0) else quotient
```

The Python expression writer renders the bound tree with Python-aware precedence. `NOT`, `AND`, and `OR` become `not`, `and`, and `or`; nested comparisons are parenthesized so a SMILE equality tree never turns into Python chained-comparison semantics.

## C++

C++ generation produces one dependency-free `Program.cpp` compiled as C++20. It is a dedicated bound-tree backend, not C output with another filename. SMILE `String`, `Integer`, and `Boolean` map to `std::string`, the selected `int`/`std::int64_t` profile, and `bool`.

Strings are RAII-owned values. Concatenation preserves the bound expression, but when a chain would begin with two literals the first operand becomes an owned value:

```cpp
std::string Text = std::string{"A"} + "B";
```

Interpolation builds `std::string` with `std::to_string` for Integers and a conditional expression for canonical Boolean text. Direct `PRINT` uses `std::cout` and `'\n'`; it does not use `printf`, `std::endl`, or globally enable `std::boolalpha`.

`std::string` equality and inequality are native value comparisons and therefore remain ordinal, case-sensitive, and length-aware. A literal containing embedded NUL must use its exact UTF-8 byte length:

```cpp
std::string Text = std::string{"A\000B", 3};
std::cout << Text << '\n';
```

Generators emit only required headers, never `using namespace std;`, and never fall back to C-style raw `char *`, `printf`, or `strcmp` for ordinary SMILE Strings.

## Lower-Level Targets

Lower-level targets may need target-local lowering, but the emitted code should still look natural for that target.

Current SMILE `LET` initializers are compile-time constants. COBOL, MASM, and other lower-level targets may emit evaluated declaration values instead of building strings or expression runtimes. C and Objective-C preserve native integer and boolean expression intent while string declarations may use their evaluated value:

```c
const char *FullName = "Sin Cioco";
int Age = 49;
bool Adult = Age >= 18;
```

This keeps early generated programs dependency-light and avoids introducing premature buffers or a SMILE runtime library.

For current NUL-free C `PRINT`, SMILE emits one safe typed `printf` statement per SMILE `PRINT`:

```c
printf("\n");
printf("Hello World!\n");
printf("Hello %s!\n", Name);
printf("Age=%d, Adult=%s\n", Age, Adult ? "TRUE" : "FALSE");
```

The generated `printf` format string must always be compiler-generated. Literal percent signs from SMILE source must be escaped in the generated format string:

```c
printf("Progress: 100%%\n");
printf("Sin is 100%% ready.\n");
```

String variables and expressions must be passed as arguments and never as the format string:

```c
printf("%s\n", Name);
```

Ordinary `int` expressions use `%d`. Wide `int64_t` expressions use `%lld` with an explicit value-preserving cast to `long long`, because the exact underlying typedef of `int64_t` is platform-specific. Boolean expressions use `%s` with an argument that selects canonical `TRUE` or `FALSE`. Literal percent signs are doubled in the compiler-generated format.

SMILE Strings are complete values with an exact length. C-family `%s` and `strcmp` may be used only when semantically valid for the complete value. For a NUL-containing `PRINT`, generated C and Objective-C use a small nested scope with compiler-owned UTF-8 byte data, an exact byte length, `fwrite`, and `fputc` for the newline. NUL-free output remains on readable `printf` calls. String equality and inequality use ordinal, case-sensitive `strcmp(...) == 0` and `strcmp(...) != 0` only when both operands contain no NUL; `<string.h>` is included only when such a generated expression needs it. Simple NUL-free variables and literals remain natural `strcmp` operands, and a complex NUL-free concatenation or interpolation may use its already evaluated String literal. If either operand contains NUL, the current pure comparison is lowered to its exact evaluated Boolean so bytes after the NUL remain significant. This is intentional target-local lowering, not a general String runtime.

Objective-C follows the same stdout style in the Windows-local console profile:

```objc
const char *Name = "Sin";
printf("Hello %s!\n", Name);
```

This profile intentionally avoids Foundation/NSString until the local Windows runtime path is hardened. The file is still generated as Objective-C (`.m`) and compiled with an Objective-C compiler.

COBOL uses GnuCOBOL free-format source and stores `LET` values in `WORKING-STORAGE`:

```cobol
01 Name PIC X(3) VALUE "Sin".
01 Age PIC X(2) VALUE "49".
DISPLAY "Hello Sin!".
```

COBOL fixed-length data items must not leak padding into SMILE output. Empty SMILE strings may use a one-character placeholder for storage, but generated `DISPLAY` output should use canonical text when the compile-time SMILE value is known:

```cobol
01 Empty PIC X VALUE SPACE.
DISPLAY "[]".
```

Blank SMILE `PRINT` must emit an empty line, not a line containing a single space:

```cobol
DISPLAY X"0A" WITH NO ADVANCING.
```

MASM stays dependency-light and uses explicit output operations, but the generated assembly should keep explanatory right-side comments so learners can follow the code.

For MASM empty strings, storage may use one placeholder byte so a label has an address:

```asm
variable0Value BYTE 0
```

The logical string length must still be zero:

```asm
variable0ValueLength EQU 0
```

This prevents `WriteFile` from emitting a hidden NUL byte before the normal `PRINT` newline.

## Identifier Spelling

SMILE preserves user-authored identifier spelling in generated target code. A SMILE declaration such as:

```smile
LET Name = "Sin"
```

should keep `Name` in target languages when that spelling is safe.

Target generators must use the compiler's symbol-based target identifier map. A valid SMILE name must be mapped when it conflicts with:

- destination-language keywords;
- destination-language contextual or restricted identifiers;
- destination-language identifier rules;
- generator-owned runtime names such as `Console`, `Program`, `Main`, `printf`, `System`, `String`, `main`, `args`, `console`, or `print`;
- destination-language reserved identifier patterns, such as C, Objective-C, and C++ names beginning with `__` or with `_` followed by an uppercase ASCII letter;
- another generated target name.

COBOL must map reserved words and identifiers that are not valid COBOL data names. Underscores should become readable hyphenated names such as `SMILE-internal`.

Java and Swift must map a single SMILE `_` identifier because `_` is not a usable ordinary local variable spelling in those targets.

Python must map keywords, the `match` and `case` soft keywords, relevant built-ins such as `str`, `bool`, `int`, `abs`, and `isinstance`, and generator-owned names including `main`, `__name__`, `_smile_text`, and `_smile_div`. A single `_` remains a valid Python identifier.

C++ must map all C++20 keywords and alternative tokens, generator-used names such as `std`, `main`, `cout`, `string`, `to_string`, and `int64_t`, and C/C++ implementation-reserved prefix patterns.

Mapped names should remain readable. For example:

```smile
LET class = "A"
LET _smile_class = "B"
```

may generate:

```csharp
string _smile_class = "A";
string _smile_class_2 = "B";
```

Every reference to a SMILE symbol must use the same mapped target name as its declaration. Generators must not perform source-text replacement to accomplish this.

## Conformance Validation

Every expression feature is validated against the `SmileEvaluator` reference oracle. The generated-target suite includes a fixed seed (`20260401`) and fixed corpus hash, small and wide Integer profile programs, signed 32-bit and signed 64-bit boundaries, intermediate-result promotion, NUL copies/concatenation/interpolation/equality, and known-variable short circuits in every expression position. It runs all locally installed toolchains for all ten targets and compares exact UTF-8 stdout bytes after normalizing only CRLF to LF in tests that explicitly permit platform line-ending normalization. Tests never trim or discard NUL, backspace, form feed, carriage return, or tab. This makes generated output deterministic while keeping semantic drift visible.

## Destination-Language Freeze

C++ is SMILE's tenth and final planned destination language. After C++ is complete, target-language expansion is frozen unless Sin explicitly reopens it. New compiler work should deepen SMILE's runtime variables, assignment, conditions, input, loops, functions, scopes, debugging, and teaching tools rather than adding another backend. Rust, Zig, and Go remain deferred.
