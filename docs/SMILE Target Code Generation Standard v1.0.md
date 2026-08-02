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

SMILE keeps a language-neutral bound representation before target generation. Targets should use that representation instead of reparsing source text.

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

## Lower-Level Targets

Lower-level targets may need target-local lowering, but the emitted code should still look natural for that target.

Official `LET` v1.0 initializers are immutable string constants. C, Objective-C, MASM, and other lower-level targets may emit the evaluated declaration value instead of building strings at runtime:

```c
const char *FullName = "Sin Cioco";
```

This keeps early generated programs dependency-light and avoids introducing premature buffers or a SMILE runtime library.

For current C `PRINT`, SMILE should prefer one safe `printf` statement per SMILE `PRINT` where practical:

```c
printf("\n");
printf("Hello World!\n");
printf("Hello %s!\n", Name);
```

The generated `printf` format string must always be compiler-generated. SMILE variables must be passed as arguments, never as the format string:

```c
printf("%s\n", Name);
```

Literal percent signs from SMILE source must be escaped in the generated format string:

```c
printf("Progress: 100%%\n");
printf("%s is 100%% ready.\n", Name);
```

Objective-C follows the same stdout style while preserving Objective-C strings:

```objc
NSString *Name = @"Sin";
printf("Hello %s!\n", [Name UTF8String]);
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
- destination-language reserved identifier patterns, such as C and Objective-C names beginning with `__` or with `_` followed by an uppercase ASCII letter;
- another generated target name.

Java and Swift must map a single SMILE `_` identifier because `_` is not a usable ordinary local variable spelling in those targets.

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
