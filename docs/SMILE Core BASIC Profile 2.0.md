# SMILE Core BASIC Profile 2.0 (Preserved Subset Record)

## Provenance

| Field | Value |
|---|---|
| Profile | SMILE Core BASIC Profile 2.0 (`Core BASIC 2`) |
| Authority | `Sincioco/SMILE-2.0` |
| Verified authority commit | `b34f4c5284f9f636e17a62ce5b6e2721d53be464` |
| SMILE 1.0 status | valid preserved subset of the one current 2.1 grammar |
| Profile 2 parity corpus | `tests/CoreBasic2Parity` |
| Profile 1 regression corpus | `tests/CoreBasicParity` |

Profile 2 contains every valid Profile 1 program and adds Option Explicit, routines, typed ByVal scalar parameters, local scope and recursion, Select Case, and fixed one-dimensional arrays. The additive [Core BASIC 2.1 Text-Game Foundation specification](SMILE%20Language%20Specification/003%20-%20SMILE%20Core%20BASIC%202.1%20Text-Game%20Foundation%20Official%20Specification.md) is the one current complete specification; there is no runtime profile selector.

## Authority resolutions

The implementation was checked against the pinned compiler rather than inferred from older prose. The resulting observable rules include:

- Boolean output is `True` and `False`.
- Call arguments and ordinary expression operands evaluate once, left to right; Boolean `And` and `Or` short-circuit.
- Parameters are ByVal whether or not the optional `ByVal` word is present.
- Parameters and local storage are fresh for recursion; a local may shadow a global.
- Functions must return on every reachable normal path; loops alone do not prove a return.
- Select evaluates its selector once, uses exact-type compile-time cases, and executes the first match.
- The pinned compiler rejects a blank source item immediately between a Select header and its first Case; the shared canonical fixture therefore has none.
- Arrays are fixed, one-dimensional, zero-based, and checked before access.

## Shared fixtures

The manifest `tests/CoreBasic2Parity/profile.json` records SHA-256 for three unchanged source/stdout pairs:

- canonical routines, Select, and global arrays;
- ByVal isolation and shadowing;
- direct and mutual recursion.

Each source is also a public example. The parity test compiles the same bytes with both repositories, compares evaluator/native output, and verifies SMILE 2.0 is still pinned and clean before and after.

## Historical scope boundary

This preserved 2.0 corpus does not exercise the additive 2.1 terminal primitives or rank-two arrays. The current compiler does implement those 2.1 features. Console Input, ByRef, Optional/ParamArray/named arguments, array parameters or returns, dynamic or rank-three arrays, modules, imports, Types, Enums, classes, OOP, files/data, graphics, media, and audio remain excluded. No eleventh target is introduced.
