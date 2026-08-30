# Migrating to Core BASIC 1

SMILE 1.0 is intentionally source-breaking. The compiler accepts only Core BASIC 1 and does not offer a compatibility option. Rewrite older research source explicitly so the new meaning is visible.

Every “unsupported” example below is expected to fail compilation.

## Compatibility matrix

| Language boundary | Authoritative Core BASIC behavior | Superseded SMILE 1.0 behavior | Conflict | Canonical migration result | Affected surfaces |
|---|---|---|---|---|---|
| assignment | direct identifier assignment, with implicit first declaration | separate declaration and mutation keywords | grammatical | old forms rejected; direct assignment is the only path | lexer, parser, binder, evaluator, all generators, examples |
| `Print Name` | evaluate `Name` and print its value | treated bare content as raw literal template text | same source, different meaning | expression semantics only; literal text must be quoted | parser, binder, evaluator, generators, docs |
| quotes | doubled quotes inside Text | backslash-based escaping and additional literal forms | lexical | doubled-quote Text only | lexer, highlighting, tests |
| comments | apostrophe, full-line or inline | several alternate full-line markers | lexical | apostrophe only; old markers rejected | lexer, highlighting, examples |
| repetition | counted `For` and post-tested `Do` | a pre-test historical loop form | structural and semantic | use the canonical loops; no alias | parser, binder, evaluator, ten generators |
| input | outside Profile 1.0 | a historical console-input statement | excluded feature | rejected, with no silent replacement | parser, evaluator, CLI, Desktop, toolchains |
| language selection | one canonical grammar | compatibility work briefly proposed multiple modes | architectural | selector and fallback removed | public API, CLI, Desktop, tests, docs |

## Declarations and assignment

Unsupported earlier form:

```text
LET Name = "Sin"
SET Name = "Sin Cioco"
```

Canonical Core BASIC 1:

```smile
Name = "Sin"
Name = "Sin Cioco"
```

Use `Dim` when an explicit type and default value make the lesson clearer:

```smile
Dim Name As Text
Dim Age As Number
Dim Ready As Boolean
```

## Output

Unsupported raw-template and interpolation forms:

```text
PRINT Hello {Name}
Print $"Hello {Name}"
```

Canonical expression-list Print:

```smile
Print "Hello "; Name
```

A trailing semicolon suppresses the newline:

```smile
Print "Loading";
Print "."
```

## Loops

Unsupported earlier pre-test loop:

```text
WHILE Tally < 3
    SET Tally = Tally + 1
END WHILE
```

Canonical counted loop when the range is known:

```smile
For Tally = 1 To 3
    Print Tally
End For
```

Canonical post-tested loop when the body must run at least once:

```smile
Tally = 0
Do
    Tally = Tally + 1
Loop Until Tally >= 3
```

Core BASIC 1 does not contain a direct pre-test-loop replacement. If zero iterations matter, use an `If` guard around a `Do` or restructure the algorithm deliberately.

## Input

The earlier input statement is unsupported. Core BASIC 1 has no console input feature. Replace it with an assignment, constant, or explicit `Dim` default when the program's lesson does not require input. If interactive input is essential, that program is outside this frozen profile and must wait for an explicitly adopted canonical language extension.

## Text

Unsupported backslash escaping:

```text
Print "She said \"Hello\"."
```

Canonical doubled quotes:

```smile
Print "She said ""Hello""."
```

Earlier block-string and interpolation delimiters are unsupported. Use an ordinary quoted Text literal; a physical newline may remain inside an open Text literal when multiline Text is needed.

## Comments

Unsupported historical comment markers:

```text
REM comment
// comment
# comment
-- comment
```

Canonical comment:

```smile
' comment
Print "Hello" ' inline comment
```

## Migration check

Compile the rewritten file normally. An unknown language-related CLI option is an error because no dialect selection exists.

```powershell
dotnet run --project src/SMILE.Cli -- migrated.smile --target csharp
```

The rejection corpus under `tests/CoreBasicParity/rejected` permanently verifies that the unsupported forms above do not return through a hidden fallback.
