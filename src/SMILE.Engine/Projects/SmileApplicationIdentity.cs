namespace SMILE.Engine;

public static class SmileApplicationIdentity
{
    public static bool IsValid(string? value) => value is { Length: >= 3 and <= 128 } && value.Contains('.') &&
        value.Split('.').All(segment => segment.Length > 0 && segment[0] is >= 'a' and <= 'z' &&
            segment[^1] != '-' && segment.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'));

    public static string Validate(string value, TextSpan span = default)
    {
        if (!IsValid(value)) throw new SmileInputException(new("SMILE3800", DiagnosticSeverity.Error,
            "ApplicationId requires 3 through 128 lowercase ASCII characters in at least two dot-separated segments. Each segment starts with a letter and contains letters, digits or non-trailing hyphens.", span));
        return value;
    }

    public static string Resolve(SmileProject project, string? commandLineOverride)
    {
        if (commandLineOverride is null) return project.EffectiveApplicationId;
        Validate(commandLineOverride);
        if (project.IsLibrary)
            throw new SmileInputException(new("SMILE3802", DiagnosticSeverity.Error,
                "Library projects do not own an ApplicationId.", new TextSpan(0, 0, 1, 1) { SourcePath = project.Path }));
        if (project.ApplicationId is not null && project.ApplicationId != commandLineOverride)
            throw new SmileInputException(new("SMILE3803", DiagnosticSeverity.Error,
                "An ApplicationId override must exactly match an explicit ApplicationId already declared by the application project.", new TextSpan(0, 0, 1, 1) { SourcePath = project.Path }));
        return commandLineOverride;
    }
}
