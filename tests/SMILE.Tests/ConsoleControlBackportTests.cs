using System.IO;
using System.Text.RegularExpressions;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("ConsoleControlBackport")]
[DoNotParallelize]
public sealed class ConsoleControlBackportTests
{
    internal const string Source = """
Option Explicit
Dim Pressed As Number
Dim Started As Number
Get Key Pressed
Print "INITIAL:"; Pressed
Started = Timer()
Do
    Get Key Pressed
    If Pressed <> Key_None Then
        Print "EVENT:"; Pressed
    End If
    If Pressed = Key_Escape Then
        Exit Do
    End If
    Wait 1 Milliseconds
Loop Until Timer() - Started > 6000
Get Key Pressed
Print "EMPTY:"; Pressed
""";

    // VT text cannot represent a modifier-only press. Inject actual records
    // into the attached console; never substitute an escape sequence for Ctrl.
    private const string Injector = """
#include <windows.h>
int main(void)
{
    Sleep(1500);
    INPUT_RECORD records[8] = {0};
    records[0].EventType = KEY_EVENT;
    records[0].Event.KeyEvent.wVirtualKeyCode = VK_SHIFT;
    records[1].EventType = FOCUS_EVENT;
    for (int i = 2; i < 8; i++)
    {
        records[i].EventType = KEY_EVENT;
        records[i].Event.KeyEvent.bKeyDown = TRUE;
        records[i].Event.KeyEvent.wRepeatCount = 1;
    }
    records[2].Event.KeyEvent.wVirtualKeyCode = VK_CONTROL;
    records[2].Event.KeyEvent.wRepeatCount = 3;
    records[2].Event.KeyEvent.dwControlKeyState = LEFT_CTRL_PRESSED;
    records[3].Event.KeyEvent.wVirtualKeyCode = VK_CONTROL;
    records[3].Event.KeyEvent.bKeyDown = FALSE;
    records[4].Event.KeyEvent.wVirtualKeyCode = 'C';
    records[4].Event.KeyEvent.uChar.UnicodeChar = 3;
    records[4].Event.KeyEvent.dwControlKeyState = LEFT_CTRL_PRESSED;
    records[5].Event.KeyEvent.wVirtualKeyCode = VK_CONTROL;
    records[5].Event.KeyEvent.dwControlKeyState = RIGHT_CTRL_PRESSED;
    records[6].Event.KeyEvent.uChar.UnicodeChar = 0x00e9;
    records[7].Event.KeyEvent.wVirtualKeyCode = VK_ESCAPE;
    DWORD written;
    return WriteConsoleInputW(GetStdHandle(STD_INPUT_HANDLE), records, 8, &written) && written == 8 ? 0 : 1;
}
""";

    [TestMethod]
    public async Task Native_control_events_are_consumed_once_in_order_on_all_ten_targets()
    {
        ToolchainRegistry registry = ToolchainRegistry.CreateDefault();
        var injectorProgram = new GeneratedProgram(TargetLanguage.C, [new("Program.c", Injector, IsPrimary: true)]);
        BuildRunResult injector = await registry.Get(TargetLanguage.C).BuildAndRunAsync(
            injectorProgram, CancellationToken.None, BuildRunOptions.BuildOnly);
        Assert.IsTrue(injector.Success, injector.BuildOutput);
        foreach (TargetLanguage target in ActiveTargetLanguages.All)
        {
            TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
            Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
            BuildRunResult build = await registry.Get(target).BuildAndRunAsync(transpile.GeneratedProgram!,
                CancellationToken.None, BuildRunOptions.BuildOnly with { CreatePauseLauncher = true });
            Assert.IsTrue(build.Success, $"{target}: {build.BuildOutput}\n{build.StandardError}");
            string wrapper = Path.Combine(build.WorkingDirectory!, "control-probe.cmd");
            await File.WriteAllTextAsync(wrapper,
                $"@echo off\r\nstart \"\" /b \"{Path.Combine(injector.WorkingDirectory!, "Program.exe")}\"\r\ncall \"{build.PauseLauncherPath}\"\r\n");
            PseudoConsoleResult run = await WindowsPseudoConsole.RunBatchFileAsync(wrapper,
                [PseudoConsoleInput.Text(2500, "x")], TimeSpan.FromSeconds(12), CancellationToken.None);
            Assert.IsFalse(run.TimedOut, $"{target}: {run.Output}");
            Assert.AreEqual(0, run.ExitCode, $"{target}: {run.Output}");
            string visible = Regex.Replace(run.Output, "\\x1B\\[[0-?]*[ -/]*[@-~]", "").Replace("\r", "");
            StringAssert.Contains(visible, "INITIAL:0");
            StringAssert.Contains(visible, "EVENT:33\nEVENT:41\nEVENT:33\nEVENT:19\nEVENT:15\nEMPTY:0", target.ToString());
            Console.WriteLine($"PASS {target}: native Control key records, ordering, release filtering, no input.");
        }
    }

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Node_console_addon_is_emitted_only_for_Get_Key()
    {
        var transpiler = new SmileTranspiler();
        GeneratedProgram ordinary = transpiler.Transpile("Print 1", TargetLanguage.JavaScript).GeneratedProgram!;
        Assert.HasCount(1, ordinary.Files);
        GeneratedProgram console = transpiler.Transpile("Get Key Pressed", TargetLanguage.JavaScript).GeneratedProgram!;
        Assert.IsTrue(console.Files.Any(file => file.RelativePath == "SmileConsole.c"));
        StringAssert.Contains(console.PrimaryFile.Content, "require(\"./SmileConsole.node\")");
        Assert.IsFalse(console.PrimaryFile.Content.Contains("setRawMode", StringComparison.Ordinal));
    }
}
