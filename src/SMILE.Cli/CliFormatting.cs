using SMILE.Engine;

namespace SMILE.Cli;

internal static class CliFormatting
{
    public static async Task<int> RunAsync(SmileCompilationInput input, bool check)
    {
        BindResult bind = input.Bind();
        if (!bind.Success)
        {
            foreach (Diagnostic diagnostic in bind.Diagnostics) Console.Error.WriteLine(diagnostic);
            return 1;
        }
        string provider = input.Sources[0].Provider;
        var results = input.Sources.Where(source => source.Provider == provider)
            .Select(source => (Source: source, Format: SmileSourceFormatter.Format(source, input.Sources))).ToArray();
        if (results.Any(result => !result.Format.Success))
        {
            foreach (Diagnostic diagnostic in results.SelectMany(result => result.Format.Diagnostics).Distinct()) Console.Error.WriteLine(diagnostic);
            return 1;
        }
        bool changed = false;
        foreach ((SmileSource source, SmileFormatResult format) in results)
        {
            if (!format.NeedsFormatting) { Console.WriteLine("Formatting is current: " + source.Path); continue; }
            changed = true;
            if (check) { Console.Error.WriteLine("Formatting required: " + source.Path); continue; }
            string temporary = Path.Combine(Path.GetDirectoryName(source.Path)!, "." + Path.GetFileName(source.Path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, format.FormattedSource).ConfigureAwait(false);
                File.Move(temporary, source.Path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Console.WriteLine("Formatted: " + source.Path);
        }
        return check && changed ? 1 : 0;
    }
}
