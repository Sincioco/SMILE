# SMILE Beginner-First Idiomatic Transpilation and Permanent Guardrails

## Purpose

This document establishes a permanent SMILE rule for target-language generation.

It applies to:

- C#;
- C;
- MASM x64 / Assembly;
- every paused language if re-enabled later;
- every future language added to SMILE.

The current three-language focus is temporary.

This transpilation philosophy is permanent.

---

# 1. Official SMILE Transpilation Rule

Add the following as an official project rule:

> **SMILE should transpile to the normal, idiomatic way a beginner would write the equivalent program in each target language. SMILE should not impose low-level cross-language runtime behavior that forces otherwise-simple target code to use compiler-generated runtime libraries.**

Also add:

> **Generated target source is educational output. Prefer the simplest conventional destination-language construct that expresses the learner's intent clearly.**

And:

> **A target backend must not mechanically imitate another target backend. Each backend should look like normal code for its own language.**

---

# 2. Official SMILE Variable Reference Rule

Make the following an official SMILE curly-brace rule:

> **Curly braces `{ }` identify interpolation holes in text-oriented syntax such as raw PRINT templates. They are not general variable-reference delimiters.**
>
> **When a statement or expression position already requires an identifier or expression, variables are written directly without braces.**

Examples:

```smile
LET Name = ""
INPUT Name
SET Name = "Sin"

PRINT Name
PRINT {Name}

IF Name = "Sin" THEN
    PRINT Hello {Name}!
END IF
```

Meaning:

```text
INPUT Name          -> operate on variable Name
SET Name = ...      -> operate on variable Name
Name = "Sin"        -> expression reads variable Name
PRINT Name          -> literal template text "Name"
PRINT {Name}        -> interpolate current value of Name
```

Do not introduce:

```smile
INPUT {Name}
SET {Name} = ...
IF {Name} = ...
```

Curly braces are interpolation syntax, not a general "variable marker."

Update the official PRINT/core-expression/INPUT documentation wherever needed so this rule is explicit and consistent.

---

# 3. Primary educational objective

When a learner opens a generated target pane, the first question is:

> "Is this how a beginner would normally be taught to write this program in this language?"

If the answer is no because the generator exposes compiler mechanics, runtime scaffolding, helper state machines, target-neutral abstractions, or uncommon APIs, the generator should be reconsidered.

Target source should teach the **destination language**, not the implementation of the SMILE transpiler.

---

# 4. Native-first rule

For every SMILE feature:

1. Identify the normal beginner-level construct in the destination language.
2. Use it directly when it expresses the ordinary feature correctly.
3. Add only the minimum extra code that language normally requires.
4. Do not invent a SMILE runtime API unless the destination genuinely lacks a reasonable native expression.
5. Do not force low-level behavior to be identical across all targets when doing so makes ordinary target code unnatural.

Examples:

## C#

Prefer:

```csharp
Console.WriteLine("Hello");
string name = Console.ReadLine() ?? "";
int age = int.Parse(Console.ReadLine()!);
```

over:

```csharp
SmileRuntime.Print(...)
SmileRuntime.ReadString(...)
SmileRuntime.ReadInteger(...)
```

or large `_smile_*` helper implementations.

## C

Prefer normal beginner C:

```c
printf("How old are you? ");
scanf("%d", &age);
```

when that is the ordinary translation of the SMILE concept.

Do not replace simple `scanf` with a generated line reader, strict UTF-8 decoder, state machine, byte limit, custom parser, and runtime error dispatcher merely to reproduce identical edge cases across unrelated target runtimes.

## MASM x64

For a simple Windows console example, recognizable CRT/Win64 calls are acceptable and preferred when they make the assembly substantially clearer:

```text
printf
scanf
ExitProcess
```

Do not generate a complete generic input runtime for a tiny beginner example unless there is no simpler reasonable target-native representation.

---

# 5. Target-native edge behavior is acceptable

SMILE should preserve the **ordinary conceptual behavior** of a program.

For example:

```smile
LET age = 0
INPUT age
```

means:

```text
read an Integer from standard input into age
```

It does not require every target to implement the exact same internal byte parsing algorithm.

For normal valid input, generated programs should behave consistently.

For obscure host/runtime edge cases such as:

- malformed redirected byte sequences;
- embedded NUL in line-oriented console input;
- exact byte-count boundaries;
- unusual CR-only streams;
- the precise exception/error text emitted by a host library;

prefer the destination language's normal behavior unless an explicit SMILE requirement is educationally important enough to justify extra code.

This rule intentionally supersedes earlier over-engineered cross-target requirements where those requirements force simple programs to contain large generated runtimes.

---

# 6. Revisit INPUT v1.0

The current INPUT implementation introduced substantial complexity to guarantee:

- strict UTF-8 redirected input;
- a shared exact 4096-byte line limit;
- embedded-NUL preservation;
- exact cross-target CR/LF behavior;
- exact SMILER1501-1506 runtime messages;
- strict cross-target parsing equivalence;
- byte-for-byte evaluator parity.

Review those requirements.

Keep only requirements that remain useful to SMILE's educational mission.

The revised INPUT specification should primarily define:

1. `INPUT variable` reads user input into an existing variable.
2. The existing variable type determines conversion.
3. String input reads a line of text.
4. Integer input converts ordinary integer text.
5. Boolean input converts the approved SMILE Boolean representation.
6. Failure behavior should be sensible and documented.
7. Targets should use normal native facilities wherever practical.

Do not keep a low-level portability requirement solely because tests were previously written for it.

Tests must follow the language mission; the language mission does not exist to satisfy old tests.

---

# 7. Do not replace native code with a generic SmileRuntime

The strategic reset does **not** mean:

```csharp
SmileRuntime.DeclareInteger("age", 0);
SmileRuntime.Print("How old are you?");
SmileRuntime.InputInteger("age");
```

That is still wrong for SMILE's teaching mission.

The learner should see real C#, C, Assembly, and future target-language constructs.

Helpers are acceptable only when:

- the destination genuinely needs them;
- the helper is small;
- the helper does not hide the concept the learner is supposed to see;
- native code would otherwise be much more confusing.

---

# 8. One-to-one mapping is a guideline, not a prison

Prefer recognizable mapping between SMILE statements and target operations.

However, allow a target to combine or simplify operations when a competent beginner-oriented example naturally would.

Example:

```smile
LET age = 0
INPUT age
```

may naturally become C#:

```csharp
int age = int.Parse(Console.ReadLine()!);
```

if the initial `0` is never observed before INPUT.

C might more naturally use:

```c
int age;
scanf("%d", &age);
```

MASM may reserve initialized storage:

```asm
age dd 0
```

and then call `scanf`.

Do not force all targets to preserve the same textual lowering.

Any combination/elision must be semantically safe for ordinary program behavior and must improve clarity.

---

# 9. Preserve expression intent

Continue good previous SMILE decisions:

- raw PRINT template text remains text;
- `{Name}` in PRINT remains interpolation;
- `$"..."` remains explicit interpolation;
- explicit concatenation remains explicit concatenation when natural;
- IF maps to normal target IF control flow;
- WHILE maps to normal target WHILE/loop control flow;
- Block Strings use the clearest idiomatic multiline representation available;
- source comments map to native target comments;
- variable names remain collision-safe.

Do not regress these while simplifying runtime behavior.

---

# 10. C# output philosophy

Use conventional beginner C#.

Prefer:

```csharp
using System;

class Program
{
    static void Main()
    {
        Console.Write("How old are you? ");
        int age = int.Parse(Console.ReadLine()!);

        Console.WriteLine($"You are {age} years old.");
    }
}
```

This is the approved complexity/style direction.

General rules:

- `Console.Write` / `Console.WriteLine` for output as appropriate to SMILE semantics;
- `Console.ReadLine` for line input;
- `int.Parse`, `long.Parse`, `bool.Parse`, `TryParse`, or similarly conventional constructs according to approved SMILE type behavior;
- normal C# interpolation;
- ordinary variables;
- normal `if`, `else if`, `else`, `while`;
- no custom generated SMILE runtime when normal C# already has the concept.

Do not emit:

- raw standard-input byte streams for ordinary INPUT;
- custom UTF-8 line readers for ordinary INPUT;
- custom integer parsers when normal parsing is acceptable;
- large error-dispatch helper libraries;
- compiler-owned state variables visible in learner code.

---

# 11. C output philosophy

Use conventional beginner C.

The supplied desired style is:

```c
#include <stdio.h>

int main(void)
{
    int age;

    printf("How old are you? ");
    scanf("%d", &age);

    printf("You are %d years old.\n", age);

    return 0;
}
```

General rules:

- `printf` for normal output;
- `scanf` for simple numeric input when appropriate;
- `fgets` for line-oriented String input when spaces must be preserved;
- normal C arrays/strings;
- normal `if`, `else if`, `else`, `while`;
- standard library calls familiar to beginners;
- only include headers actually needed.

Do not generate a custom UTF-8 parser, 4096-byte runtime contract, error state machine, exact String-length bookkeeping, or generalized runtime library for a simple beginner program unless a current approved SMILE feature truly requires it.

When C's native representation imposes a real limitation, choose the simplest documented tradeoff rather than automatically simulating a richer cross-language runtime.

---

# 12. MASM x64 output philosophy

Assembly is naturally more verbose, but it should still be proportionate to the source program.

The supplied desired style uses:

```asm
includelib msvcrt.lib
includelib kernel32.lib

extern printf:proc
extern scanf:proc
extern ExitProcess:proc
```

with simple data and direct calls.

That is the approved direction for tiny console programs.

Generate enough comments to teach important assembly concepts, but do not comment every obvious instruction.

Prefer:

- clear `.data` values;
- straightforward `main PROC`;
- correct Windows x64 shadow space/alignment;
- direct `printf` / `scanf` calls when suitable;
- direct `ExitProcess`;
- clear source-variable storage;
- normal labels for IF/WHILE.

Do not turn every INPUT into a custom byte reader + UTF-8 validator + line parser + error dispatcher unless a separately approved feature truly needs that machinery.

---

# 13. Future-language rule

Any future backend proposal must answer:

1. What would a beginner normally write in this language?
2. What native API/construct represents the SMILE feature?
3. Why is any custom helper necessary?
4. Can the helper be avoided?
5. Does the generated source still teach the destination language?
6. Is the code simpler than a generalized cross-language runtime?
7. Are tests checking readability as well as execution?

A future target must not be approved merely because it can reproduce evaluator bytes.

---

# 14. Golden readability tests

For active targets, add small golden/structural tests that inspect learner-facing generated source.

At minimum cover:

- LET + PRINT;
- INPUT;
- interpolation;
- SET;
- IF;
- WHILE;
- Block String.

Tests should fail if simple output regresses into generated runtime machinery.

For small fixtures, exact expected source is encouraged.

Example C# tests should be able to assert the absence of:

```text
_smile_read_byte
_smile_read_line
SmileRuntime.ReadString
System.IO.Stream
```

when native C# constructs are sufficient.

C tests should be able to assert the absence of:

```text
_smile_valid_utf8
_smile_read_line
_smile_input_error
```

for simple native input.

MASM tests should ensure a tiny program remains tiny enough to inspect and uses the chosen normal APIs.

---

# 15. Update AGENTS.md

Add this permanent instruction prominently:

> **Beginner-first native transpilation is a permanent SMILE invariant. Generated code must use the normal idiomatic destination-language construct whenever practical. Do not introduce compiler-generated runtime machinery merely to force obscure edge-case behavior to be identical across targets.**

Also add the Variable Reference Rule.

Future Codex sessions must see these before implementing new features.

---

# 16. Review existing complexity

Audit existing C#, C, and MASM generators.

Identify code that exists primarily to support old cross-target exactness requirements.

For each such area:

```text
Keep
Simplify
Replace with native target construct
Remove
```

Do not refactor unrelated code merely for aesthetic reasons.

Prioritize the code paths that affect ordinary learner examples.

---

# 17. Definition of done

This rule is successfully implemented when:

- tiny SMILE programs generate tiny normal target programs;
- C# looks like C# tutorials;
- C looks like C tutorials;
- Assembly looks like understandable Windows x64 assembly;
- target-native facilities are preferred;
- `{}` is clearly interpolation-only;
- custom SMILE runtime code is exceptional rather than normal;
- old tests that mandate unnecessary runtime complexity are revised/removed;
- new readability tests protect the mission;
- documentation makes the policy permanent for all future languages.
