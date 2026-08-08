# SMILE Temporary Three-Target Focus — C#, C, and MASM x64

## Purpose

SMILE currently has ten implemented destination-language backends.

For the next phase of development, active target work must be narrowed to:

```text
C#
C
MASM x64 / Assembly
```

This is a **temporary development freeze** for the other targets.

It is not a permanent deletion.

---

# 1. Active targets

Until Sin explicitly changes this policy, the only active transpilation targets are:

```text
TargetLanguage.CSharp
TargetLanguage.C
TargetLanguage.MasmX64
```

These three receive:

- new language-feature support;
- generator fixes;
- readability work;
- toolchain work;
- active Desktop exposure;
- routine regression tests;
- Build & Run support;
- current documentation examples.

---

# 2. Paused targets

Pause active support for the existing other seven:

```text
JavaScript
Java
COBOL
Objective-C
Swift
Python
C++
```

The exact names should follow the current `TargetLanguage` enum.

"Paused" means:

- do not spend normal development time updating them for new features;
- do not include them in routine cross-target test matrices;
- do not require their toolchains for normal commits;
- do not show them as normal active choices in the Desktop/CLI;
- do not allow their maintenance burden to block C#/C/MASM progress.

---

# 3. Do not delete paused backends

Do not delete:

- generator source files;
- toolchain source files;
- historical tests;
- target identifier data;
- syntax highlighting definitions;
- documentation history.

Do not rewrite git history.

The goal is to make future re-enablement inexpensive.

---

# 4. Add one simple central active-target policy

Do not scatter checks throughout the codebase.

Add one small central source of truth equivalent to:

```csharp
public static class ActiveTargetLanguages
{
    public static readonly IReadOnlyList<TargetLanguage> All =
    [
        TargetLanguage.CSharp,
        TargetLanguage.C,
        TargetLanguage.MasmX64
    ];
}
```

Use a name that fits the current architecture.

KISS.

Do not create a plugin system or dynamic feature-flag framework.

This list should drive:

- Desktop target choices;
- CLI target enumeration;
- "transpile all" behavior;
- routine active-target tests;
- default toolchain detection;
- documentation of currently active targets.

---

# 5. Product behavior

The Desktop currently defaults its three visible panes to C#, MASM x64, and C.

Keep that as the active three-target experience.

Remove or hide paused languages from normal target selectors while paused.

If the CLI has commands that enumerate available targets, report only active targets by default.

If a user explicitly names a paused target, return a clear message such as:

```text
Python transpilation is temporarily paused in the current SMILE development phase.
```

Do not silently generate stale output.

---

# 6. Keep re-enablement easy

The central policy should make re-enabling a target a deliberate small change:

```text
1. add it back to the active list
2. update that backend for all language changes made while paused
3. run its focused tests
4. run the major-milestone/full validation gate
5. expose it in UI/CLI again
```

Document this process.

---

# 7. No new target languages during the freeze

Do not add, recommend, prototype, or scaffold another destination language unless Sin explicitly asks.

The engineering focus is SMILE itself plus the quality of C#, C, and Assembly output.

---

# 8. Language-feature development during the freeze

When implementing a new SMILE language feature:

- implement the front-end/compiler semantics normally;
- implement generation for C#, C, and MASM x64;
- add focused tests for those three;
- document that paused backends require catch-up before re-enablement.

Do not force every new feature task to update seven paused generators.

This is the main velocity benefit of the freeze.

---

# 9. Paused tests

Do not delete historical tests for paused targets merely because they are not part of routine validation.

Options:

- mark them with a category such as `PausedTarget`;
- organize them so routine test filters exclude them;
- keep major-milestone commands capable of running them when re-enablement work begins.

Do not let paused-target failures block ordinary work while those targets are intentionally unsupported.

---

# 10. Documentation

Update:

```text
AGENTS.md
README.md
docs/Roadmap.md
docs/Architecture.md
docs/Toolchains.md
```

Make the status explicit:

> **Current active targets: C#, C, MASM x64. Other existing target implementations are retained but temporarily paused.**

Do not describe the seven paused targets as deleted.

---

# 11. Permanent quality rule still applies to paused/future targets

The temporary target freeze does not change the permanent transpilation rule.

When any paused language is re-enabled later, it must satisfy:

> **Generate the normal, idiomatic, beginner-readable code for that destination language.**

Do not resurrect the old "force all languages through the same runtime behavior" approach.

---

# 12. Definition of done

This focus change is complete when:

- normal SMILE UI/CLI development involves only C#, C, and MASM;
- routine tests involve only those active targets plus shared compiler tests;
- paused backend code remains intact;
- no paused toolchain is required for routine work;
- one central list controls active targets;
- future re-enablement is documented and straightforward;
- no new target work occurs unless Sin explicitly changes the strategy.
