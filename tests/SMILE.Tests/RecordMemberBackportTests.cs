using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
[TestCategory("RecordMemberBackport")]
public sealed class RecordMemberBackportTests
{
    private const string Source = """
Option Explicit
Type Point
    Public X As Number
    Name As Text
    Public Sub MoveBy(Optional Delta As Number = 1)
        Me.X = Me.X + Delta
    End Sub
    Function Read(Optional Extra As Number = 0) As Number
        Return Me.Secret() + Extra
    End Function
    Private Function Secret() As Number
        Return Me.X
    End Function
    Sub Alias(ByRef Value As Number)
        Me.X = Me.X + 1
        Value = Value + 10
    End Sub
    Property Current As Number
        Get
            Return Me.X
        End Get
        Set
            Me.X = Value
        End Set
    End Property
    Property Caption As Text
        Get
            Return Me.Name + "!"
        End Get
    End Property
    Property Label As Text
        Set
            Me.Name = Value + "?"
        End Set
    End Property
    Function Copy() As Point
        Return Me
    End Function
    Function Mixed(FirstNumber As Number, FirstReal As Double, SecondNumber As Number, SecondReal As Double, ThirdNumber As Number, ThirdReal As Double) As Double
        Return ToDouble(Me.X + FirstNumber + SecondNumber + ThirdNumber) + FirstReal + SecondReal + ThirdReal
    End Function
    Function Recur(Remaining As Number) As Number
        If Remaining = 0 Then
            Return Me.X
        End If
        Me.X = Me.X + 1
        Return Me.Recur(Remaining - 1)
    End Function
End Type
Type Bag
    Position As Point
    Points[2] As Point
End Type
Type Utility
    Function Answer() As Number
        Return 7
    End Function
    Function Echo(self As Number) As Number
        Return self
    End Function
    Function property() As Number
        Return 9
    End Function
    Property ReadOnlyValue As Number
        Get
            Return 10
        End Get
    End Property
End Type
Dim P As Point
Dim Saved As Point
Dim Bags[2] As Bag
Dim Helper As Utility
Dim Calls As Number
Call P.MoveBy()
Call (P).MoveBy(Delta:=2)
Print P.Read(Extra:=3)
Call P.Alias(P.X)
Print P.X
P.Current = 20
P.Label = "Sin"
Print P.Current; ":"; P.Caption
Saved = P.Copy()
Call P.MoveBy(4)
Print Saved.X; ":"; P.X
Print P.Mixed(ThirdReal:=0.125, ThirdNumber:=3, SecondReal:=0.25, SecondNumber:=2, FirstReal:=0.5, FirstNumber:=1)
Print P.Recur(3)
With Bags[1].Points[0]
    Call .MoveBy(5)
    .Current = .Current + 1
    Print .Read()
End With
Print Bags[1].Points[0].X; ":"; Bags[0].Position.X
Bags[ReceiverIndex()].Position.Current = NewValue()
Print Bags[1].Position.X; ":"; Calls
Print Helper.Answer()
Print Helper.Echo(8)
Print Helper.property(); ":"; Helper.ReadOnlyValue
Function ReceiverIndex() As Number
    Print "receiver"
    Calls = Calls + 1
    Return 1
End Function
Function NewValue() As Number
    Print "value"
    Return 9
End Function
""";
    private const string Expected = "6\n14\n20:Sin?!\n20:24\n30.875\n27\n6\n6:0\nvalue\nreceiver\n9:1\n7\n8\n9:10\n";

    private const string AggregateSource = """
Option Explicit
Enum State
    Ready
End Enum
Type Token
    Caption As Text
    Counts[2] As Number
End Type
Type Box
    Stored As Token
    Amount As Double
    Enabled As Boolean
    Property Snapshot As Token
        Get
            Return Me.Stored
        End Get
        Set
            Value.Counts[1] = Value.Counts[1] + 1
            Me.Stored = Value
        End Set
    End Property
    Property Real As Double
        Get
            Return Me.Amount
        End Get
        Set
            Call Increase(Value)
            Value = Value + ToDouble(newValue)
            Me.Amount = Value
        End Set
    End Property
    Property Active As Boolean
        Get
            Return Me.Enabled
        End Get
        Set
            Me.Enabled = Value
        End Set
    End Property
    Private Property Secret As Text
        Get
            Return Me.Stored.Caption + "!"
        End Get
    End Property
    Function Description() As Text
        Return Me.Secret
    End Function
    Sub CopyFrom(ByVal Other As Box)
        Me.Stored = Other.Snapshot
    End Sub
    Function ReadState(OtherToken As Token) As Text
        Return OtherToken.Caption
    End Function
End Type
Type Reader
    Total As Number
    Property Missing As Number
        Get
            Dim Bytes[1] As Number
            Dim ByteCount As Number
            Load Text File "missing-member-backport-file" Into Bytes Count ByteCount
            Return ByteCount
        End Get
        Set
            Dim Bytes[1] As Number
            Dim ByteCount As Number
            Load Text File "missing-member-backport-file" Into Bytes Count ByteCount
            Me.Total = Value + ByteCount
        End Set
    End Property
End Type
Dim First As Token
Dim Saved As Token
Dim Item As Box
Dim Second As Box
Dim InputFile As Reader
Dim newValue As Number
newValue = 3
First.Caption = "one"
First.Counts[1] = 4
Item.Snapshot = First
First.Caption = "changed"
Saved = Item.Snapshot
Saved.Counts[1] = 99
Print Item.Snapshot.Caption; ":"; Item.Snapshot.Counts[1]; ":"; First.Counts[1]
Item.Real = 0.5
Item.Active = True
Print Item.Real; ":"; Item.Active; ":"; Item.Description()
Call Second.CopyFrom(Item)
Item.Stored.Caption = "different"
Print Second.Stored.Caption; ":"; Second.Stored.Counts[1]
Print Item.ReadState(First)
Print InputFile.Missing
InputFile.Missing = 7
Print InputFile.Total
Sub Increase(ByRef Value As Double)
    Value = Value + 1.25
End Sub
""";

    private const string AggregateExpected = "one:5:4\n4.75:True:one!\none:5\nchanged\n0\n7\n";

    [TestMethod]
    [TestCategory("MissionGuardrail")]
    public void Record_members_evaluate_with_borrowed_receivers_and_value_first_setters()
    {
        EvaluationResult result = new SmileEvaluator().Evaluate(Source);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(Expected, result.Output);
        SmileFormatResult formatted = SmileSourceFormatter.Format(Source);
        Assert.IsTrue(formatted.Success, string.Join("\n", formatted.Diagnostics));
        StringAssert.Contains(formatted.FormattedSource, "    Public Sub MoveBy(Optional Delta As Number = 1)\n\n        Me.X = Me.X + Delta");
        StringAssert.Contains(formatted.FormattedSource, "    Property Current As Number\n        Get\n\n            Return Me.X");
        Assert.AreEqual(Expected, new SmileEvaluator().Evaluate(formatted.FormattedSource).Output);
        Assert.AreEqual(formatted.FormattedSource, SmileSourceFormatter.Format(formatted.FormattedSource).FormattedSource);
        result = new SmileEvaluator().Evaluate(AggregateSource);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual(AggregateExpected, result.Output);
    }

    [TestMethod]
    [DataRow("Print Me", "SMILE3442")]
    [DataRow("Type P\nPrivate X As Number\nEnd Type", "SMILE3440")]
    [DataRow("Type P\nX As Number\nSub X()\nEnd Sub\nEnd Type", "SMILE3440")]
    [DataRow("Type P\nProperty X As Number\nEnd Property\nEnd Type", "SMILE3441")]
    [DataRow("Type P\nSub Work()\nMe = Me\nEnd Sub\nEnd Type", "SMILE3442")]
    [DataRow("Type P\nSub Work()\nCall Take(Me)\nEnd Sub\nEnd Type\nSub Take(ByRef Value As P)\nEnd Sub", "SMILE3442")]
    [DataRow("Type P\nPrivate Sub Work()\nEnd Sub\nEnd Type\nDim V As P\nCall V.Work()", "SMILE3446")]
    [DataRow("Type P\nX As Number\nEnd Type\nDim V As P\nCall V.Missing()", "SMILE3443")]
    [DataRow("Type P\nSub Work()\nEnd Sub\nEnd Type\nCall Make().Work()\nFunction Make() As P\nDim V As P\nReturn V\nEnd Function", "SMILE3444")]
    [DataRow("Type P\nProperty X As Number\nGet\nReturn 1\nEnd Get\nEnd Property\nEnd Type\nDim V As P\nV.X = 2", "SMILE3445")]
    [DataRow("Type P\nProperty X As Number\nSet\nEnd Set\nEnd Property\nEnd Type\nDim V As P\nPrint V.X", "SMILE3445")]
    [DataRow("Type P\nProperty X As Number\nGet\nReturn 1\nEnd Get\nEnd Property\nEnd Type\nDim V As P\nCall Take(V.X)\nSub Take(ByRef Value As Number)\nEnd Sub", "SMILE2165")]
    public void Invalid_record_members_are_diagnosed(string source, string code)
    {
        BindResult result = new SmileTranspiler().Bind(source);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == code), string.Join("\n", result.Diagnostics));
    }

    [TestMethod]
    public void Missing_member_terminators_preserve_later_declarations()
    {
        const string source = "Type Broken\nPublic X As Number\nProperty Score As Number\nGet\nReturn Me.X\nSet\nMe.X = Value\nEnd Set\nY As Number\nSub Work()\nMe.Y = 1\nZ As Number\nEnd Type\nDim Current As Broken";
        ParseResult result = new SmileTranspiler().Parse(source);
        Assert.IsFalse(result.Success);
        RecordDeclarationSyntax type = result.Program!.Statements.OfType<RecordDeclarationSyntax>().Single();
        Assert.AreEqual("X|Y|Z", string.Join("|", type.SourceItems.OfType<InstanceFieldDeclarationSyntax>().Select(field => field.Name)));
        Assert.IsNotNull(type.SourceItems.OfType<InstancePropertyDeclarationSyntax>().Single().Setter);
        Assert.AreEqual(1, type.SourceItems.OfType<InstanceMethodDeclarationSyntax>().Count());
        Assert.IsTrue(result.Program.Statements.OfType<DimStatementSyntax>().Any(variable => variable.Name == "Current"));
    }

    [TestMethod]
    public void A_record_parameter_uses_local_lookup_before_an_enum_qualifier()
    {
        const string source = "Enum State\nReady\nEnd Enum\nType Item\nCaption As Text\nFunction Read(State As Item) As Text\nReturn State.Caption\nEnd Function\nEnd Type\nDim Value As Item\nValue.Caption = \"local\"\nPrint Value.Read(Value)";
        EvaluationResult result = new SmileEvaluator().Evaluate(source);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual("local\n", result.Output);
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.Cobol)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Python)]
    [DataRow(TargetLanguage.Swift)]
    [DataRow(TargetLanguage.Cpp)]
    public async Task Record_members_execute_on_every_target(TargetLanguage target)
    {
        TranspileResult transpile = new SmileTranspiler().Transpile(Source, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        BuildRunResult run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual(Expected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
        transpile = new SmileTranspiler().Transpile(AggregateSource, target);
        Assert.IsTrue(transpile.Success, string.Join("\n", transpile.Diagnostics));
        run = await ToolchainRegistry.CreateDefault().Get(target).BuildAndRunAsync(transpile.GeneratedProgram!, CancellationToken.None);
        Assert.IsTrue(run.Success, $"{target}: {run.BuildOutput}\n{run.StandardError}");
        Assert.AreEqual(AggregateExpected, run.StandardOutput.Replace("\r\n", "\n"), target.ToString());
    }
}
