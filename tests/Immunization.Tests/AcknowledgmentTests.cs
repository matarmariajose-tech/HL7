using Immunization.Domain;
using Xunit;

namespace Immunization.Tests;

public sealed class AcknowledgmentTests
{
    private readonly AcknowledgmentInterpreter interpreter = new();
    internal static string Ack(string code, string id = "source", string suffix = "") =>
        $"MSH|^~\\&|REGISTRY|TEST|||20260101||ACK^V04|response|T|2.5.1\rMSA|{code}|{id}\r{suffix}";

    [Theory]
    [InlineData("AA", Outcome.Accepted)]
    [InlineData("AE", Outcome.Rejected)]
    [InlineData("AR", Outcome.Rejected)]
    [InlineData("CA", Outcome.Unknown)]
    [InlineData("CE", Outcome.Unknown)]
    [InlineData("CR", Outcome.Unknown)]
    [InlineData("ZZ", Outcome.Unknown)]
    public void ClassifiesOnlyCorrelatedApplicationAcknowledgments(string code, Outcome expected) =>
        Assert.Equal(expected, interpreter.Interpret(Ack(code), "source").Outcome);

    [Fact]
    public void WarningIncludesServiceReasonAndFieldLocation()
    {
        var result = interpreter.Interpret(Ack("AA", suffix: "ERR||RXA^1^5|103^Invalid value^HL70357|W|||Check vaccine\r"), "source");
        Assert.Equal(Outcome.Observed, result.Outcome);
        var finding = Assert.Single(result.Findings!);
        Assert.Equal("RXA^1^5", finding.Location);
        Assert.Equal("Check vaccine", finding.Description);
    }

    [Fact]
    public void ContradictoryAcknowledgmentIsUnknown() =>
        Assert.Equal(Outcome.Unknown, interpreter.Interpret(Ack("AA", suffix: "ERR|||101|E\r"), "source").Outcome);

    [Theory]
    [InlineData("")]
    [InlineData("<html>failure</html>")]
    [InlineData("MSH|^~\\&|A|B|||20260101||ACK|R|T|2.5.1")]
    public void MissingAcknowledgmentIsUnknown(string response) =>
        Assert.Equal(Outcome.Unknown, interpreter.Interpret(response, "source").Outcome);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("different")]
    public void UncorrelatedAcknowledgmentIsUnknown(string? source) =>
        Assert.Equal(Outcome.Unknown, interpreter.Interpret(Ack("AA"), source).Outcome);

    [Fact]
    public void DuplicateMsaIsAmbiguous() =>
        Assert.Equal(Outcome.Unknown, interpreter.Interpret(Ack("AA", suffix: "MSA|AR|source\r"), "source").Outcome);

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SupportsTransportLineEndings(string ending) =>
        Assert.Equal(Outcome.Accepted, interpreter.Interpret(Ack("AA").Replace("\r", ending), "source").Outcome);

    [Fact]
    public void SupportsDeclaredDelimiters()
    {
        var ack = "MSH*$%!?*REGISTRY*TEST***20260101**ACK$V04*response*T*2.5.1\rMSA*AA*source\r";
        Assert.Equal(Outcome.Accepted, interpreter.Interpret(ack, "source").Outcome);
    }

    [Fact]
    public void MissingSourceDoesNotInferRejectionFromPatientOrVaccine() =>
        Assert.Equal(Outcome.Unknown, interpreter.Interpret(Ack("AA"), null).Outcome);
}
