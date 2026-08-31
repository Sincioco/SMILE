# COBOL Text-Game Rendering Fix Completion Report

Date: 2026-08-31

## Outcome

Complete. COBOL supports all three SMILE text games. The games were running, but the generator rendered every dynamic Text value with `FUNCTION TRIM(..., TRAILING)`. A game cell containing one space therefore printed zero characters, collapsing the playfield rows around the non-space symbols.

The COBOL generator now keeps a numeric logical length beside each mutable `PIC X(4096)` Text value. The length follows scalar and array assignments, routine parameters, function returns, expression temporaries, concatenation, comparison, and Text `Select Case`. Dynamic Print uses guarded COBOL reference modification and never trims meaningful spaces.

## Generated-code change

Affected SMILE features: dynamic Text assignment and Print, Text arrays, typed ByVal Text parameters, Text-returning functions, Text concatenation and equality, Text `Select Case`, and the three text-game renderers. Affected target: COBOL only.

Before, an interior Trail Runner row collapsed:

```text
#oo@#
```

The old generated operation was:

```cobol
DISPLAY FUNCTION TRIM(SMILE-RETURN-VALUE, TRAILING) WITH NO ADVANCING
```

After, the same 24-column row retains its cells:

```text
#   oo@                #
```

The generated operation is normal guarded COBOL reference modification:

```cobol
IF SMILE-RETURN-LENGTH > 0
    DISPLAY SMILE-RETURN-VALUE(1:SMILE-RETURN-LENGTH) WITH NO ADVANCING
END-IF
```

No custom runtime helper was added. COBOL's native `PIC X`, `COMP-5`, `MOVE`, `COMPUTE`, `IF`, reference modification, `DISPLAY`, `OCCURS`, and call linkage provide the implementation. The existing feature-gated C companion remains limited to Windows console/time/random primitives.

## Regression coverage

- A fast MissionGuardrail generation test rejects trailing trim and verifies length storage for scalars, arrays, parameters, and returns.
- A native GnuCOBOL test distinguishes `" "` from `""` and validates exact spaces through variables, arrays, function calls/returns, concatenation, equality, inequality, and Text `Select Case` without compiler warnings.
- A native Windows pseudo-console test builds, launches, drives, redraws, and exits Trail Runner, Lantern Maze, and Sky Foundry. It requires a full-width interior row containing spaces for each game.

Final passing commands:

```powershell
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Release --filter "TestCategory=CobolFocused|FullyQualifiedName~Cobol_preserves_the_logical_length" -nologo
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Debug --filter TestCategory=MissionGuardrail -nologo -p:OutDir=D:\SMILE\out\codex-debug\
dotnet test tests/SMILE.Tests/SMILE.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~CoreBasicGenerationTests|FullyQualifiedName~CoreBasic2ConformanceTests|FullyQualifiedName~TextGameFoundationTests" -nologo
```

The focused command passes 3/3 tests, including all three native games. The MissionGuardrail command passes 7/7 tests. The broader generation, Core BASIC 2 conformance, and Text-Game Foundation selection passes 84/84 tests. The alternate Debug output directory avoids overwriting assemblies held by a running SMILE Desktop instance.

## Files changed

- `src/SMILE.Engine/Generation/CoreBasicCobolWriter.cs`
- `tests/SMILE.Tests/TextGameFoundationTests.cs`
- `tests/SMILE.Tests/TextGameToolchainMatrixTests.cs`
- `tests/SMILE.Tests/TextGameInteractiveMatrixTests.cs`
- `README.md`
- `docs/Architecture.md`
- `docs/SMILE Target Code Generation Standard v1.0.md`
- `docs/SMILE Core BASIC 2.1 Text-Game Foundation Completion Report.md`
- `docs/COBOL Text-Game Rendering Fix Completion Report.md`

No files were deleted. SMILE 2.0 was not modified.

## Known target-native tradeoff

COBOL Text remains bounded by the established 4,096-byte generated field capacity. The parallel length fields and guarded moves add ceremony that native fixed-width COBOL storage cannot avoid while preserving empty Text and meaningful trailing or all-space Text as distinct values. Other targets are unchanged.
