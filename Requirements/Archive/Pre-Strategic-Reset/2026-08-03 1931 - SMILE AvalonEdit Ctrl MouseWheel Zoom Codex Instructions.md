# SMILE AvalonEdit Ctrl + Mouse Wheel Zoom
## Codex Implementation Instructions

**Repository:** `Sincioco/SMILE`
**Primary project:** `src/SMILE.Desktop`
**Feature type:** Small WPF/AvalonEdit editor usability enhancement
**Prepared:** August 3, 2026

---

# 1. Objective

Add familiar editor zoom behavior to every AvalonEdit-based code pane in the SMILE desktop application:

- **Ctrl + mouse wheel up** makes the code text larger.
- **Ctrl + mouse wheel down** makes the code text smaller.
- The ordinary mouse wheel, without Ctrl, continues to scroll normally.

Implement the feature once in the existing reusable `SmileCodeEditor` control so it automatically applies to:

1. The editable SMILE source pane.
2. Generated target pane 1.
3. Generated target pane 2.
4. Generated target pane 3.

Do **not** add this behavior to the ordinary `Diagnostics, Build, and Program Output` WPF `TextBox`.

This is an editor usability feature only. It must not change SMILE language behavior, parsing, transpilation, generated code, build/run behavior, or document contents.

---

# 2. Read Before Editing

Before making changes:

1. Read `AGENTS.md` completely.
2. Read the current `README.md`.
3. Inspect the working tree:

```bat
cmd /c cd /d C:\SMILE && git status --short
```

4. Inspect the current versions of:

```text
src/SMILE.Desktop/Controls/SmileCodeEditor.cs
src/SMILE.Desktop/MainWindow.xaml
src/SMILE.Desktop/SMILE.Desktop.csproj
README.md
tests/SMILE.Tests/SMILE.Tests.csproj
```

5. Preserve all unrelated user changes.
6. Do not reset, discard, overwrite, or broadly reformat unrelated files.
7. Do not create a feature branch. SMILE development currently occurs directly on `main`.
8. Do not commit or push unless Sin separately and explicitly asks for that in the active Codex session.

The checked-out repository is the final source of truth. Integrate with the code that exists when this task is performed.

---

# 3. Current Architecture to Preserve

At the time these instructions were prepared, SMILE already has this reusable control:

```text
src/SMILE.Desktop/Controls/SmileCodeEditor.cs
```

It derives directly from AvalonEdit:

```csharp
public sealed class SmileCodeEditor : TextEditor
```

The control currently owns common editor defaults such as:

- `Consolas`
- Font size `14`
- Line numbers
- Scrollbars
- Tab behavior
- Bindable document text
- Syntax highlighting

`MainWindow.xaml` uses `SmileCodeEditor` for the editable SMILE source and the three generated-code panes. Therefore, the zoom behavior belongs in `SmileCodeEditor`, not in four separate XAML event handlers.

Preserve the existing:

- `DocumentText` dependency property.
- `LanguageId` dependency property.
- Text synchronization guard flags.
- Caret-preservation logic.
- Undo-history protection.
- Syntax-highlighting behavior.
- Read-only generated-pane behavior.
- Debounced live transpilation.
- Source and generated text bindings.

---

# 4. Required User Behavior

Implement these exact rules:

| User action | Required behavior |
|---|---|
| Mouse wheel without Ctrl | Scroll the editor normally |
| Ctrl + wheel up | Increase the hovered editor's font size by `1.0` point |
| Ctrl + wheel down | Decrease the hovered editor's font size by `1.0` point |
| Ctrl + wheel up at maximum | Keep the maximum size; do not scroll |
| Ctrl + wheel down at minimum | Keep the minimum size; do not scroll |

Use these limits:

```text
Default font size: 14.0
Minimum font size: 8.0
Maximum font size: 48.0
Zoom step: 1.0
```

Additional requirements:

- Zoom only the `SmileCodeEditor` currently under the mouse pointer.
- Each code pane may retain its own in-memory font size.
- The editable source pane and read-only generated panes must both support zoom.
- Do not change the source text or generated text.
- Do not move the caret intentionally.
- Do not clear or damage the undo/redo history.
- Do not trigger document text synchronization merely because the font size changed.
- Do not trigger live transpilation merely because the font size changed.
- Do not make the application window itself larger or smaller.
- Do not zoom the diagnostics/output `TextBox`.
- Do not persist zoom between application launches in this task.
- Do not add a zoom percentage display, slider, menu item, status-bar control, or settings page.
- Do not add Ctrl+Plus, Ctrl+Minus, or Ctrl+0 in this task.

---

# 5. Preferred Implementation

Modify:

```text
src/SMILE.Desktop/Controls/SmileCodeEditor.cs
```

Add the WPF input namespace if it is not already available:

```csharp
using System.Windows.Input;
```

Add small, clearly named constants inside `SmileCodeEditor`:

```csharp
private const double DefaultEditorFontSize = 14.0;
private const double MinimumEditorFontSize = 8.0;
private const double MaximumEditorFontSize = 48.0;
private const double EditorZoomStep = 1.0;
```

Replace the constructor's literal font-size assignment:

```csharp
FontSize = 14;
```

with:

```csharp
FontSize = DefaultEditorFontSize;
```

Override WPF's preview mouse-wheel handler in the reusable control.

Preferred implementation shape:

```csharp
protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
{
    bool isControlPressed =
        (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

    if (!isControlPressed || e.Delta == 0)
    {
        base.OnPreviewMouseWheel(e);
        return;
    }

    double zoomAdjustment = e.Delta > 0
        ? EditorZoomStep
        : -EditorZoomStep;

    double newFontSize = Math.Clamp(
        FontSize + zoomAdjustment,
        MinimumEditorFontSize,
        MaximumEditorFontSize);

    SetCurrentValue(FontSizeProperty, newFontSize);

    // Ctrl + mouse wheel is zoom, so AvalonEdit must not also scroll.
    e.Handled = true;
}
```

Minor naming changes are acceptable when they match the existing source style, but preserve the behavior and simplicity above.

## Why `PreviewMouseWheel`

Use the preview/tunneling event so the reusable outer `SmileCodeEditor` can intercept Ctrl + wheel before AvalonEdit's internal scrolling elements process it.

For normal wheel input, call:

```csharp
base.OnPreviewMouseWheel(e);
```

For Ctrl + wheel input:

1. Calculate the new font size.
2. Clamp it to the required range.
3. apply it.
4. Set `e.Handled = true`.
5. Do not call the base implementation for that handled zoom gesture.

At the minimum or maximum size, still mark Ctrl + wheel as handled. Reaching a limit must not suddenly make the document scroll.

## Why `FontSize`

Change AvalonEdit's `FontSize`. Do not use:

- `ScaleTransform`
- `LayoutTransform`
- Render scaling
- DPI manipulation
- Canvas scaling
- Window scaling

A transform can also scale controls, scrollbars, margins, or line numbers and may make text blurry. The requested feature is code-font resizing, not visual scaling of the entire editor control.

## Why `SetCurrentValue`

Using:

```csharp
SetCurrentValue(FontSizeProperty, newFontSize);
```

updates the dependency property without unnecessarily replacing a possible style or binding source. A direct `FontSize = newFontSize` assignment would also be simple, but `SetCurrentValue` is preferred in this reusable WPF control.

---

# 6. Do Not Add XAML Event Handlers

Do not add separate handlers such as:

```xml
PreviewMouseWheel="..."
```

to every `SmileCodeEditor` declaration in `MainWindow.xaml`.

The feature belongs in the reusable custom control. `MainWindow.xaml` should require no change unless the current checked-out architecture has materially changed.

Do not add code-behind handlers to `MainWindow.xaml.cs` for this feature.

---

# 7. No New Dependencies or Architecture

Do not add or replace any packages.

Continue using the AvalonEdit package already present in the desktop project.

Do not introduce:

- A zoom service.
- A zoom view model.
- A command framework.
- An attached behavior.
- A new user control.
- A custom scroll viewer.
- A new editor abstraction.
- A settings subsystem.
- A third-party input package.
- Global static mutable zoom state.

This feature should remain a small addition to the existing `SmileCodeEditor`.

Follow KISS and KISS v2, “The Sin Way.”

---

# 8. Documentation Update

`AGENTS.md` identifies `README.md` as SMILE's living documentation and requires UI behavior changes to be documented.

Update the appropriate editor/features or usage section of `README.md` with a concise statement similar to:

```text
Hold Ctrl and rotate the mouse wheel over any code pane to increase or decrease that pane's editor font size. Normal mouse-wheel scrolling is unchanged.
```

The documentation may mention the `8`-through-`48` point range if that fits naturally.

Do not present zoom persistence, keyboard zoom shortcuts, or a zoom indicator as implemented because they are outside this task.

Do not change the SMILE version number solely for this small editor enhancement unless the checked-out repository has a newer explicit versioning requirement that mandates it.

---

# 9. Automated Verification

After implementation, run:

```bat
cmd /c cd /d C:\SMILE && dotnet restore SMILE.sln
```

```bat
cmd /c cd /d C:\SMILE && dotnet build SMILE.sln -c Release --no-restore
```

```bat
cmd /c cd /d C:\SMILE && dotnet test SMILE.sln -c Release --no-build
```

All existing builds and tests must pass.

Do not create a large UI-test framework for this small feature.

Because synthesizing WPF keyboard-modifier state and mouse-wheel routing can make unit tests fragile, manual acceptance testing is required. Add an automated test only if the current test architecture already provides a clean, stable way to test this behavior without reflection, sleeps, Windows input injection, or production-code abstractions created solely for testing.

---

# 10. Manual Acceptance Tests

Launch the SMILE desktop application:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Desktop\SMILE.Desktop.csproj
```

Perform all of the following tests.

## 10.1 Editable SMILE source pane

1. Place the mouse pointer over the SMILE source editor.
2. Rotate the wheel without Ctrl.
3. Confirm the document scrolls normally.
4. Hold Ctrl and rotate the wheel upward.
5. Confirm the source text becomes larger.
6. Hold Ctrl and rotate the wheel downward.
7. Confirm the source text becomes smaller.
8. Confirm Ctrl + wheel does not also scroll the document.
9. Type new SMILE code after zooming.
10. Confirm the caret behaves normally.
11. Confirm live transpilation still occurs.
12. Press Ctrl+Z and Ctrl+Y.
13. Confirm undo and redo still work normally.
14. Confirm the text content and whitespace were not altered by zooming.

## 10.2 Generated-code panes

For each visible generated-code pane:

1. Place the mouse over that pane.
2. Use Ctrl + wheel up and down.
3. Confirm its code text zooms.
4. Confirm the pane remains read-only.
5. Confirm text can still be selected and copied.
6. Confirm normal wheel input still scrolls.
7. Confirm zooming one pane does not unexpectedly alter another pane's font size.

## 10.3 Minimum and maximum limits

1. Use Ctrl + wheel down repeatedly.
2. Confirm the font stops shrinking at `8.0`.
3. Continue rotating downward while Ctrl remains pressed.
4. Confirm the editor does not scroll because the minimum was reached.
5. Use Ctrl + wheel up repeatedly.
6. Confirm the font stops growing at `48.0`.
7. Continue rotating upward while Ctrl remains pressed.
8. Confirm the editor does not scroll because the maximum was reached.

The interface must remain responsive throughout this test.

## 10.4 Diagnostics/output pane

1. Place the mouse over `Diagnostics, Build, and Program Output`.
2. Hold Ctrl and rotate the wheel.
3. Confirm this ordinary `TextBox` does not receive the new AvalonEdit zoom behavior.

## 10.5 Regression checks

Confirm all of these still work:

- New
- Open
- Save
- Save As
- Transpile All
- Generated-language selection
- Copy generated code
- Save generated source
- Build & Run
- Cancel
- Line numbers
- Syntax highlighting
- Horizontal scrolling
- Vertical scrolling
- Text selection
- Source editing
- Source undo/redo

---

# 11. Acceptance Criteria

The task is complete only when all of the following are true:

- [ ] Ctrl + wheel up enlarges text in the hovered `SmileCodeEditor`.
- [ ] Ctrl + wheel down reduces text in the hovered `SmileCodeEditor`.
- [ ] Font size is clamped from `8.0` through `48.0`.
- [ ] Each gesture changes font size by `1.0` point.
- [ ] Normal wheel input still scrolls.
- [ ] Ctrl + wheel never also scrolls the AvalonEdit document.
- [ ] The editable source pane supports zoom.
- [ ] All three read-only generated panes support zoom.
- [ ] Zooming one pane does not unexpectedly zoom all panes.
- [ ] The diagnostics/output `TextBox` is unchanged.
- [ ] Source text and generated text are unchanged by zooming.
- [ ] Caret, selection, undo, redo, and copy behavior remain correct.
- [ ] Live transpilation is not triggered solely by a font-size change.
- [ ] No new package or unnecessary abstraction was added.
- [ ] `README.md` accurately documents the feature.
- [ ] The Release build succeeds.
- [ ] All existing automated tests pass.
- [ ] Manual acceptance tests pass.

---

# 12. Expected Change Scope

The expected production-code diff should be small.

Likely modified files:

```text
src/SMILE.Desktop/Controls/SmileCodeEditor.cs
README.md
```

Possibly modified test files only when a clean and stable existing WPF test pattern supports the feature.

Files that should normally remain unchanged:

```text
src/SMILE.Desktop/MainWindow.xaml
src/SMILE.Desktop/MainWindow.xaml.cs
src/SMILE.Desktop/MainWindowViewModel.cs
src/SMILE.Desktop/TargetPaneViewModel.cs
src/SMILE.Engine/*
src/SMILE.Toolchains/*
```

Do not modify parser, lexer, binder, evaluator, target generators, official language specifications, or compiler tests for this editor-only feature.

---

# 13. Final Codex Report

When finished, report:

1. The files changed.
2. The exact zoom behavior implemented.
3. The selected minimum, maximum, default, and step values.
4. Why the implementation was placed in `SmileCodeEditor`.
5. Whether `MainWindow.xaml` required any change.
6. The Release build result.
7. The automated test result, including passed/failed/skipped counts.
8. The manual acceptance-test result.
9. Any remaining limitation or concern.
10. Confirmation that no commit or push was performed unless Sin explicitly requested it.

Do not merely state that the feature works. Include the verification evidence.
