# Core BASIC Parity Report

## Scope

This report records SMILE 1.0's frozen implementation parity with the SMILE 2.0 BASIC Core language. SMILE 2.0 is the read-only language authority; no file, index entry, branch, or commit in that repository is changed by the verification.

## Authority and corpus

- Repository: `Sincioco/SMILE-2.0`
- Frozen commit: `ec61dfa6324de7b22ea5ca0959828ff40e5e3902`
- Unchanged positive sources: `tests/CoreBasicParity/canonical.smile` and `counter-semantics.smile`
- Expected outputs: their paired `.stdout` files
- Rejected earlier-source corpus: `tests/CoreBasicParity/rejected/*.smile`
- Deterministic manifest: `tests/CoreBasicParity/profile.json`
- Manifest SHA-256: `e6e91989e8af9acca2ec087d528ac07ead902a3a4d2b4ef677fdb1bfd3a9224e`

The shared sources cover constants, explicit defaults, implicit assignment, all three scalar types, ascending and descending For, nested If, expression-list Print, trailing-semicolon suppression, post-tested Do, typed Exit Do, Boolean conjunction, End Program, zero-iteration counters, and the authoritative counter value after normal completion. The manifest hashes 13 artifacts: two positive programs, two expected-output files, and nine negative programs.

## Verification path

`scripts/Test-CoreBasicParity.ps1`:

1. resolves and validates the SMILE 2.0 path;
2. requires the frozen commit and a clean worktree;
3. runs the `CoreBasicParity` test category;
4. verifies every manifest fixture hash;
5. evaluates both unchanged positive sources in SMILE 1.0;
6. generates, builds, and runs their Windows x64 MASM forms;
7. compiles and runs the unchanged files with the SMILE 2.0 compiler's Windows x64 target;
8. compares normalized stdout with the checked-in expected output and with each other;
9. rechecks the authority commit and clean worktree.

The wider milestone suite separately generates every active target and builds/runs every installed target against evaluator output.

## Recorded result

On 2026-08-30, both unchanged fixtures compiled successfully in both repositories and all four executables returned exit code 0. The primary fixture produced:

```text
Core "BASIC": Sin
Total=6
321
Parity OK
```

The counter-boundary fixture produced:

```text
3
0
2
1
```

The authoritative repository was at the frozen commit and clean both before and after the probe. The parity category passed 3/3. The milestone Core BASIC filter passed 30/30, including deterministic generation for all ten destinations. All ten detected local toolchains built and ran the expanded fixture with matching evaluator output and no compiler warnings. MissionGuardrail passed 3/3.

## Exclusions

Parity is intentionally limited to the frozen Core BASIC 1 profile. Passing the profile does not claim that SMILE 1.0 implements the SMILE 2.0 superset. Excluded reserved features must remain rejected. Earlier SMILE 1.0 research syntax must also remain rejected; matching its behavior is not a parity goal.
