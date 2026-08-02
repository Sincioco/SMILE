# SMILE Desktop Syntax Highlighting
## Codex Implementation Instructions — AvalonEdit

**Repository:** `Sincioco/SMILE`  
**Primary project:** `src/SMILE.Desktop`  
**Implementation type:** WPF desktop user-interface enhancement  
**Prepared:** August 2, 2026

---

# 1. Objective

Replace the four plain WPF code `TextBox` controls in the SMILE desktop application with a reusable AvalonEdit-based editor that provides clear syntax highlighting and line numbers.

The four code panes are:

1. The editable SMILE source pane.
2. Generated target pane 1.
3. Generated target pane 2.
4. Generated target pane 3.

Do **not** replace the `Diagnostics, Build, and Program Output` `TextBox`. That pane is ordinary output text and should remain a normal read-only WPF `TextBox`.

The completed implementation must:

- Preserve all existing SMILE source-editing behavior.
- Preserve the existing 250-millisecond debounced live transpilation.
- Preserve New, Open, Save, Save As, Copy, Save Source, Transpile All, Build & Run, and Cancel behavior.
- Preserve exact source and generated text, including whitespace, tabs, blank lines, and trailing newlines.
- Add syntax highlighting for SMILE and every generated target language.
- Add line numbers to all four code panes.
- Keep generated panes read-only but selectable, scrollable, and copyable.
- Avoid caret jumps and undo-history corruption while typing.
- Follow `AGENTS.md`, KISS, and KISS v2, “The Sin Way.”

This task is syntax highlighting, not a redesign of the application and not a new SMILE-language feature.

---

# 2. Read Before Editing

Before making changes:

1. Read `AGENTS.md` completely.
2. Read the current `README.md`.
3. Inspect the current working tree with:

```bat
cmd /c cd /d C:\SMILE && git status --short
```

4. Inspect the current versions of at least:

```text
src/SMILE.Desktop/SMILE.Desktop.csproj
src/SMILE.Desktop/MainWindow.xaml
src/SMILE.Desktop/MainWindowViewModel.cs
src/SMILE.Desktop/TargetPaneViewModel.cs
src/SMILE.Engine/Language.cs
tests/SMILE.Tests/SMILE.Tests.csproj
tests/SMILE.Tests/DesktopCommandTests.cs
```

5. Preserve all unrelated user changes already in the working tree.
6. Do not reset, discard, overwrite, or reformat unrelated files.
7. Do not commit or push unless Sin separately and explicitly asks for that in the active Codex session.

The instructions below describe the current expected architecture, but the checked-out repository is the final source of truth. Integrate cleanly with the code that exists when this task is executed.

---

# 3. Current Baseline to Preserve

At the time these instructions were prepared:

- `SMILE.Desktop` is a WPF application targeting `net10.0-windows`.
- The SMILE source pane is bound to `MainWindowViewModel.SourceText`.
- `SourceText` schedules a 250-millisecond debounced live transpilation.
- There are three `TargetPaneViewModel` instances.
- Each generated pane is bound to `TargetPaneViewModel.GeneratedCode`.
- Each generated pane can switch among:
  - C#
  - C
  - Assembly — Windows x64 MASM
  - JavaScript
  - Java
  - Objective-C
  - Swift
- Target-language stable IDs already come from `TargetLanguageInfo.GetStableId(...)`.
- The output pane uses `OutputTextBox_TextChanged` to scroll to the latest output.

Do not weaken or bypass the existing view-model-driven architecture.

---

# 4. Approved Dependency

Use the official AvalonEdit NuGet package:

```xml
<PackageReference Include="AvalonEdit" Version="6.3.1.120" />
```

Package facts:

- Package ID: `AvalonEdit`
- Namespace: `ICSharpCode.AvalonEdit`
- License: MIT
- Compatible with `net10.0-windows`
- Official source: `icsharpcode/AvalonEdit`

Use the exact stable package above unless the checked-out repository already contains an approved newer official AvalonEdit version.

Do **not** add:

- Monaco
- WebView2
- Scintilla
- RoslynPad
- TextMateSharp
- Whipstaff wrappers
- A second editor framework
- A general-purpose UI framework
- A third-party MVVM framework

One editor dependency is enough.

If package restore fails, report the actual restore error. Do not silently replace AvalonEdit with an unofficial fork.

---

# 5. Required Architecture

Use this simple structure unless the current codebase presents a compelling reason for a minor naming adjustment:

```text
src/SMILE.Desktop/
  Controls/
    SmileCodeEditor.cs
  Highlighting/
    SyntaxHighlightingCatalog.cs
    SMILE.xshd
    MasmX64.xshd
    ObjectiveC.xshd
    Swift.xshd
```

A single C# custom control derived from AvalonEdit’s `TextEditor` is preferred over a `UserControl` containing another editor. It is the smallest complete WPF solution.

Recommended declaration:

```csharp
public sealed class SmileCodeEditor : ICSharpCode.AvalonEdit.TextEditor
```

The custom control should provide:

- A bindable `DocumentText` dependency property.
- A `LanguageId` dependency property.
- Existing AvalonEdit `IsReadOnly` support.
- Safe two-way text synchronization.
- Syntax-highlighting selection.
- Standard editor defaults used consistently by all four code panes.

Do not create a large editor framework or an unnecessary hierarchy of interfaces, services, factories, base classes, adapters, or view models.

---

# 6. Add AvalonEdit to the Desktop Project

Update:

```text
src/SMILE.Desktop/SMILE.Desktop.csproj
```

Add:

```xml
<ItemGroup>
  <PackageReference Include="AvalonEdit" Version="6.3.1.120" />
</ItemGroup>
```

Add the four custom `.xshd` files as deterministic embedded resources. Give each resource an explicit logical name so loading does not depend on default namespace or folder inference.

For example:

```xml
<ItemGroup>
  <EmbeddedResource Include="Highlighting\SMILE.xshd"
                    LogicalName="SMILE.Desktop.Highlighting.SMILE.xshd" />
  <EmbeddedResource Include="Highlighting\MasmX64.xshd"
                    LogicalName="SMILE.Desktop.Highlighting.MasmX64.xshd" />
  <EmbeddedResource Include="Highlighting\ObjectiveC.xshd"
                    LogicalName="SMILE.Desktop.Highlighting.ObjectiveC.xshd" />
  <EmbeddedResource Include="Highlighting\Swift.xshd"
                    LogicalName="SMILE.Desktop.Highlighting.Swift.xshd" />
</ItemGroup>
```

Avoid wildcard resource names if they make the manifest resource names uncertain.

Do not add generated package files, `bin`, `obj`, or temporary output to source control.

---

# 7. Implement `SmileCodeEditor`

Create:

```text
src/SMILE.Desktop/Controls/SmileCodeEditor.cs
```

Use the namespace:

```csharp
namespace SMILE.Desktop.Controls;
```

## 7.1 Bindable text property

AvalonEdit’s normal `Text` property is not a WPF dependency property suitable for the application’s existing binding pattern. Add a separate dependency property named:

```csharp
DocumentText
```

Its metadata must use:

```csharp
FrameworkPropertyMetadataOptions.BindsTwoWayByDefault
```

Conceptual shape:

```csharp
public static readonly DependencyProperty DocumentTextProperty =
    DependencyProperty.Register(
        nameof(DocumentText),
        typeof(string),
        typeof(SmileCodeEditor),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnDocumentTextChanged));

public string DocumentText
{
    get => (string)GetValue(DocumentTextProperty);
    set => SetValue(DocumentTextProperty, value);
}
```

Treat `null` as `string.Empty`.

## 7.2 Prevent feedback loops and caret jumps

This is a critical requirement.

A naive implementation can create this loop:

```text
User types
  -> AvalonEdit TextChanged
  -> view-model SourceText changes
  -> WPF binding writes the same entire text back
  -> AvalonEdit document is reassigned
  -> caret moves or undo history is damaged
```

Use two private guard flags with clear names, such as:

```csharp
private bool _isApplyingDocumentText;
private bool _isPublishingEditorText;
```

Required behavior:

### Editor to view model

When AvalonEdit raises `TextChanged`:

1. Return immediately when `_isApplyingDocumentText` is true.
2. Compare `DocumentText` and the editor’s current `Text` using ordinal equality.
3. Do nothing when the strings are already equal.
4. Set `_isPublishingEditorText`.
5. Update the dependency property with `SetCurrentValue(...)`, not plain `SetValue(...)`, so the existing binding is preserved.
6. Clear the flag in `finally`.

### View model to editor

When `DocumentText` changes:

1. Return immediately when `_isPublishingEditorText` is true.
2. Convert null to an empty string.
3. Compare the incoming value with the editor’s current `Text`.
4. Do not reassign the document when the text is already equal.
5. Set `_isApplyingDocumentText`.
6. Assign the incoming text.
7. Keep the caret offset within the new document length.
8. Clear the flag in `finally`.

The equality checks and guard flags are mandatory. Add a concise teaching comment explaining that they prevent a WPF binding echo from resetting the editor on every keystroke.

Ordinary typing must not reconstruct or replace the entire AvalonEdit document after each keypress.

## 7.3 Language dependency property

Add a dependency property:

```csharp
LanguageId
```

Recommended declaration:

```csharp
public static readonly DependencyProperty LanguageIdProperty =
    DependencyProperty.Register(
        nameof(LanguageId),
        typeof(string),
        typeof(SmileCodeEditor),
        new PropertyMetadata(string.Empty, OnLanguageIdChanged));
```

When it changes, assign:

```csharp
SyntaxHighlighting = SyntaxHighlightingCatalog.GetDefinition(LanguageId);
```

Unknown, null, or blank language IDs should safely produce plain text rather than crash the application.

## 7.4 Editor defaults

Apply consistent defaults in the constructor:

```text
Font family: Consolas
Font size: 14
Show line numbers: true
Word wrap: false
Horizontal scrollbar: Auto
Vertical scrollbar: Auto
Tabs accepted: yes
Convert tabs to spaces: false
Indentation size: 4
```

Use WPF/AvalonEdit properties rather than custom drawing.

Keep these normal editor behaviors working:

- Arrow keys
- Home and End
- Page Up and Page Down
- Ctrl+A
- Ctrl+C
- Ctrl+X in the editable pane
- Ctrl+V in the editable pane
- Ctrl+Z and Ctrl+Y in the editable pane
- Mouse selection
- Vertical and horizontal scrolling
- Tab insertion in the editable pane

Read-only generated panes must remain focusable and selectable so the user can copy code.

Do not introduce a minimap, autocomplete, code folding, search bar, theme selector, or diagnostic squiggles in this task.

---

# 8. Implement `SyntaxHighlightingCatalog`

Create:

```text
src/SMILE.Desktop/Highlighting/SyntaxHighlightingCatalog.cs
```

Recommended namespace:

```csharp
namespace SMILE.Desktop.Highlighting;
```

Use AvalonEdit types:

```csharp
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
```

Use `XmlReader` and `HighlightingLoader.Load(...)` for custom resources.

The catalog should expose a small API such as:

```csharp
public static IHighlightingDefinition? GetDefinition(string? languageId)
```

Requirements:

- Trim the input.
- Match IDs case-insensitively.
- Cache definitions so embedded XML is not parsed on every UI update.
- Be deterministic.
- Be thread-safe without unnecessary complexity.
- Return `null` for an unknown ID.
- Throw a clear `InvalidOperationException` when a known custom definition is missing or malformed. A packaging error should be obvious during tests instead of silently disabling highlighting.

A simple `Lazy<T>` or a case-insensitive dictionary of lazy definitions is sufficient.

## 8.1 Required mapping

Use the existing SMILE target stable IDs:

| Language ID | Highlighting source |
|---|---|
| `smile` | Custom `SMILE.xshd` |
| `csharp` | AvalonEdit built-in definition `"C#"` |
| `c` | AvalonEdit built-in definition `"C++"` |
| `masm-x64` | Custom `MasmX64.xshd` |
| `javascript` | AvalonEdit built-in definition `"JavaScript"` |
| `java` | AvalonEdit built-in definition `"Java"` |
| `objective-c` | Custom `ObjectiveC.xshd` |
| `swift` | Custom `Swift.xshd` |

AvalonEdit officially registers the built-in names `"C#"`, `"C++"`, `"JavaScript"`, and `"Java"`. Retrieve them through:

```csharp
HighlightingManager.Instance.GetDefinition(...)
```

Validate that a built-in definition is non-null. A known language must not silently lose highlighting.

## 8.2 Embedded resource loading

Load custom resources from the `SMILE.Desktop` assembly using their explicit logical names.

Conceptual example:

```csharp
private static IHighlightingDefinition LoadEmbedded(string resourceName)
{
    Assembly assembly = typeof(SyntaxHighlightingCatalog).Assembly;

    using Stream stream = assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException(
            $"Syntax-highlighting resource '{resourceName}' was not found.");

    using XmlReader reader = XmlReader.Create(stream);

    return HighlightingLoader.Load(
        reader,
        HighlightingManager.Instance);
}
```

Include the resource name in failure messages.

Do not access the network at runtime.

---

# 9. Create the Custom XSHD Definitions

Use the AvalonEdit XSHD v2 namespace:

```xml
http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008
```

Author the definitions specifically for SMILE. Do not copy unlicensed grammar files from random repositories.

Use a readable Visual Studio-like light palette consistently:

| Token | Suggested color |
|---|---|
| Keywords | `#0000FF`, bold |
| Strings | `#A31515` |
| Comments | `#008000` |
| Numbers | `#098658` |
| Preprocessor/directives | `#AF00DB` |
| Operators/braces | `#1F1F1F` or another clearly visible neutral |
| Registers/types | a readable teal or blue distinct from keywords |

Exact nearby accessible colors are acceptable, but text must remain readable on a white background.

## 9.1 `SMILE.xshd`

Current implemented SMILE keywords are only:

```text
LET
PRINT
```

Highlight them:

- Case-insensitively.
- As whole keywords, not as substrings inside identifiers.
- In blue and bold.

Also highlight:

- Ordinary quoted strings: `"..."`.
- Interpolated strings: `$"..."`.
- Operators used today, including `=` and `+`.
- Interpolation braces `{` and `}`.
- Numeric literals may receive a number color for forward compatibility, but do not add numeric language semantics.

Important SMILE-language rules:

- Do not add future keywords such as `IF`, `THEN`, `ELSE`, `INPUT`, `FOR`, or `WHILE`.
- Do not highlight apostrophes, `REM`, `//`, or `#` as SMILE comments. Comments are not currently part of the official SMILE language.
- Ordinary strings do not interpolate.
- Raw PRINT template text should remain normal text except for recognized interpolation braces and expressions.
- Highlighting is lexical presentation only. Do not alter parsing or evaluate text in the editor.

The following sample must look sensible:

```basic
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
PRINT Literal braces: {{Name}}
```

## 9.2 `MasmX64.xshd`

Provide practical highlighting for the generated Windows x64 MASM output:

- MASM instructions.
- Common directives.
- Registers.
- Numeric literals, including hexadecimal forms.
- String literals.
- Semicolon-to-end-of-line comments.
- Case-insensitive keywords.

Cover the instructions and directives actually emitted by the current SMILE MASM generator before adding broad speculative lists.

## 9.3 `ObjectiveC.xshd`

Provide practical highlighting for generated Objective-C:

- Objective-C directives and keywords such as `@autoreleasepool`.
- C-family keywords.
- Foundation-related type names used by the generator.
- `@"..."` strings and ordinary C strings.
- Character literals.
- Numeric literals.
- `//` and `/* ... */` comments.
- Preprocessor lines beginning with `#`.

## 9.4 `Swift.xshd`

Provide practical highlighting for generated Swift:

- Swift keywords used by the generator and common basic declarations.
- String literals.
- Numeric literals.
- `//` and `/* ... */` comments.
- Operators and interpolation punctuation.

Do not try to reproduce an entire production compiler grammar. The definitions should be small, readable, valid, and sufficient for SMILE’s generated educational examples.

---

# 10. Update `TargetPaneViewModel`

Update:

```text
src/SMILE.Desktop/TargetPaneViewModel.cs
```

Add a derived property:

```csharp
public string HighlightingId =>
    TargetLanguageInfo.GetStableId(Language);
```

When `SelectedLanguageOption` changes, also raise:

```csharp
OnPropertyChanged(nameof(HighlightingId));
```

Keep the existing notifications for:

```text
Language
Title
BuildButtonText
```

Do not duplicate the target-language mapping in the view model. Use `TargetLanguageInfo.GetStableId(...)`, which is already the canonical stable-ID source.

---

# 11. Replace Only the Four Code TextBoxes

Update:

```text
src/SMILE.Desktop/MainWindow.xaml
```

Add the control namespace, for example:

```xml
xmlns:controls="clr-namespace:SMILE.Desktop.Controls"
```

## 11.1 Editable SMILE source pane

Replace the source `TextBox` with:

```xml
<controls:SmileCodeEditor
    Grid.Row="1"
    DocumentText="{Binding SourceText,
                           Mode=TwoWay,
                           UpdateSourceTrigger=PropertyChanged}"
    LanguageId="smile"
    IsReadOnly="{Binding IsBusy}"
    AutomationProperties.Name="SMILE source editor" />
```

Keep its existing grid location and sizing.

## 11.2 Generated target panes

Inside the `TargetPaneViewModel` data template, replace the generated-code `TextBox` with:

```xml
<controls:SmileCodeEditor
    Grid.Row="2"
    DocumentText="{Binding GeneratedCode, Mode=OneWay}"
    LanguageId="{Binding HighlightingId}"
    IsReadOnly="True"
    AutomationProperties.Name="{Binding Title}" />
```

Generated code must remain one-way/read-only.

## 11.3 Output pane

Do not replace or alter the output `TextBox` except for a strictly necessary compile fix.

The following must remain intact:

```text
x:Name="OutputTextBox"
Text="{Binding OutputText}"
TextChanged="OutputTextBox_TextChanged"
```

## 11.4 Existing global `TextBox` style

The current global `TextBox` style can remain for the output box and any other ordinary text boxes.

Do not try to apply the `TextBox` style to AvalonEdit. Configure `SmileCodeEditor` directly.

---

# 12. Preserve Live Transpilation and View-Model Behavior

Do not change the existing 250-millisecond live-transpilation delay.

Do not move parsing, binding, or target generation onto the UI thread.

Do not add parsing work to `SmileCodeEditor`.

Syntax highlighting should be handled by AvalonEdit’s lexical highlighter and should not invoke the SMILE transpiler independently.

The flow must remain:

```text
User edits AvalonEdit
  -> DocumentText binding updates SourceText
  -> existing debounce schedules transpilation
  -> existing transpiler generates visible targets
  -> GeneratedCode updates
  -> read-only editors display the new text
```

The latest source revision must continue to win. Do not change cancellation or stale-result handling.

---

# 13. Automated Tests

Add focused tests, preferably in:

```text
tests/SMILE.Tests/SyntaxHighlightingTests.cs
```

At minimum, add the following coverage.

## 13.1 Every supported language resolves

Test:

```text
Syntax_highlighting_catalog_resolves_every_supported_language
```

Verify non-null definitions for:

```text
smile
csharp
c
masm-x64
javascript
java
objective-c
swift
```

This test also validates that every custom embedded XSHD file exists and parses successfully.

## 13.2 IDs are case-insensitive

Test examples such as:

```text
SMILE
CSharp
MASM-X64
Objective-C
```

They should resolve to the same definitions as their lowercase stable IDs.

## 13.3 Unknown IDs safely use plain text

Verify that null, blank, and an unknown value return `null` rather than throwing.

## 13.4 Target pane exposes the correct ID

Add or extend a `TargetPaneViewModel` test:

1. Create the pane with C#.
2. Assert `HighlightingId == "csharp"`.
3. Change it to Swift.
4. Assert `HighlightingId == "swift"`.
5. Confirm a `PropertyChanged` notification is raised for `HighlightingId`.

## 13.5 Existing tests remain unchanged in meaning

All existing language, generation, desktop command, and toolchain tests must still pass.

Do not add brittle pixel-comparison tests.

A WPF control-level synchronization test may be added only if it runs reliably on the existing MSTest setup and does not introduce a new UI-test framework. Manual caret/undo validation is still required.

---

# 14. Manual Smoke Test

Run the application:

```bat
cmd /c cd /d C:\SMILE && dotnet run --project src\SMILE.Desktop
```

Perform every check below.

## 14.1 Initial display

- The application opens maximized as before.
- The SMILE source pane shows line numbers.
- All three generated panes show line numbers.
- `LET` and `PRINT` are visibly highlighted.
- Strings have a distinct string color.
- Generated C#, MASM, and C have sensible syntax coloring.
- The output pane remains a normal plain-text box without line numbers.

## 14.2 Typing and caret stability

Place the caret in the middle of an existing source line and type at least ten characters one at a time.

Verify:

- The caret never jumps to the beginning or end unexpectedly.
- Text is not duplicated.
- Text is not lost.
- The visible target panes continue updating after the normal debounce.
- Ctrl+Z reverses the edits normally.
- Ctrl+Y reapplies them normally.

This is a release-blocking test.

## 14.3 Editing operations

Verify in the source editor:

- Select and replace text.
- Backspace.
- Delete.
- Enter.
- Tab.
- Ctrl+A.
- Ctrl+C.
- Ctrl+X.
- Ctrl+V.
- Ctrl+Z.
- Ctrl+Y.
- Horizontal and vertical scrolling.

## 14.4 File commands

Verify:

- New updates the editor.
- Open loads a `.smile` file.
- Save writes the exact editor text.
- Save As writes the exact editor text.
- No invisible rich-text formatting is inserted into files.

## 14.5 Target switching

Cycle a generated pane through all seven target languages.

Verify:

- The target code changes.
- The highlighting changes to the selected language.
- No exception occurs.
- The editor remains read-only.
- The user can select text and press Ctrl+C.
- Existing Copy and Save Source buttons still work.

## 14.6 Build and run

Verify:

- Build & Run for an individual supported target still works.
- Build & Run Visible Languages still works.
- Cancel behavior still works.
- Build and program output still scrolls normally.
- Objective-C and Swift remain transpile-only on Windows as before.

## 14.7 Resize

Resize the splitters and window.

Verify:

- Editors fill their existing panes.
- Scrollbars work.
- Line-number margins remain visible.
- No pane collapses because of a fixed editor size.

---

# 15. Build and Test Commands

Run all of the following from the current repository path. Use the actual checkout path when it is not `C:\SMILE`.

```bat
cmd /c cd /d C:\SMILE && dotnet restore SMILE.sln
cmd /c cd /d C:\SMILE && dotnet build SMILE.sln -c Debug
cmd /c cd /d C:\SMILE && dotnet test SMILE.sln -c Debug --no-build
cmd /c cd /d C:\SMILE && dotnet build SMILE.sln -c Release
cmd /c cd /d C:\SMILE && dotnet test SMILE.sln -c Release --no-build
```

Requirements:

- Debug build succeeds.
- Debug tests pass.
- Release build succeeds.
- Release tests pass.
- No new compiler warnings are introduced.
- No ignored build artifacts are staged.

After testing:

```bat
cmd /c cd /d C:\SMILE && git status --short
cmd /c cd /d C:\SMILE && git diff --check
cmd /c cd /d C:\SMILE && git diff
```

Review the complete diff before reporting completion.

---

# 16. Documentation and License Notice

Update `README.md` in the same change, as required by `AGENTS.md`.

Document that:

- The four code panes use AvalonEdit.
- The SMILE source and generated targets have syntax highlighting.
- All four code panes display line numbers.
- The output pane remains ordinary text.
- Highlighting is lexical only.
- Semantic highlighting, autocomplete, and diagnostic squiggles are not yet implemented.

Add a small third-party component notice.

If the repository does not already have one, create:

```text
THIRD-PARTY-NOTICES.md
```

Include:

- AvalonEdit name.
- Exact package version.
- Official source repository.
- MIT license.
- The copyright and permission notice taken from the official AvalonEdit package/repository license.

Do not paraphrase or invent license text. Use the exact official notice required by the MIT license.

Add a link from the README to `THIRD-PARTY-NOTICES.md`.

Do not change SMILE’s own AGPL-3.0-only license.

A version bump is not required for this task unless the repository’s current versioning practice clearly requires one. If a version is changed, keep the project file, window title, About dialog, and README fully aligned.

---

# 17. Explicit Non-Goals

Do not implement any of the following in this task:

- Parser-driven semantic coloring.
- Error squiggles.
- Hover diagnostics.
- Autocomplete or IntelliSense.
- Code folding.
- Find/replace UI.
- A minimap.
- Multiple tabs.
- Dark mode.
- User-selectable themes.
- Font settings.
- Formatter support.
- Language Server Protocol support.
- New SMILE keywords.
- New comments syntax.
- New parser behavior.
- New target generators.
- A general UI redesign.
- Monaco, WebView2, Scintilla, or Roslyn.
- Broad refactoring unrelated to the editor replacement.

The architecture should leave room for a future parser-backed SMILE colorizer, but do not build that future phase now.

---

# 18. Definition of Done

The task is complete only when all statements below are true:

- [ ] The official `AvalonEdit` package is referenced once.
- [ ] The source `TextBox` has been replaced with `SmileCodeEditor`.
- [ ] All three generated-code `TextBox` controls have been replaced.
- [ ] The output `TextBox` remains unchanged in function.
- [ ] All four code panes display line numbers.
- [ ] SMILE syntax is highlighted.
- [ ] C# syntax is highlighted.
- [ ] C syntax is highlighted.
- [ ] MASM x64 syntax is highlighted.
- [ ] JavaScript syntax is highlighted.
- [ ] Java syntax is highlighted.
- [ ] Objective-C syntax is highlighted.
- [ ] Swift syntax is highlighted.
- [ ] Switching a target language switches its highlighting immediately.
- [ ] Generated panes remain read-only and selectable.
- [ ] Source typing still triggers existing debounced transpilation.
- [ ] Ordinary typing does not reset the document, caret, selection, or undo history.
- [ ] New/Open/Save/Save As still work.
- [ ] Copy/Save Source/Build & Run still work.
- [ ] All custom XSHD resources load successfully.
- [ ] All automated tests pass in Debug and Release.
- [ ] Manual smoke tests pass.
- [ ] README is updated accurately.
- [ ] AvalonEdit’s MIT notice is preserved.
- [ ] No unrelated files are changed.
- [ ] No build artifacts are committed.
- [ ] Nothing is committed or pushed without Sin’s explicit instruction.

---

# 19. Required Final Report to Sin

When implementation and validation are complete, report:

## Summary

Briefly describe what was implemented.

## Files changed

List every added and modified file.

## Editor behavior

State:

- Which panes now use AvalonEdit.
- Which language definitions are built-in.
- Which definitions are custom.
- How the binding feedback loop and caret-reset problem were prevented.

## Automated validation

Provide the exact result of:

```text
Debug build
Debug tests
Release build
Release tests
git diff --check
```

Include test counts when available.

## Manual validation

Report each manual smoke-test category and whether it passed.

## Dependency and license

State the exact AvalonEdit package version and where its MIT notice was recorded.

## Git status

Show the final concise working-tree status.

## Commit status

Explicitly state that no commit or push was performed unless Sin separately requested it.

Do not merely say “done.” Provide concrete evidence that the editor works and that existing SMILE behavior was preserved.

---

# 20. Official References

- AvalonEdit repository: <https://github.com/icsharpcode/AvalonEdit>
- Official NuGet package: <https://www.nuget.org/packages/AvalonEdit/6.3.1.120>
- AvalonEdit built-in highlighting resources: <https://github.com/icsharpcode/AvalonEdit/tree/master/ICSharpCode.AvalonEdit/Highlighting/Resources>
