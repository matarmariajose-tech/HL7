namespace Immunization.Domain;

public sealed class AcknowledgmentInterpreter
{
    public Assessment Interpret(string response, string? expectedMessageId)
    {
        if (string.IsNullOrWhiteSpace(expectedMessageId))
            return new(Outcome.Unknown, "Cannot correlate: source MSH-10 is missing.");
        Hl7 ack;
        try { ack = new Hl7(response); }
        catch (FormatException) { return new(Outcome.Unknown, "Service did not return a parseable HL7 acknowledgment."); }
        var headers = ack.Segments("MSH").ToArray();
        if (headers.Length != 1 || ack.Part(ack.Field(headers[0], 9), 1) != "ACK")
            return new(Outcome.Unknown, "Service response is not a single ACK message.");
        var acknowledgments = ack.Segments("MSA").ToArray();
        if (acknowledgments.Length != 1)
            return new(Outcome.Unknown, "Expected exactly one MSA segment.");
        var msa = acknowledgments[0];
        var code = ack.Field(msa, 1);
        if (!string.Equals(ack.Field(msa, 2), expectedMessageId, StringComparison.Ordinal))
            return new(Outcome.Unknown, "MSA-2 does not match source MSH-10.", code);
        var findings = ack.Segments("ERR").Select(err => new Finding(
            ack.Decode(ack.Field(err, 3)), ack.Field(err, 4), ack.Decode(ack.Field(err, 2)),
            ack.Decode(string.Join(" | ", new[] { ack.Field(err, 7), ack.Field(err, 8) }
                .Where(x => !string.IsNullOrWhiteSpace(x)))))).ToArray();
        var detail = ack.Decode(ack.Field(msa, 3));
        return code switch
        {
            "AA" when findings.Any(f => f.Severity is "E" or "F") =>
                new(Outcome.Unknown, "Contradictory response: AA with error severity.", code, findings),
            "AA" when findings.Length > 0 => new(Outcome.Observed, detail.Length > 0 ? detail : "Accepted with observations.", code, findings),
            "AA" => new(Outcome.Accepted, detail.Length > 0 ? detail : "Application accepted the message.", code, findings),
            "AE" or "AR" => new(Outcome.Rejected, detail.Length > 0 ? detail : "Application rejected the message; inspect ERR findings.", code, findings),
            "CA" or "CE" or "CR" => new(Outcome.Unknown, "Commit acknowledgment does not establish application outcome.", code, findings),
            _ => new(Outcome.Unknown, "Unsupported application acknowledgment code.", code, findings)
        };
    }
}
