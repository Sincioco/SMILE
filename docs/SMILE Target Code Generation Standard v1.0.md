# SMILE Target Code Generation Standard v1.0

This document is the public standard for generated target-language code in SMILE.

## Core Rule

Generated target code should be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write.

When a direct variable read has a clear target representation, the generator must read the target variable's current storage. It must not replace that read with an unrelated compiler-time literal merely because the current straight-line value is known.

## Priority Order

1. Preserve SMILE behavior exactly.
2. Preserve SMILE expression intent when the destination language has a natural equivalent.
3. Prefer conventional destination-language constructs, APIs, formatting, and program structure.
4. Keep generated code simple, deterministic, readable, dependency-light, and educational.
5. Use target-local lowering only when the destination language lacks a close native equivalent.

## Expression Intent

SMILE keeps one canonical language-neutral bound representation for each expression feature before target generation. String concatenation is the typed binary `+` operator rather than a target-specific or compatibility node. Targets must use that representation instead of reparsing source text or inventing parallel expression semantics.

One shared pass may simplify pure bound expressions before any target generator runs. It uses the known environment at each statement position and applies these Boolean identities recursively:

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

The pass is target-independent and safe because expressions remain pure. For SET, it simplifies and evaluates the right side using the old environment and changes the known value only after the complete assignment succeeds. For IF, every branch starts from the same incoming environment and outgoing facts merge to Known only when every possible path proves the same value. A known condition may guide analysis but must never delete a source clause or body. Target generators consume the same branch-aware result.

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

Native expression intent must not make a valid SMILE program invalid in the destination compiler. After binding validates both operands, the shared simplifier uses the current known values in every expression position. It simplifies and evaluates the left operand first, decides whether the right operand is reachable, and never simplifies or evaluates an unreachable right subtree. Target generators must consume that shared result rather than duplicate short-circuit policy.

SMILE v0.6.1 has conditional branches and non-semantic source layout but still has no input, loop, function, or external runtime data. Optimization must respect SET mutation, branch boundaries, atomic right-side evaluation, left-to-right short-circuiting, direct runtime-storage reads, and the exact relative order of comments and blank lines. Never reuse an old known value after SET, carry a value from one branch into another, propagate a post-IF value without a proven merge, replace genuine IF control flow with a currently selected branch, or move layout across a clause boundary.

## Semantic Integer And Target Storage

SMILE `Integer` is always a signed 64-bit semantic type. The parser, binder, checked constant evaluator, diagnostics, and reference evaluator enforce that definition regardless of how a destination stores a particular program.

Generated storage must use the most idiomatic natural Integer representation that preserves the complete bound program. One per-program profile examines every Integer literal, LET value, SET value, IF and ELSE IF condition, operand, intermediate, PRINT expression, interpolation hole, and nested branch, including branches not selected by current values:

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

## Runtime Variables, SET, And Block Strings

`LET` declares a variable and stores its initial value. `SET` changes the current value of an earlier declaration without changing its SMILE type. Every target generator iterates ordered bound source items, handling non-semantic comments and blank lines directly while retaining the existing `BoundLetStatement`, `BoundSetStatement`, `BoundPrintStatement`, and recursive `BoundIfStatement` behavior. High-level targets preserve natural assignment:

```csharp
int Counter = 0;
Counter = Counter + 1;
```

```cpp
std::string Name = "Sin";
Name = "Louiery";
```

```python
Ready = False
Ready = True
```

Low-level targets may lower a right side to the exact value proven by the shared branch-aware program analysis, but they must emit a real storage update at the SET statement. A generator must not omit SET merely because a later or final value is already known.

The front end completely normalizes a SET Block String Literal to an ordinary bound String value. Target generators never inspect delimiters, remove indentation, normalize physical newlines, decode block syntax, or choose behavior based on the original source form. They emit the normalized value using the clearest exact ordinary destination String syntax.

One shared recursive mutation analysis records every symbol targeted by SET anywhere in the program, including currently unselected IF branches. Swift declares those symbols with `var` and may retain `let` only for symbols never changed on any path.

A valid direct SMILE self-assignment must remain an explicit assignment in generated code. When a destination rejects or warns about `target = target`, its generator must emit the smallest type-preserving identity expression while retaining a real target assignment: append an empty String, add Integer zero, or OR Boolean false. C# uses this lowering to avoid `CS1717`, and Swift uses it because plain self-assignment is rejected. Detection is based only on a direct bound variable expression whose `VariableSymbol` is the same symbol as the SET target; equal current values, differently cased source spellings, or mapped target names must not substitute source-text comparison for symbol identity. Other targets use their natural mutable assignment forms unless their compiler proves that focused lowering is necessary.

## IF Control Flow

Every generator consumes the shared ordered `BoundConditionalClause` and `BoundIfStatement` tree. It must emit every source clause and body, even when current known values make one clause predictably selected. Generators must not inspect IF source text, flatten a nested IF with ad hoc searches, or substitute one compiler-time branch for genuine destination control flow.

The front end supports IF nesting depth 1 through 128. Attempting to enter depth 129 produces `SMILE1416`, so target generation is not attempted for that invalid program. This compiler resource limit does not alter the generated control flow of any valid program within the supported depth.

C#, C, C++, Java, JavaScript, Objective-C, and Swift use natural `if / else if / else` blocks. Python uses `if / elif / else` and emits `pass` for an empty body. COBOL emits warning-free free-format `IF / ELSE / END-IF`; a nested ELSE/IF shape is acceptable for multiple clauses when order and matching terminators remain clear. MASM x64 emits deterministic compare/jump control flow with stable labels for each clause and the shared end.

An IF condition reads current target storage whenever the destination can represent the bound expression clearly. Branch SET operations update real storage only when that branch executes. An IF without ELSE includes the unchanged incoming path for analysis. After IF, a target-local known value may be used only when every possible outgoing path proves the same value; branch-specific facts never leak into later code.

Integer profiling, String sizing, embedded-NUL planning, mutation analysis, required headers/helpers, and compiler-owned names inspect the complete recursive IF tree. A branch-assigned String contributes its maximum UTF-8 byte length and NUL possibility even when another branch is selected by current values. Deterministic output requires stable indentation, COBOL names, and MASM labels for nested and multi-clause programs.

## Generator Source Organization

`Generation.cs` is the small public transpilation facade. Shared generator helpers live under `src/SMILE.Engine/Generation/`, alongside one focused source file for each destination generator. This source split is organizational only: namespace, visibility, target registry order and IDs, public/internal APIs, generated filenames and project files, deterministic labels, and emitted bytes remain unchanged. Shared lowering and planning logic must stay shared rather than being copied into destination files; the split does not create a new generator framework, discovery mechanism, template system, or dependency.

## Source Comments And Blank-Line Layout

Comments and blank physical source lines are ordered non-semantic bound source items. Generators must emit them directly from that ordered tree, not reconstruct them from spans, offsets, original source text, or a side table. Semantic statements continue to use the existing analysis facts; generators must never request a statement ordinal, known value, mutation, range, or display fact for a layout item.

One shared target-comment emitter maps every SMILE source marker to the destination's native full-line marker:

| Destination | Generated marker |
|---|---|
| C#, C, C++, JavaScript, Java, Objective-C, Swift | `//` |
| Python | `#` |
| COBOL free source | `*>` |
| Windows x64 MASM | `;` |

The original marker kind does not leak into target source; the exact payload after it is preserved in order and safely rendered. NUL, U+2028, U+2029, and target-unsafe C0 controls other than permitted tab use readable reversible `\u{HEX}` text. C, C++, and Objective-C must not leave a literal backslash as the last non-horizontal-whitespace character before a physical line ending, because translation-phase splicing can otherwise consume the next line; trailing payload spaces and tabs remain present. Java must prevent source text such as `\u000A` from becoming a Unicode escape before ordinary tokenization. Printable Unicode remains literal when the current target toolchain accepts it. GnuCOBOL comments wrap deterministically only when needed, repeat `*>` and indentation on consecutive fragments, preserve payload order, and stay within a conservative 200-column cap. Width accounting includes UTF-8 byte length and conventional tab stops. At extreme IF depth, only generated comment indentation may be capped, at 40 spaces; payload text remains complete. No character may be silently dropped or invented.

Each blank source line outside a SET Block String emits one physically empty target line at the corresponding source-order boundary. Preserve isolated, consecutive, leading, trailing, and branch-local blank lines in addition to generator-owned formatting. A statement that lowers to several target lines remains one chunk, so a source blank line belongs between chunks. Layout does not need to force target line numbers to equal SMILE line numbers.

Comments and blank lines belong only in the primary generated program file. Python places them inside `main()` or the existing source-order user body, never before file-level boilerplate where a comment could become an encoding declaration or shebang. MASM places them in the `.code` source-order statement stream and never duplicates them into `.data`. COBOL emits every layout item exactly once in the closest deterministic `PROCEDURE DIVISION` user-code region; LET storage may remain in `WORKING-STORAGE`. Comment-only or blank-only target bodies are still semantically empty, so Python emits `pass` and COBOL emits `CONTINUE` wherever their grammar requires a statement.

SET Block String ownership is stronger than comment and blank-line recognition. Its marker-looking and blank content lines are normalized into the ordinary bound String value and must never be emitted separately as source layout. Because preserved comments are copied into generated primary files, programmers must not put passwords, private keys, tokens, or other secrets in source comments.

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

Generators emit only required headers, never `using namespace std;`, and never fall back to C-style raw `char *`, `printf`, or `strcmp` for ordinary SMILE Strings. C++ header emission is facility-driven: emit `<string>` only when generated code actually uses `std::string`, `std::to_string`, or another String-library facility. A directly streamed NUL-free literal or template does not require `<string>` merely because its SMILE expression type is `String`.

## Lower-Level Targets

Lower-level targets may need target-local lowering, but the emitted code should still look natural for that target.

The reference execution trace records the one branch selected by the current source program, while branch-aware analysis decides whether a value is safe to embed in generated code. COBOL, MASM, and other lower-level targets may emit an evaluated declaration, assignment, condition, or output value only when the corresponding branch-aware fact is `Known`. After an `Unknown` merge they must read runtime storage and lower the expression, even though the reference trace also contains today's selected concrete value. C and Objective-C preserve native Integer and Boolean expression intent; branch-aware Known String declarations and assignments may use exact values:

```c
const char *FullName = "Sin Cioco";
int Age = 49;
bool Adult = Age >= 18;
```

This keeps generated programs dependency-light without treating an initial LET value as permanent. Every later SET still emits an actual destination storage update. A branch-aware Known value may guide storage size, literal construction, and complex-expression lowering. A selected concrete trace value must never replace a runtime read or expression after analysis reports `Unknown`, including a later LET, SET, PRINT, interpolation hole, or IF condition.

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

SMILE Strings are complete values with an exact length. C-family `%s` and `strcmp` may be used only when semantically valid for the complete value. C and Objective-C keep mutable `const char *` pointers. A branch-aware Known String assignment may lower to one exact ordinary C literal followed by pointer assignment; a runtime-unknown direct assignment or later LET copies current source storage, while an Unknown composite expression materializes its current segments into a bounded deterministic buffer. Direct runtime copies preserve value semantics without later mutable-buffer aliasing. If a variable can contain NUL at any LET or SET position, a deterministic collision-safe `size_t smileString{variableIndex}Length` support variable accompanies it. Runtime copies and materialization synchronize the logical length, using `strlen` only when the complete source value is proved NUL-free. The pointer and logical length must always describe the same current String value.

A direct C or Objective-C String variable PRINT reads current target storage. When the variable has a logical-length companion, output uses the current pointer and current length, normally with `fwrite(variable, 1, variableLength, stdout)` followed by `fputc('\n', stdout)`. An ordinary NUL-free variable may continue to use `printf("%s\n", variable)`. Composite output writes runtime String segments with their current logical lengths when NUL is possible and may combine those writes with static literal segments. The PRINT operation must not use a second independent static copy of a variable whose branch-aware value is Unknown. Raw templates, interpolation, concatenation, and other complex expressions may use exact static lowering only when their complete value is branch-aware Known.

Applicable C-family String equality also reads current storage. NUL-free variable operands use ordinary ordinal `strcmp`. Exact byte-aware operands compare logical lengths before `memcmp`, so unequal lengths and prefix collisions cannot read past the shorter value or ignore bytes after NUL. Variable-versus-variable, variable-versus-literal, literal-versus-variable, and supported composite operands follow this rule. A complex expression may lower to a static Boolean only when branch-aware analysis proves that Boolean on every possible incoming path; the selected concrete trace is never authority after an Unknown merge.

Objective-C follows the same stdout style in the Windows-local console profile:

```objc
const char *Name = "Sin";
printf("Hello %s!\n", Name);
```

This profile intentionally avoids Foundation/NSString until the local Windows runtime path is hardened. The file is still generated as Objective-C (`.m`) and compiled with an Objective-C compiler.

COBOL uses GnuCOBOL free-format source and stores variables in `WORKING-STORAGE`:

```cobol
01 Name PIC X(3) VALUE "Sin".
01 Age PIC X(2) VALUE "49".
DISPLAY "Hello Sin!".
```

COBOL storage sizing must inspect every LET and SET value so each `PIC X` item fits the maximum assigned UTF-8 display-byte length. A branch-aware Known assignment emits `MOVE <exact literal> TO <variable>`. A runtime-unknown direct SET or later LET moves the current source item and copies its logical length; an Unknown composite LET or SET uses a runtime `STRING` plan plus numeric/Boolean display storage and writes the resulting length. Unknown output and conditions use the same current storage rather than selected concrete values. A mutated variable also has a COMP-5 `SMILE-SET-LENGTH-{variableIndex}` field; its compiler-owned name is compared with mapped user names case-insensitively and receives a deterministic numeric suffix on collision. Fixed-length data items must not leak padding into SMILE output.

A direct COBOL variable PRINT reads the mapped `WORKING-STORAGE` item rather than displaying a compiler-time copy of its known value. Every directly printed String has a logical-length field; mutated values also maintain one across SET. Output reads that current length and uses reference modification for nonempty storage. A runtime zero-length condition emits exactly one line feed and no padding space. This applies equally to ordinary Strings, normalized Block Strings, embedded NUL, UTF-8, control bytes, and intentional trailing whitespace. Integer and Boolean storage continues to display canonical SMILE text.

Conceptually, mutable output follows this GnuCOBOL shape:

```cobol
IF Name-LENGTH = 0
    DISPLAY X"0A" WITH NO ADVANCING
ELSE
    DISPLAY Name(1:Name-LENGTH) WITH NO ADVANCING
    DISPLAY X"0A" WITH NO ADVANCING
END-IF.
```

For example, a fixed-width empty item remains a storage placeholder while surrounding output must not leak its padding:

```cobol
01 Empty PIC X VALUE SPACE.
DISPLAY "[]".
```

Blank SMILE `PRINT` must emit an empty line, not a line containing a single space:

```cobol
DISPLAY X"0A" WITH NO ADVANCING.
```

MASM stays dependency-light and uses explicit output operations, but the generated assembly should keep explanatory right-side comments so learners can follow the code. Runtime variables retain `variable{n}Ptr` and `variable{n}Length`, and Integer variables also retain signed runtime numeric storage for arithmetic and relational conditions. LET data uses deterministic `variable{n}Value` labels. A branch-aware Known SET may use a deterministic `set{statementIndex}Value` label; runtime-unknown direct LET or SET copies current source value storage without later mutable-buffer aliasing, and Unknown composite expressions use bounded deterministic buffers plus one signed Integer text formatter. Direct and composite PRINT or condition plans read current storage. Exact static bytes may be used only when the complete value is branch-aware Known; the selected concrete trace is never a lowering authority after Unknown.

For MASM empty strings, storage may use one placeholder byte so a label has an address:

```asm
variable0Value BYTE 0
```

The logical string length must still be zero:

```asm
variable0ValueLength EQU 0
```

This prevents `WriteFile` from emitting a hidden NUL byte before the normal `PRINT` newline. A SET to an empty or non-empty value must update the runtime pointer and length rather than relying on the declaration's original label.

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
- destination preprocessor macros and standard-library macros that can be active in the generated translation unit;
- destination-language reserved identifier patterns, such as C and Objective-C names beginning with `__` or with `_` followed by an uppercase ASCII letter;
- another generated target name.

C and Objective-C must map every standard-library facility or type name emitted by the generator, including `bool`, `int64_t`, `size_t`, `printf`, `fwrite`, `fputc`, `fputs`, `strcmp`, `memcmp`, `memcpy`, `strlen`, `snprintf`, `main`, and `stdout`. Learner variables must never shadow a generated storage read, comparison, output call, or declaration type.

COBOL must map reserved words and identifiers that are not valid COBOL data names. Underscores should become readable hyphenated names such as `SMILE-internal`.

Java and Swift must map a single SMILE `_` identifier because `_` is not a usable ordinary local variable spelling in those targets.

Python must map keywords, the `match` and `case` soft keywords, relevant built-ins such as `str`, `bool`, `int`, `abs`, and `isinstance`, and generator-owned names including `main`, `__name__`, `_smile_text`, and `_smile_div`. A single `_` remains a valid Python identifier.

C, Objective-C, and C++ must protect the complete fixed-width Integer and limit macro family exposed by `<stdint.h>` or `<cstdint>`, including names such as `INT64_MAX`, `INT64_C`, `UINT64_MAX`, and `SIZE_MAX`. Protection belongs in the shared target identifier map, not in target generator source rewriting.

C++ must map all C++20 keywords and alternative tokens, generator-used names such as `std`, `main`, `cout`, `string`, `to_string`, and `int64_t`, and C++ implementation-reserved patterns. Any double underscore anywhere in a C++ identifier is reserved. A mapped spelling must remove that pattern from the final emitted name rather than merely prefixing the original spelling.

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

Every expression, statement, comment, and source-layout feature is validated against the `SmileEvaluator` reference oracle. The generated-target suite includes all four comment markers, nested and layout-only branches, comment-safe IF recovery, source blank-line boundaries, Block String ownership, unsafe control and Unicode payloads, C-family trailing backslash, Java Unicode-looking text, multilingual and long comments, IF control flow, SET mutation, wide Integers, and embedded NUL. Every generated source must retain every body and every source layout item exactly once except intentional safe wrapping. It runs all locally installed toolchains for all ten targets and compares exact UTF-8 stdout bytes after normalizing only CRLF to LF where explicitly permitted. Tests never trim or discard NUL, backspace, form feed, carriage return, tab, or meaningful trailing whitespace. Repeated generation must remain byte-for-byte deterministic. Java release validation requires both `javac` and `java` and executes the normative comment/layout program plus cumulative `examples/language.smile` without skips.

Release validation must distinguish warnings from the SMILE solution build and warnings from generated target programs. `SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1` activates destination-specific warning checks for compiler-backed targets; JavaScript and Python have no compile stage in their normal SMILE toolchains. The normative comment/layout program must build/run through all ten targets, return exit code zero, emit zero detected compiler warnings, preserve native comments and source blank-line boundaries safely, and match `SmileEvaluator`. This generated warning gate remains separate from the solution-build warning count.

The hosted `SMILE CI` Windows workflow restores, builds, and tests the Debug and Release solution on .NET SDK 10.0.302. That independent solution check does not install the complete destination toolchain matrix and does not replace strict local release runs with `SMILE_REQUIRE_JAVA=1`, `SMILE_REQUIRE_ALL_TARGETS=1`, and `SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1`.

## Destination-Language Freeze

C++ is SMILE's tenth and final planned destination language. Target-language expansion is frozen unless Sin explicitly reopens it. After v0.6.1 Full-Line Comments and Source Layout Preservation, later work should deepen input, loops, functions, scopes, debugging, and teaching tools rather than adding another backend. Rust, Zig, and Go remain deferred.
