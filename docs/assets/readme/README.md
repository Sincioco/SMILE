# README visuals

These assets document SMILE 1.0 at source commit `7be3bab`, captured on September 25, 2026. They are documentation assets, not build output.

| Asset | Source and method |
|---|---|
| `desktop-csharp-java-cpp.jpg` | Unaltered screenshot of the current Debug Desktop build, with `examples/core-basic.smile` open and C#, Java, and C++ selected. |
| `compiler-pipeline.svg` | Editable vector diagram based on `docs/Architecture.md`, the shared parser/binder/evaluator pipeline, and the target registry. |
| `lantern-maze.svg` | Board characters from `examples/text-maze-muncher.smile`, evaluated with scripted keys; cursor-positioned player and shadow characters are included. |
| `trail-runner.svg` | Board characters from `examples/text-snake.smile`, evaluated with scripted keys and random values. |
| `sky-foundry.svg` | Board characters from `examples/text-falling-blocks.smile`, evaluated with scripted keys and random values. |

The board images are **rendered evaluator diagrams, not terminal screenshots**. Their typography, framing, and colors are illustrative. The board and score characters come from the unmodified examples; the renderer applies each cursor move to an 80-column, 25-row character grid before selecting a completed frame. This preserves overlays rather than treating the evaluator's accumulated output as plain sequential lines.

The captures reuse the successful scenarios in `TextGameFoundationTests`:

- Trail Runner: Enter at 0 ms, Up at 20 ms, Escape at 1500 ms; random values `6, 6, 10, 4`; capture a completed frame with score 10 and length 4.
- Lantern Maze: Enter at 0 ms, D at 20 ms, Escape at 2000 ms; capture the last completed board with score 5.
- Sky Foundry: Enter at 0 ms, S every 20 ms through 400 ms, Up at 420 ms, Left at 440 ms, Escape at 500 ms; random values `2, 3`; capture the last completed board with score 119 and one cleared row.

The evaluator uses virtual time and a four-million-statement budget for each capture. The temporary capture and verification helpers are outside the repository; no runtime or compiler changes are needed to reproduce these scenarios using `SmileEvaluator` and `ISmileEvaluationHost`.

When refreshing the visuals, rebuild Desktop, verify the current examples, and capture new images. Keep actual screenshots distinct from diagrams, update this provenance, and use a new asset name when the content changes to avoid stale browser image caches. Do not include unrelated windows, personal paths, diagnostics containing private data, or generated executables.
