namespace SMILE.Engine;

internal sealed partial class CoreBasicMasmWriter
{
    private void WriteConsoleInput()
    {
        // Stack owns the INPUT_RECORD (20 bytes), count and handle. Nothing
        // survives a call; the Windows input buffer owns pending events.
        Line("smile_get_key PROC");
        Line("    sub rsp, 88");
        Line("    mov ecx, -10");
        Line("    call GetStdHandle");
        Line("    mov QWORD PTR [rsp+64], rax");
        Line("smile_get_key_poll:");
        WriteConsoleReadCall("PeekConsoleInputW");
        WriteConsoleReadCall("ReadConsoleInputW");
        Line("    cmp WORD PTR [rsp+32], 1");
        Line("    jne smile_get_key_poll");
        Line("    cmp DWORD PTR [rsp+36], 0");
        Line("    je smile_get_key_poll");
        foreach (bool characters in new[] { false, true })
        {
            Line($"    movzx eax, WORD PTR [rsp+{(characters ? 46 : 42)}]");
            foreach (var key in ConsoleKeyMap.Keys)
                foreach (int value in characters ? key.Characters.Select(c => (int)c) : key.Virtual)
                {
                    Line($"    cmp eax, {value}");
                    Line($"    je smile_get_key_{key.Code}");
                }
        }
        Line("    or ax, WORD PTR [rsp+42]");
        Line("    jz smile_get_key_poll");
        Line("    mov eax, 19");
        Line("    jmp smile_get_key_done");
        foreach (var key in ConsoleKeyMap.Keys)
        {
            Line($"smile_get_key_{key.Code}:");
            Line($"    mov eax, {key.Code}");
            Line("    jmp smile_get_key_done");
        }
        Line("smile_get_key_none:");
        Line("    xor eax, eax");
        Line("smile_get_key_done:");
        Line("    add rsp, 88");
        Line("    ret");
        Line("smile_get_key ENDP");
        Line();
    }

    private void WriteConsoleReadCall(string function)
    {
        Line("    mov rcx, QWORD PTR [rsp+64]");
        Line("    lea rdx, [rsp+32]");
        Line("    mov r8d, 1");
        Line("    lea r9, [rsp+56]");
        Line($"    call {function}");
        Line("    test eax, eax");
        Line("    jz smile_get_key_none");
        Line("    cmp DWORD PTR [rsp+56], 0");
        Line("    je smile_get_key_none");
    }
}
