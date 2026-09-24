namespace SMILE.Engine;

internal static partial class ManagedDataPersistence
{
    public static string Generate(BoundProgram program, TargetLanguage language)
    {
        var features = CoreBasicProgramFeatureSet.Create(program);
        (string common, string load, string save) = language switch
        {
            TargetLanguage.CSharp => (CSharpCommon, CSharpLoad, CSharpSave),
            TargetLanguage.Java => (JavaCommon, JavaLoad, JavaSave),
            TargetLanguage.JavaScript => (JavaScriptCommon, JavaScriptLoad, JavaScriptSave),
            TargetLanguage.Python => (PythonCommon, PythonLoad, PythonSave),
            TargetLanguage.Swift => (SwiftCommon, SwiftLoad, SwiftSave),
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };
        return string.Join("\n", new[] { common, features.HasDataLoad ? load : "", features.HasDataSave ? save : "" }
            .Where(value => value.Length != 0)).Replace("APPLICATION_HASH", NativeDataPersistence.IdentityHash(program), StringComparison.Ordinal);
    }
}
