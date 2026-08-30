# SMILE Core BASIC 2 Parity Report

## Result

SMILE 1.0 Core BASIC 2 parity is pinned to the read-only authoritative `Sincioco/SMILE-2.0` commit `9aa9583a651eab452ea3af80772b08b68fc03220`. Three unchanged Profile 2 programs compile in both repositories and produce the same recorded standard output. The retained Profile 1 corpus separately protects every earlier canonical subset program and the intentional rejection corpus.

## Reproducible corpora

| Corpus | Location | Positive programs | Purpose |
|---|---|---:|---|
| Profile 1 | `tests/CoreBasicParity` | 2 | canonical subset, counter semantics, and rejected historical source forms |
| Profile 2 | `tests/CoreBasic2Parity` | 3 | routines/arrays/Select, ByVal/scope, and recursion |

`profile.json` in each directory pins the authority SHA and the SHA-256 of every source and expected-output file. Profile 2 records these source hashes:

| Fixture | Source SHA-256 | Expected stdout SHA-256 |
|---|---|---|
| `canonical` | `508cd6032e712f3497872476583cda058505bde747670dcb3c541b4dd0d89249` | `8fefdcd14242d1fc4d24de5d4460f38259b8c1b24c87de8584930a02f1985dc3` |
| `byval-scope` | `b6dc161edc8803b4970d2f0d80f47d4f27132c68de1b0d71e880134103869985` | `b15600d5ed3d5d0b6e2b8d307327594009c93947f138dd83d528cbd6a218bd07` |
| `recursion` | `2e54f90ba52a417fe21a8107e111152b4eb4b6463eca2fc6af40d8b9cc1c37f4` | `1d7c601de16f9dca5c3da6174464e71184414ae979e425203079e30b1c9f4ba7` |

The three Profile 2 `.smile` files are byte-identical to their public example counterparts.

## Verification method

`scripts/Test-CoreBasicParity.ps1` performs the gate:

1. Resolve the SMILE 2.0 checkout and require the pinned commit.
2. Require a clean authority working tree.
3. Verify manifest hashes.
4. Bind and evaluate each fixture in SMILE 1.0.
5. Generate/build/run the SMILE 1.0 Windows x64 MASM destination.
6. compile and run the same source bytes through the SMILE 2.0 Windows x64 compiler;
7. normalize only physical output newlines and compare exact stdout;
8. verify the authority SHA and clean state again.

All executable output is created beneath the system temporary directory. The test never writes into SMILE 2.0.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-CoreBasicParity.ps1
```

## Authority findings reflected in Profile 2

- Boolean output is `True` and `False`.
- Calls and ordinary expression operands evaluate once from left to right; `And` and `Or` short-circuit.
- Parameters are ByVal with or without the optional keyword and receive fresh recursive frames.
- A Function must return on every reachable normal path.
- Select evaluates its selector once and uses exact typed constant cases in source order.
- The first Case immediately follows the Select header in shared fixtures because the pinned authority rejects an intervening blank source item.
- Arrays are fixed, one-dimensional, zero-based, default initialized, and checked.

## Scope restriction

Parity applies to the frozen Core BASIC 2 profile, not every feature in SMILE 2.0. Console Input, ByRef, Optional/named parameters, dynamic or multidimensional arrays, modules, user-defined/object types, files/data, and multimedia remain excluded. SMILE 2.0 remains authoritative whenever shared language behavior differs.
