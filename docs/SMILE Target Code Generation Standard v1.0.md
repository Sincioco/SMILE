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

## Identifier Spelling

SMILE preserves user-authored identifier spelling in generated target code. A SMILE declaration such as:

```smile
LET Name = "Sin"
```

should keep `Name` in target languages unless a future language feature explicitly introduces target-specific name mapping.
