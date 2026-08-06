# Codex Implementation Instructions — SMILE v0.6.1 Full-Line Comments and Source Layout Preservation

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work directly on `main`.
- Sin is the only developer.
- Do not create or suggest a feature branch.
- Do not open a pull request.
- Re-read `AGENTS.md` before changing code.
- Inspect the newest `main` commit, current working tree, current `SMILE CI` workflow, and current project documentation before editing.
- Do not discard, reset, overwrite, or commit unrelated work.
- Do not force-push or rewrite published history.
- Follow KISS and KISS v2, “The Sin Way.”
- Preserve the destination-language freeze at ten targets.
- Commit all intended changes and push only after local validation is green.
- After pushing, verify that the `SMILE CI` run for the exact final `main` commit SHA completes successfully before reporting the task complete.

The reviewed baseline when this brief was prepared was:

```text
da1a105f94befa10bf6a4c3555ad60c0ed4b74d0
Sin and Codex: Require green post-push CI
```

Do not assume that SHA is still current. Always begin from the newest `main`.

---

# 1. Companion official specification

Use the complete companion file:

```text
SMILE - Full-Line Comments and Source Layout Preservation Official Specification v1.0.md
```

Publish it at:

```text
docs/SMILE Language Specification/SMILE - Full-Line Comments and Source Layout Preservation Official Specification v1.0.md
```

The implementation must conform to that specification.

Do not silently change the language design while coding.

---

# 2. Milestone

Create:

> **SMILE v0.6.1 — Full-Line Comments and Source Layout Preservation**

Implement four equivalent full-line comment forms:

```smile
REM Original BASIC-style comment
// C-family-style comment
# Script-language-style comment
-- SQL/Ada/Haskell-style comment
```

Also preserve source-authored blank lines in generated target source.

Comments and blank lines must remain non-semantic while being retained for target generation and tooling.

---

# 3. Required language behavior

## 3.1 Supported markers

Recognize:

```text
REM
//
#
--
```

## 3.2 First non-whitespace

Allow zero or more ASCII spaces or tabs before a marker.

Do not broaden horizontal whitespace rules to arbitrary Unicode whitespace.

## 3.3 Case-insensitive contextual `REM`

Recognize `REM` using ordinal case-insensitive comparison.

Require the formal boundary after `REM`.

Accept:

```smile
REM
rem
Rem A comment
rEm	A comment
```

Do not recognize as comments:

```smile
REMEMBER
REMARK
REMOTE
REM:
REM#
```

Do not add `REM` to the global keyword map or reserved-variable-name set.

Preserve:

```smile
LET REM = "Value"
PRINT {REM}
```

## 3.4 Symbol-marker spacing

The symbolic markers require no separating whitespace:

```smile
//comment
#comment
--comment
```

## 3.5 Full-line only

Do not implement inline or trailing comments.

Preserve PRINT raw-template behavior:

```smile
PRINT // This text is printed
PRINT # This text is printed
PRINT -- This text is printed
PRINT REM This text is printed
PRINT https://example.com
```

## 3.6 Block String ownership

Inside a SET Block String Literal:

- comment markers are String data;
- blank physical content lines are String data;
- existing delimiter, margin, newline, escape, trailing-whitespace, and NUL rules remain authoritative.

## 3.7 Blank-line preservation

A physical line containing only spaces or tabs outside a Block String becomes one non-semantic blank-line layout item.

Preserve:

- isolated blank lines;
- consecutive blank lines;
- leading blank lines;
- trailing blank lines;
- blank lines in IF, ELSE IF, and ELSE bodies.

Do not preserve spaces or tabs on an otherwise blank line. Emit an empty target line.

---

# 4. Non-goals

Do not add:

- inline comments;
- trailing comments;
- block comments;
- nested comments;
- documentation-comment semantics;
- apostrophe comments;
- `/* ... */` comments;
- a strip-comments option;
- `INPUT`;
- loops;
- functions;
- procedures;
- scopes;
- arrays;
- classes;
- floating-point or decimal values;
- one-line IF;
- assignment expressions;
- compound assignment;
- another destination language;
- a parser generator;
- a compiler framework;
- an unnecessary dependency.

Do not change runtime semantics of LET, SET, PRINT, IF, expressions, or Strings.

---

# 5. Required architecture: first-class non-semantic source items

Preserving comments and blank lines cannot be implemented by deleting them in the parser and attempting to rediscover them later.

Introduce one ordered source-item model used by program and branch bodies.

## 5.1 Recommended syntax model

Use a structure equivalent to:

```csharp
public abstract record SourceItemSyntax(TextSpan Span)
    : SyntaxNode(Span);

public abstract record StatementSyntax(TextSpan Span)
    : SourceItemSyntax(Span);

public sealed record FullLineCommentSyntax(
    FullLineCommentMarker Marker,
    string Payload,
    TextSpan Span)
    : SourceItemSyntax(Span);

public sealed record BlankLineSyntax(TextSpan Span)
    : SourceItemSyntax(Span);
```

Program and IF bodies should retain ordered source items.

The exact names may differ when a better repository-consistent name exists.

Do not reconstruct comments or blank lines later through source-offset guesses or a side table.

## 5.2 Recommended bound model

Use a structure equivalent to:

```csharp
public abstract record BoundSourceItem;

public abstract record BoundStatement
    : BoundSourceItem;

public sealed record BoundFullLineComment(
    FullLineCommentMarker OriginalMarker,
    string Payload)
    : BoundSourceItem;

public sealed record BoundBlankLine()
    : BoundSourceItem;
```

Bound program and branch bodies retain ordered items.

Provide filtered semantic-statement enumeration for evaluator, trace, analysis, simplification, and profiling.

Do not classify comments or blank lines as executable statements merely to avoid updating the ordered-list model.

## 5.3 Backward-compatible convenience properties

Where practical, preserve existing `Statements`-style APIs as filtered projections so existing semantic tests and compiler passes remain readable.

Avoid repeatedly allocating projections in hot generator paths. A small reusable enumeration helper is acceptable.

---

# 6. Shared full-line comment classifier

Add one focused front-end helper, such as:

```text
src/SMILE.Engine/FullLineCommentFacts.cs
```

Recommended responsibilities:

```csharp
internal static bool TryClassify(
    string physicalLine,
    int firstNonWhitespace,
    out FullLineCommentMarker marker,
    out int payloadStart)
```

Required behavior:

- recognize `//`, `#`, and `--` exactly;
- recognize `REM` ordinal case-insensitively;
- enforce the REM boundary;
- return the payload start;
- return false for blank lines and near misses;
- operate only on one physical line without its terminator.

Use the same classifier in:

- indexed parser;
- public lexer;
- IF over-depth recovery;
- tests;
- syntax-highlighting expectations where feasible.

Do not duplicate marker logic in every component.

---

# 7. Parser changes

The parser currently skips blank lines. Replace that behavior with layout capture.

In every statement-list loop, classify in this order:

```text
blank line
full-line comment
IF terminator or malformed terminator
ordinary semantic statement
```

This order is mandatory.

## 7.1 Blank lines

Create a blank-line source item for every physical blank line.

Do not create semantic diagnostics or statements.

## 7.2 Comments

Create a full-line-comment source item containing:

- original marker kind;
- exact payload after the marker;
- full source span;
- correct line and column information.

Leading indentation before the source marker is represented by the span but is not part of the payload.

## 7.3 IF bodies

Preserve comments and blank lines in:

- initial IF body;
- each ELSE IF body;
- final ELSE body;
- nested IF bodies.

Comments that spell `ELSE`, `END IF`, `IF`, `ENDIF`, or malformed headers must not affect structure.

## 7.4 Program span

Program and branch spans must remain valid when a list contains only comments or blank lines.

A comment-only or blank-only program must parse successfully.

---

# 8. IF maximum-depth recovery

Update iterative over-limit IF recovery to classify and ignore valid comment lines before structural analysis.

Comment payloads such as:

```smile
// END IF
# IF TRUE = TRUE THEN
-- ELSE
REM ENDIF
```

must not affect nesting counts.

Block String ownership remains stronger than comment recognition.

Continue using the canonical block String scanner so comment-looking and IF-looking lines inside a Block String remain data.

Add recovery tests combining:

- depth 129;
- depth 1,000;
- all four comment markers;
- standalone ELSE followed by nested IF;
- same-line ELSE IF;
- malformed END IF;
- Block String content;
- later recoverable top-level code.

---

# 9. Public lexer

Add a comment token/trivia kind or an equivalent lexical result that preserves:

- marker kind;
- payload;
- source span;
- line information.

Do not tokenize comment payload as identifiers, operators, Strings, or invalid characters.

Preserve normal EOL accounting.

A final comment without a trailing newline must terminate cleanly at EOF.

Do not use recursive token skipping for many consecutive comments.

The bounded expression lexer must not treat inline marker text as comments.

Block String tokenization must continue to own all physical content lines until its closing delimiter.

---

# 10. Binder

Bind syntax source items into ordered bound source items.

For comments and blank lines:

- create no variable;
- bind no expression;
- add no diagnostic;
- append nothing to concrete execution trace;
- create no semantic value.

Keep existing semantic binding behavior unchanged.

Ensure `REM` remains a valid identifier outside first-position comment syntax.

---

# 11. Evaluator, execution trace, and analysis

Update semantic passes to enumerate only `BoundStatement` items.

Comments and blank lines must not:

- receive `BoundStatementAnalysis`;
- receive ordinals;
- appear in assigned-value tracking;
- affect mutation sets;
- affect integer ranges;
- affect expression display facts;
- affect concrete values;
- affect output.

Add regressions showing a commented/spaced program and its layout-stripped equivalent have identical evaluator results and semantic analysis.

---

# 12. Simplifier and statement-tree helpers

Preserve layout items in exact order while simplifying semantic statements recursively.

The simplifier must not:

- delete comments;
- coalesce comments;
- reorder comments;
- delete blank lines;
- move layout across an IF clause boundary.

Semantic statement enumeration helpers must filter layout items without losing nested statement traversal.

---

# 13. Target comment emitter

Add one shared target-comment helper used by all ten generators.

Recommended mapping:

```text
C#          //
C           //
C++         //
JavaScript  //
Java        //
Objective-C //
Swift       //
Python      #
COBOL       *>
MASM x64    ;
```

The helper should accept:

- target language;
- target indentation;
- comment payload.

It should return one or more safe target comment lines.

Do not make each generator reimplement marker mapping and escaping.

---

# 14. Target comment safety

Preserved comments must never change generated program behavior.

Implement deterministic target-safe payload rendering.

## 14.1 Unsafe line separators and controls

Render target-unsafe control or line-separator characters using a readable reversible form such as:

```text
\u{HEX}
```

At minimum test:

- NUL;
- U+2028;
- U+2029;
- other C0 controls except permitted tab.

## 14.2 C-family trailing backslash

For C, C++, and Objective-C, prevent a generated `//` comment line from ending with a literal backslash that could splice the following physical line.

Encode or otherwise safely represent the final backslash without dropping it.

## 14.3 Java Unicode escapes

Java processes Unicode escapes before normal tokenization, including inside comments.

Prevent source payload text such as:

```text
\u000A
```

from becoming a generated line break or executable injection.

Encode the initiating backslash or otherwise make the sequence safe and readable.

## 14.4 Python placement

Emit preserved Python comments inside `main()` or the existing source-order user body, not before file-level boilerplate.

This prevents a source comment from becoming a Python encoding declaration or shebang-like file directive.

## 14.5 Unicode/toolchain compatibility

Prefer readable literal Unicode when every current target toolchain accepts it.

If a target cannot compile a printable Unicode comment reliably, use the documented reversible ASCII escape form for that target only.

Add all-ten-target tests with multilingual comment payloads.

---

# 15. Long comment lines

GnuCOBOL free source has a practical line-length limit.

Implement target-safe wrapping when required.

Requirements:

- normal comments remain one line;
- fragments remain consecutive;
- payload order is exact;
- no character is lost or invented;
- indentation and target marker repeat;
- output is deterministic;
- COBOL comment lines stay safely below the supported toolchain limit.

Use a conservative maximum such as 200 target columns for COBOL comments unless repository toolchain evidence supports another safe value.

Test a long comment through every target. Add target-specific wrapping only where necessary.

---

# 16. Generator integration

Every generator must iterate ordered source items rather than only semantic statements.

Handle each item:

```text
BoundFullLineComment -> emit target-native comment
BoundBlankLine       -> append one empty line
BoundStatement       -> existing generation
```

Do not request analysis facts for layout items.

## 16.1 Primary generated file only

Preserve source comments and blank lines in the primary generated program file.

Do not copy them into:

- generated project files;
- launchers;
- build scripts;
- auxiliary metadata files.

## 16.2 Target indentation

Apply destination indentation for the current generated body.

Do not copy source indentation before a comment marker verbatim.

Preserve the payload after the marker.

## 16.3 Statement chunks

When one SMILE statement generates multiple target lines, treat the generated chunk as one source-order unit.

A blank source line between statements appears between their generated chunks.

## 16.4 Generator-owned formatting

Preserve source-originated blank lines in addition to required target formatting.

Where current generators automatically add blank lines around every statement, distinguish those from source-authored layout so tests can prove source blank lines are not lost.

Avoid creating uncontrolled duplicate blank lines when a straightforward refactor can separate generator formatting from source formatting.

---

# 17. Structurally split targets

Some targets relocate declarations or data.

## 17.1 MASM

Emit source layout in the `.code` source-order statement stream.

Existing LET initialization code already follows source order.

Do not duplicate user comments into `.data`.

## 17.2 COBOL

Emit every user comment exactly once.

Use `*>` in free source format.

Preserve source-order layout in the nearest deterministic user-code region.

Known LET declarations may remain in WORKING-STORAGE while executable source layout remains in PROCEDURE DIVISION.

Do not duplicate comments in both divisions.

Document and test the chosen deterministic placement policy.

## 17.3 Exact target line numbers are not required

Do not attempt to force target physical line numbers to match SMILE source line numbers.

Preserve relative order and layout boundaries while respecting target structure.

---

# 18. Empty target bodies

Comments and blank lines are non-executable.

Continue emitting required placeholders for semantically empty bodies.

Examples:

Python:

```python
if condition:
    # preserved comment

    pass
```

COBOL may require its existing `CONTINUE` lowering.

Add tests for:

- comment-only IF branch;
- blank-only IF branch;
- comment-and-blank-only branch;
- explicit empty ELSE with comments.

---

# 19. Desktop syntax highlighting

Update:

```text
src/SMILE.Desktop/Highlighting/SMILE.xshd
```

Add a dedicated Comment style.

Recognize all four markers only at first non-whitespace.

Requirements:

- `REM` is case-insensitive;
- REM boundary is enforced;
- symbolic markers need no following whitespace;
- inline occurrences are not comments;
- PRINT raw-template marker text is not a comment;
- ordinary String contents remain String;
- Block String contents remain String, including marker-looking lines;
- incomplete editing remains safe.

The multiline Block String span must retain precedence over comment rules.

Add or expand AvalonEdit highlighting tests.

---

# 20. Source-layout test suite

Add a focused file such as:

```text
tests/SMILE.Tests/FullLineCommentAndLayoutConformanceTests.cs
```

Keep tests cohesive.

## 20.1 Marker recognition

Test every marker with:

- no leading whitespace;
- leading spaces;
- leading tabs;
- no payload;
- payload without separating space;
- Unicode payload;
- punctuation;
- EOF without final newline.

## 20.2 REM rules

Accept all casing variants.

Reject as comments:

```text
REMEMBER
REMARK
REMOTE
REM:
REM#
```

Prove `LET REM = ...` remains valid.

## 20.3 Blank lines

Test:

- one blank line;
- multiple consecutive blank lines;
- leading blank lines;
- trailing blank lines;
- whitespace-only blank lines;
- blank lines inside each IF clause;
- blank lines inside Block Strings as data.

## 20.4 Source positions

Place comments and blank lines before an invalid statement.

Assert exact physical diagnostic line and column.

## 20.5 Comment-only and blank-only programs

Require successful parse, bind, evaluate, and target generation.

Require empty evaluator output.

## 20.6 PRINT raw-template near misses

Verify exact output for:

```smile
PRINT // text
PRINT # text
PRINT -- text
PRINT REM text
PRINT https://example.com
```

## 20.7 Inline near misses

Verify inline markers are not removed and produce existing diagnostics where applicable.

## 20.8 Ordinary and interpolated Strings

Verify markers remain data.

## 20.9 Block String preservation

Compare exact bound values and output bytes for all markers and blank lines inside Block Strings.

Include trailing whitespace and embedded NUL coverage where appropriate.

## 20.10 IF structure and recovery

Prove comments cannot change clause boundaries or over-depth recovery.

## 20.11 Public lexer

Verify comment token/trivia payload, marker, spans, EOL behavior, and many consecutive comments without recursion.

---

# 21. Target layout tests

For each target, verify the native marker:

```text
C#          //
C           //
C++         //
JavaScript  //
Java        //
Objective-C //
Swift       //
Python      #
COBOL       *>
MASM x64    ;
```

Use a source program with all four SMILE markers and assert each maps to the one target marker.

Verify:

- original comment order;
- payload preservation;
- target indentation;
- no original SMILE marker leaks merely because it was the source marker;
- comment appears exactly once, except intentional safe wrapping;
- blank lines appear at source boundaries;
- consecutive blank lines remain consecutive;
- comments and blank lines inside nested IF bodies remain correctly placed.

---

# 22. Blank-line example acceptance

Use the user's layout example:

```smile
LET a = 49

PRINT a
```

For targets that keep LET and PRINT in one source-order body, assert one source-originated blank line separates their generated chunks.

For MASM, assert the blank line appears between the LET initialization chunk and PRINT chunk in `.code`.

For COBOL, assert the selected deterministic policy preserves the blank-line boundary in the nearest source-order PROCEDURE layout while the LET declaration remains valid in WORKING-STORAGE.

Do not change the existing meaning of `PRINT a`.

---

# 23. Comment-safety tests

Add direct tests for payloads that could be unsafe after target mapping:

```text
Ends with backslash \
Java-looking \u000A escape
U+2028
U+2029
NUL
multilingual text
very long comment
```

Compile/run all available targets.

Require:

- no target source injection;
- no swallowed following statement;
- no compiler warning;
- exact runtime output;
- deterministic source.

---

# 24. Semantic-equivalence tests

Create two programs:

1. commented and spaced;
2. semantically identical with comments and blank lines removed.

Assert:

- identical binding diagnostics;
- identical evaluator output;
- identical final semantic values;
- identical executable behavior on all ten targets.

Generated source is expected to differ in comments and blank lines.

Do not assert byte-identical generated source between the two programs.

---

# 25. Cumulative language reference

Extend:

```text
examples/language.smile
```

Do not replace or remove the existing cumulative LET, PRINT, SET, Block String, and IF tour.

Add:

- all four comment forms;
- case-insensitive REM;
- a valid variable named REM;
- comments in IF bodies;
- deliberate blank lines between statements;
- multiple consecutive blank lines;
- markers and blank lines inside a Block String;
- PRINT raw-template marker text.

Keep runtime output deterministic and documented.

Update cumulative-reference tests and packaged Desktop-copy tests.

---

# 26. Version and documentation

Update release identity to:

```text
0.6.1 Full-Line Comments and Source Layout Preservation
```

Update applicable locations:

- Desktop project metadata;
- About dialog;
- README;
- roadmap;
- architecture;
- high-level language specification;
- target-generation standard;
- official-specification index and cross-links;
- requirements/progress history;
- AGENTS permanent rules;
- cumulative language documentation.

Do not add a comment diagnostic code.

Document that source comments are copied into generated files and must not contain secrets.

---

# 27. Required all-ten-target runtime validation

Use the normative acceptance program from the official specification.

For every target:

1. transpile;
2. assert native comment markers;
3. assert source blank-line boundaries;
4. compile or run through the installed toolchain;
5. require exit code 0;
6. compare exact stdout with `SmileEvaluator`;
7. require zero detected compiler warnings where supported.

Keep Java, all ten targets, and generated-warning gates mandatory in strict validation.

---

# 28. Build and test commands

Run from the actual repository root. Examples use `D:\SMILE`.

## Restore

```bat
cmd /c "cd /d D:\SMILE && dotnet restore SMILE.sln"
```

## Debug

```bat
cmd /c "cd /d D:\SMILE && dotnet build SMILE.sln -c Debug --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
```

## Release

```bat
cmd /c "cd /d D:\SMILE && dotnet build SMILE.sln -c Release --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

## Strict Debug

```bat
cmd /c "cd /d D:\SMILE && set SMILE_REQUIRE_JAVA=1 && set SMILE_REQUIRE_ALL_TARGETS=1 && set SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1 && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
```

## Strict Release

```bat
cmd /c "cd /d D:\SMILE && set SMILE_REQUIRE_JAVA=1 && set SMILE_REQUIRE_ALL_TARGETS=1 && set SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1 && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

Required:

- zero build warnings and errors;
- zero test failures;
- zero unexpected skips;
- Java executed;
- all ten targets executed;
- zero detected generated compiler warnings;
- exact evaluator conformance;
- deterministic generation.

---

# 29. Desktop smoke test

Launch the real Desktop app and verify:

1. first paint remains responsive;
2. cumulative source loads;
3. all four comments highlight correctly;
4. REM is case-insensitive;
5. REMEMBER is not a comment;
6. inline markers are not comments;
7. markers inside Block Strings remain String-highlighted;
8. blank lines remain visible in SMILE and generated panes;
9. changing a source blank-line count updates generated panes;
10. comments in nested IF blocks remain placed correctly;
11. comment-only branches remain valid;
12. rapid target switching remains responsive;
13. representative C#, MASM, COBOL, and Python generated comments use native markers;
14. Build & Run produces exact output;
15. About displays:

```text
0.6.1 Full-Line Comments and Source Layout Preservation
```

---

# 30. Acceptance criteria

This task is complete only when all requirements below are true.

## Comment syntax

- REM, //, #, and -- work at first non-whitespace.
- REM is case-insensitive and boundary-sensitive.
- REM remains a valid identifier elsewhere.
- Inline comments are not implemented.
- PRINT raw marker text remains printable.
- ordinary and interpolated String marker text remains data.
- Block String marker lines remain exact String data.

## Source layout

- blank source lines are retained as non-semantic items;
- consecutive blank lines preserve count;
- leading and trailing blank lines are retained in generated user bodies;
- comments and blank lines retain relative order;
- target boilerplate may add but does not erase source layout;
- source and target physical line numbers need not match.

## Architecture

- one shared classifier exists;
- syntax and bound ordered-item models retain layout;
- comments and blank lines are not executable statements;
- evaluator, trace, analysis, and profiles ignore layout;
- simplifier preserves layout;
- generators emit layout directly from ordered bound items;
- no fragile source-offset side table is used.

## Targets

- all ten targets use the official native marker;
- comments appear exactly once except safe wrapping;
- payload is target-safe;
- blank lines are represented;
- empty bodies still receive required no-op constructs;
- all target programs compile/run and match evaluator output;
- zero generated warnings remain.

## Release

- Debug and Release builds are clean;
- normal and strict tests pass;
- docs and About identify v0.6.1;
- cumulative language reference is updated and packaged;
- changes are committed and pushed;
- final exact-SHA `SMILE CI` run concludes success.

---

# 31. Commit message

Use a detailed public commit message similar to:

```text
Sin and Codex: Preserve comments and source layout

Release SMILE v0.6.1 Full-Line Comments and Source Layout Preservation.

Add first-non-whitespace REM, //, #, and -- full-line comments with case-insensitive contextual REM and no inline-comment syntax. Retain comments and blank physical source lines as ordered non-semantic source items through parsing and binding, while keeping evaluator, execution-trace, analysis, and runtime behavior unchanged.

Emit preserved comments with native syntax for all ten targets and preserve source-authored blank-line boundaries in generated user-code regions. Add target-safe comment escaping for line separators, control characters, C-family trailing backslashes, Java Unicode escapes, Unicode/toolchain constraints, and COBOL line limits. Preserve Block String content, PRINT raw templates, IF structure, depth recovery, empty-body placeholders, deterministic generation, syntax highlighting, and exact evaluator conformance.

Validation: <insert exact normal and strict build/test results>. Post-push SMILE CI: <insert exact final run ID and successful conclusion>.
```

Replace placeholders with actual results.

Commit all intended changes and push to `main`.

Do not create a Git tag or GitHub Release unless Sin explicitly requests it or the repository establishes that convention.

---

# 32. Mandatory post-push verification

After pushing:

1. read the exact final `main` SHA;
2. locate `SMILE CI` for that exact SHA;
3. wait for completion;
4. require conclusion `success`;
5. confirm Restore, Debug Build/Test, and Release Build/Test succeeded.

Do not use an older run as evidence.

If CI fails:

- inspect logs;
- fix the root cause;
- rerun applicable local validation;
- create a normal follow-up commit;
- push;
- verify the replacement exact-SHA run is green.

Do not force-push or rewrite public history.

---

# 33. Completion report to Sin

Report:

- final commit SHA;
- push status;
- files added or changed;
- version identity;
- source-item architecture;
- comment classifier location;
- bound layout representation;
- target comment mapping;
- target-safety handling;
- blank-line preservation policy;
- COBOL placement policy;
- exact normal Debug/Release results;
- exact strict Debug/Release results;
- all-ten-target result;
- generated-warning result;
- normative acceptance output;
- cumulative language result;
- Desktop smoke result;
- GitHub Actions run ID and conclusion;
- whether a corrective follow-up commit was needed;
- remaining known limitations.

Highlight these as ready for testing:

- **Case-insensitive contextual REM comments**
- **// full-line comments**
- **# full-line comments**
- **-- full-line comments**
- **Target-native comment preservation**
- **Blank-line preservation**
- **Block String layout protection**
- **Comment-safe IF parsing and recovery**
- **SMILE v0.6.1 Full-Line Comments and Source Layout Preservation**
