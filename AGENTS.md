# Project Instructions

- SMILE is a public repo, so write detailed commit messages that help people follow its progression.
- Whenever Codex creates a commit for this project, prefix the commit subject with `Sin and Codex:`.
- Do not commit or push unless Sin explicitly asks for it.
- All SMILE development is performed directly on `main`. Sin is the only developer. Do not create or recommend feature branches unless Sin explicitly changes this rule.
- When Sin says to "commit all files" and push, interpret that as staging and committing all current unstaged and untracked repo changes, while still respecting `.gitignore`.
- Never include build/output folders or generated build/output files in commits, even when committing all files. Do not force-add ignored artifacts.
- Never force-push, never discard user work, and do not commit unrelated local changes.

## Post-Push CI Completion Gate

- **Post-push CI verification:** After pushing to `main`, identify the exact current `main` commit SHA and inspect the `SMILE CI` GitHub Actions run for that SHA and the expected push event. The task is not complete until that run has status `completed` and conclusion `success`. An older green run does not validate a newer commit, and a run that is queued, in progress, skipped, cancelled, timed out, or neutral is not successful completion.
- If the exact-SHA run fails, inspect the failed step and logs, fix the root cause, rerun all applicable local validation, create a normal follow-up commit, push it, and verify the replacement commit's own `SMILE CI` run. Preserve the failed run for history; never hide or delete it, amend the public commit, force-push, or rewrite published `main` history merely to remove a failure.
- When a failure is a CI-only portability problem, correct the underlying test or workflow issue rather than weakening production behavior.
- The completion report for every task pushed to `main` must include the final commit SHA, the `SMILE CI` workflow name and conclusion, whether the first run passed or required a follow-up fix, and the final run URL or ID when available.

## Guiding Principles

- SMILE stands for "Simple Modern Interactive Learning Environment." Use that expansion in public documentation and in the IDE title where the full project name is shown.
- SMILE's mission is: "SMILE is a modern programming language inspired by BASIC, designed to help newcomers learn not only how to write code, but also how programming languages work at a fundamental level. Building on BASIC’s simplicity and accessibility, SMILE allows students to transpile their code into multiple programming languages and compile the resulting programs. This enables learners to see how the same logic and concepts are expressed using different languages and syntaxes. Through this comparative approach, students can recognize an essential principle: despite their surface-level differences, all programming languages share the same core fundamentals. The primary goal is therefore not to memorize the syntax of a particular language, but to develop logical thinking, problem-solving skills, and a strong understanding of programming concepts. By combining simplicity, experimentation, and cross-language learning, SMILE provides a fun and educational environment that teaches students how to think like programmers."
- KISS and KISS v2, "The Sin Way," govern the entire SMILE project, including architecture, UI, runtime behavior, documentation, tests, and generated target-language code.
- Choose the simplest complete solution. Avoid unnecessary complexity, abstractions, frameworks, dependencies, code, files, folders, classes, methods, variables, features, and bells and whistles.
- User-experience performance is the first performance priority. Functional performance is second.
- The WPF UI thread must never be blocked by toolchain detection, compilation, linking, execution, process output, long file operations, or other noticeable work.
- Recoverable desktop failures must be contained. Build/run, toolchain detection, process execution, folder opening, command-state refresh, and logging failures should report a concise user-visible message, record diagnostics when possible, and keep the IDE open.
- Generated target code should be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write.
- Generated code must be minimal, readable, deterministic, educational, dependency-light, and fast without sacrificing clarity.
- Prefer one natural destination-language statement for one SMILE statement when that keeps the generated code clear. Target-local lowering must preserve behavior without exposing compiler mechanics as awkward target code.

## Official Language Specifications

- The official SET syntax is defined by `docs/SMILE Language Specification/001 - SMILE - SET Statement Official Specification v1.0.md`.
- The official PRINT syntax is defined by `docs/SMILE Language Specification/002 - SMILE - PRINT Statement Official Specification v1.0.md`.
- Official string literal behavior is defined by `docs/SMILE Language Specification/003 - SMILE - String Literals Official Specification v1.0.md`.
- Official core type and expression behavior is defined by `docs/SMILE Language Specification/004 - SMILE - Core Types and Expressions Official Specification v1.0.md`.
- The official LET syntax is defined by `docs/SMILE Language Specification/005 - SMILE - LET Statement Official Specification v1.0.md`.
- The official IF syntax is defined by `docs/SMILE Language Specification/006 - SMILE - IF Statement Official Specification v1.0.md`.
- Official full-line comment and source-layout behavior is defined by `docs/SMILE Language Specification/007 - SMILE - Full-Line Comments and Source Layout Preservation Official Specification v1.0.md`.
- The official INPUT syntax is defined by `docs/SMILE Language Specification/008 - SMILE - INPUT Statement Official Specification v1.0.md`.
- LET declares and initializes a variable. SET changes an existing variable from a SMILE expression. INPUT changes an existing variable from one runtime input line. SET and INPUT never change the type established by LET.
- INPUT is a statement, not an expression. It accepts exactly one existing identifier, is permitted at top level and in every IF-related body, and has no built-in prompt, multiple-target, declaration, or retry form in v1.0.
- Each executed INPUT consumes one CRLF-, LF-, CR-, or final-EOF-terminated logical line. Redirected input is strict UTF-8, and the shared maximum is 4096 UTF-8 bytes after removing the terminator and before Integer or Boolean trimming.
- String INPUT preserves the complete line, including spaces, tabs, an empty line, Unicode, and embedded NUL. Integer and Boolean INPUT trim only ASCII spaces and tabs; Integer accepts strict signed decimal Int64 text, and Boolean accepts only TRUE or FALSE ordinal case-insensitively.
- After INPUT, the target type remains known but its value is runtime-unknown. String analysis must allow 0 through 4096 UTF-8 bytes and NUL, Integer analysis must allow the full signed 64-bit range, and Boolean analysis must allow both values.
- Runtime-dependent Integer arithmetic is checked in the evaluator and every target. Reached overflow and division failures use `SMILER1206` and `SMILER1207`; source-known definitely evaluated failures retain compile diagnostics `SMILE1206` and `SMILE1207`.
- The evaluator receives input through an injectable source. Automated target tests provide scripted stdin and compare exact stdout, stderr, and exit code; normal CLI and Desktop execution must show prompts live and accept interactive input without blocking the WPF UI thread.
- Current runtime state belongs to the evaluator environment, not permanently to `BoundLetStatement`.
- Compile-time propagation must be statement-order, mutation aware, and branch aware. Never reuse an old known value after SET or INPUT, or propagate a branch-specific value unless every outgoing path merges to the same known value.
- Every expression feature must be defined once in the official core expression specification and implemented through the shared lexer, parser, binder, evaluator, and bound tree.
- SMILE `AND` and `OR` use left-to-right short-circuit evaluation. Binding and type checking still examine both operands, but evaluation-time failures in an unreachable operand are not produced.
- Each expression concept must have one canonical syntax and bound representation. Remove obsolete parallel representations rather than maintaining duplicate compiler paths.
- Target generators must consume the shared bound tree and must not invent expression semantics or reparse SMILE source text.
- Cross-target runtime tests for expression features must compare generated output to the `SmileEvaluator` reference evaluator whenever the target toolchain is locally runnable.
- PRINT parsing must be deterministic and must never guess whether bare text is a variable.
- Bare PRINT text is literal template text; expressions require braces.
- Ordinary quoted strings do not interpolate; `$"..."` and raw templates do.
- Target generators should preserve expression intent when a destination language has a clear idiomatic equivalent: interpolation should remain interpolation, explicit concatenation should remain concatenation, and lower-level targets may use equivalent output forms.
- Lower-level targets should still choose the clearest conventional form available. For NUL-free C and Objective-C `PRINT`, prefer one safe `printf` call with a compiler-generated format string; exact length-aware output may use several small statements when required.
- Destination-language String representations must preserve the complete SMILE String value, including embedded NUL characters. C-family `%s` and `strcmp` may be used only when they are semantically valid for the complete value.
- Shared short-circuit simplification must use the current known values at each statement position and apply to every expression position. Binding still validates both operands before simplification, and an unreachable right operand must not be simplified or evaluated.
- Exact-byte conformance tests must not trim or discard NUL, backspace, form-feed, carriage-return, or tab characters.
- A physical source line normally contains one statement, and semicolons do not separate statements.
- A second standalone `PRINT` keyword on the same line is a compiler error.
- `REM`, `//`, `#`, and `--` are equivalent full-line comment markers only at the first non-space-or-tab position. `REM` is ordinal case-insensitive, requires a space, tab, line ending, or EOF boundary, and remains a valid identifier elsewhere.
- Inline and trailing comments are not SMILE syntax. Marker-looking text after PRINT, inside an ordinary or interpolated String, or inside a SET Block String Literal remains data.
- Program and branch bodies retain comments and blank physical lines as ordered non-semantic source items. Semantic passes must enumerate statements without assigning layout items ordinals, values, mutations, diagnostics, or execution-trace entries.
- The SET Block String scanner owns all content through its closing delimiter. Comment classification, syntax highlighting, and IF recovery must never reinterpret marker-looking block content.
- Every target emits preserved comments once in its primary user-code region using native syntax: `//` for C#, C, C++, JavaScript, Java, Objective-C, and Swift; `#` for Python; `*>` for COBOL; and `;` for MASM x64.
- Preserved comment payloads must be deterministic and target-safe. Escape unsafe controls and line separators, protect C-family trailing backslashes and Java Unicode escapes, wrap COBOL when needed, and keep Python comments inside the generated user body.
- Every source-authored blank line outside a Block String remains one physically empty target line at the corresponding source-order boundary. Generator formatting may add lines but must not erase or coalesce source layout.
- Preserved comments are copied into generated source. Documentation and examples must warn learners not to place secrets, passwords, keys, or tokens in comments.
- Low-level targets may lower a provably known SET value, but they must emit an actual target storage update at the SET position.
- Direct SMILE self-assignment is valid and must remain a real generated assignment. For destinations that reject or warn about `target = target`, use the smallest type-preserving identity expression.
- Direct variable PRINT should read the generated target variable's current storage. Do not replace a direct variable read with an unrelated compiler-time literal when the target can represent the read clearly.
- C and Objective-C mutable Strings that require exact byte semantics must keep their pointer and logical length synchronized across LET and every SET.
- COBOL direct mutable String output must read current storage and current logical length, including the exact empty-String path.
- Generated-target compiler warnings are separate from SMILE solution warnings. Strict release validation must inspect generated compiler output where supported.
- IF conditions are call-free. Every value used by a condition must already exist without invoking a function or procedure during condition evaluation.
- Every atomic IF condition must contain an explicit comparison and right-hand operand. Standalone Boolean variables and literals are invalid.
- ELSE IF consists of ELSE and IF on the same logical header line. An IF after a standalone ELSE line is nested and requires its own END IF.
- IF v1.0 permits PRINT, SET, INPUT, nested IF, blank lines, and SET Block String Literals in branches. LET is not permitted until scopes are formally introduced.
- Every target must preserve genuine branch structure. Do not delete unselected source branches merely because current values are known.
- Branch-aware known-value analysis may propagate a value after IF only when outgoing-path merge proves it known.
- The maximum supported IF nesting depth is 128. Entering depth 129 must report `SMILE1416` at that IF keyword and recover without recursing into the over-limit body or interpreting SET Block String content as control-flow headers.
- A SET Block String Literal is a SET-only complete-value source form. Its delimiter lines are excluded, content-line boundaries become logical line feeds, and the closing delimiter's indentation margin is removed from matching content lines.
- Source tooling must not trim trailing spaces or tabs because block String content may depend on them.
- Block String normalization belongs entirely to the front end. Target generators receive only the normalized ordinary String value.
- New language work must preserve asynchronous debounced WPF live transpilation.
- Every visible target pane is an independent editable build unit. Global visible-pane builds remain sequential in pane order and must not collapse duplicate target-language selections.
- Startup and live generation must capture each receiving pane's selected language and user-edit revision. An older result may update the generated cache but must not overwrite a pane edited after that request began; unaffected and same-language sibling panes must still update.
- Later SMILE source edits, same-pane language changes, explicit Transpile All, and New remain authoritative over earlier target edits. A target edit made after one of those pending generation requests becomes the newest action and must survive that older result.
- A target-pane title `*` means the current primary source differs from generated SMILE output, not that a separate file is unsaved. Save, build, toolchain refresh, unrelated pane operations, and Maximize/Restore preserve it; authoritative generated replacement, same-pane language switching, and New clear it.
- Published official language specifications and compiler behavior must remain synchronized.
- Every normative valid and invalid example in an official language specification should be represented in the conformance test suite.
- Target generators must use a symbol-based target identifier map and must not emit raw SMILE identifiers when they conflict with destination-language syntax or generator-owned runtime names.
- Every valid SMILE identifier must be mapped to valid, collision-safe destination identifiers in every target. Target restrictions include exact keywords, contextual/restricted identifiers, generator-owned names, and reserved identifier patterns.
- SMILE `Integer` is a signed 64-bit semantic type, but generated storage must use one idiomatic per-program target profile that preserves every bound Integer literal, value, operand, and intermediate result across every IF branch.
- Ordinary small programs use C/Objective-C `int`, C#/Java `int`, JavaScript `Number`, Swift `Int`, and Python `int` without unnecessary wide literal suffixes. Promote only when required: C/Objective-C `int64_t`, C#/Java `long`, JavaScript `BigInt`, and Swift `Int64`.
- Pure bound-expression simplification is shared by every target. Keep Boolean identities target-independent, do not duplicate simplification logic in individual generators, and never simplify away an IF clause or body.
- C++ is the tenth and final planned destination language. After it is implemented, do not add or recommend another target language unless Sin explicitly reopens target expansion.
- C++ generation must use idiomatic C++ facilities such as `std::string`, `std::cout`, native value equality, and RAII ownership. Do not emit C-style `printf`, `strcmp`, or raw `char *` code merely because the C target already exists.
- C++ String generation must preserve embedded NUL bytes through length-aware `std::string` construction.
- C, Objective-C, and C++ target identifier maps must protect the standard fixed-width Integer and limit macro family whenever those names could be active in generated translation units.
- C++ identifiers containing a double underscore anywhere are implementation-reserved; the mapped target spelling itself must not retain a double underscore.
- C++ headers must be emitted according to the facilities used by generated code, not merely according to broad SMILE expression categories.
- Rust, Zig, and Go are intentionally deferred destination languages. Do not add them to target metadata, generators, toolchains, active roadmap milestones, or desktop selectors unless Sin explicitly reactivates one of them.
- Keep `Parser.cs` focused on parsing and `Binder.cs` focused on binding. File-organization refactors must preserve their existing syntax-tree boundary, diagnostics, spans, and behavior.
- Keep `Generation.cs` as the small public generation facade, shared generator helpers under `src/SMILE.Engine/Generation`, and each destination generator in its own focused file. Do not duplicate shared logic or change generated output merely because code moves between files.
- `.github/workflows/smile-ci.yml` is the normal hosted Windows solution check. It does not replace strict local release validation with Java, all ten target toolchains, and zero generated compiler warnings required through `SMILE_REQUIRE_JAVA`, `SMILE_REQUIRE_ALL_TARGETS`, and `SMILE_REQUIRE_ZERO_TARGET_WARNINGS`.

## Educational Code Comments

- SMILE is also a learning project for compilers, interpreters, transpilers, and local toolchains.
- Be generous with inline comments where they explain compiler concepts, parsing decisions, syntax-tree boundaries, target generation choices, process execution, cancellation, and UI responsiveness.
- Comments should teach the "why" and the flow of the system. Avoid comments that merely repeat an obvious line of code.

## Build Artifact Cleanup

- Codex has permission to permanently delete SMILE-owned build and output artifacts older than 1 day when they are discovered.
- This permission applies only to known generated artifact locations such as `bin`, `obj`, `out`, and `%TEMP%\SMILE\Runs`.
- Before deleting recursively, verify resolved absolute paths are inside the SMILE repository or the SMILE-owned temporary root. Never delete outside those roots.

## Living Documentation

- `README.md` is the living source of truth for SMILE's current mission, principles, features, supported syntax, target languages, toolchain requirements, setup steps, UI behavior, limitations, and roadmap.
- `examples/language.smile` is the cumulative Desktop language reference and must grow with the language. Preserve earlier valid LET, PRINT, SET, and future syntax demonstrations; append new canonical forms and mixed scenarios instead of replacing or shrinking prior teaching coverage.
- Package `language.smile` beside the Desktop executable for build and deployment. The Desktop must finish its first paint before asynchronously loading the language reference and transpiling only the visible target languages.
- Every feature, command, target language, toolchain, UI behavior, build/run behavior, prerequisite, architecture change, renamed path, changed limitation, or changed generated output must update `README.md` in the same commit.
- Documentation must describe the code that actually exists after the change. Never present a roadmap item as implemented.
- When a change genuinely requires no README update, explain why in the commit or pull-request summary.

## Versioning

- When adding a new SMILE language keyword or reaching an important project milestone, Codex may increment the SMILE build/version number as appropriate.
- Keep About SMILE and documented version references aligned with the project file version.
