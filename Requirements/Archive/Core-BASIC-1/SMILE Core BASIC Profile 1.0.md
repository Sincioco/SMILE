# SMILE Core BASIC Profile 1.0

## Frozen provenance

| Field | Value |
|---|---|
| Profile | SMILE Core BASIC Profile 1.0 (`Core BASIC 1`) |
| Authoritative repository | `Sincioco/SMILE-2.0` |
| Read-only local authority | `D:\SMILE 2.0` |
| Frozen commit | `ec61dfa6324de7b22ea5ca0959828ff40e5e3902` |
| Shared parity fixture | `tests/CoreBasicParity/canonical.smile` |
| Expected stdout | `tests/CoreBasicParity/canonical.stdout` |
| Machine-readable manifest | `tests/CoreBasicParity/profile.json` |
| Manifest SHA-256 | `e6e91989e8af9acca2ec087d528ac07ead902a3a4d2b4ef677fdb1bfd3a9224e` |

The source file is unchanged between compiler invocations. Verification checks the authority commit and empty worktree both before and after. All compilation products are written beneath the system temporary directory.

## Included lexical rules

- case-insensitive keywords and identifiers;
- Unicode letter/underscore identifier start and Unicode letter/digit/underscore continuation;
- CRLF, LF, and CR line endings;
- apostrophe inline or full-line comments;
- unsigned decimal literal tokens constrained to the signed 64-bit range;
- double-quoted Text with doubled quotes; physical Text newlines normalized to LF;
- parentheses, arithmetic/comparison operators, and Print semicolons.

## Included semantics

- Number, Boolean, and Text scalar types with exact assignment typing;
- direct assignment with implicit first declaration;
- explicit `Dim ... As Number|Boolean|Text` defaults;
- program-level compile-time `Const`, forward references, and cycle rejection;
- expression-list `Print`, blank Print, and trailing-semicolon newline suppression;
- `If / Else If / Else / End If`;
- inclusive `For ... To` and `For ... Down To`, bounds evaluated once, with the authoritative post-loop counter value;
- post-tested `Do / Loop [Until]`;
- lexically typed `Exit For` and `Exit Do`;
- `End Program`;
- unary `-` and `Not`; `*`, `/`, `Mod`, `+`, `-`; comparisons; equality; short-circuit `And` and `Or`.

## Deliberately excluded

The profile excludes the larger SMILE 2.0 module/procedure/function, array, I/O, graphics, audio, data/file, timer, object/type, import, and `Option Explicit` features. Their language words remain reserved.

Research-era SMILE 1.0 forms are not included. They are rejected rather than translated implicitly. See the migration guide for the unsupported spellings and explicit canonical replacements.

## Public surface rule

`SmileTranspiler`, `SmileEvaluator`, CLI, Desktop, highlighting, examples, and tests expose only this profile. No public or hidden selector is permitted. Adding a second parser attempt after a diagnostic would violate the profile even if no UI exposed it.

## Verification

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-CoreBasicParity.ps1
```

The executable test compares evaluator output, SMILE 1.0 generated Windows x64 MASM output, and authoritative SMILE 2.0 Windows x64 output against the same recorded bytes after newline normalization.
