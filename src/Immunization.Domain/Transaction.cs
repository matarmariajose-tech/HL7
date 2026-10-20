namespace Immunization.Domain;

public enum Outcome { Accepted, Observed, Rejected, Unknown }
public sealed record Finding(string Code, string Severity, string Location, string Description);
public sealed record Assessment(Outcome Outcome, string Reason, string? AcknowledgmentCode = null,
    IReadOnlyList<Finding>? Findings = null, Guid? ServiceMessageId = null);
public sealed record Submission(Guid Id, string FileName, string Sha256, string? MessageId, string Content);
public sealed record Receipt(Guid Id, string FileName, string Sha256, string? MessageId,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, Assessment Assessment);
