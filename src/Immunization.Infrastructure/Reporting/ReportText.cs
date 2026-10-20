using System.Globalization;
using Immunization.Domain;

namespace Immunization.Infrastructure.Reporting;

internal static class ReportText
{
    private static readonly IReadOnlyDictionary<string, AssessmentReason> LegacyReasons = new Dictionary<string, AssessmentReason>(StringComparer.Ordinal)
    {
        ["Cannot correlate: source MSH-10 is missing."] = AssessmentReason.SourceIdentifierMissing,
        ["Service did not return a parseable HL7 acknowledgment."] = AssessmentReason.InvalidAcknowledgment,
        ["Service response is not a single ACK message."] = AssessmentReason.UnexpectedMessageType,
        ["Expected exactly one MSA segment."] = AssessmentReason.AmbiguousAcknowledgment,
        ["MSA-2 does not match source MSH-10."] = AssessmentReason.CorrelationMismatch,
        ["Contradictory response: AA with error severity."] = AssessmentReason.ContradictoryAcknowledgment,
        ["Commit acknowledgment does not establish application outcome."] = AssessmentReason.CommitAcknowledgmentOnly,
        ["Unsupported application acknowledgment code."] = AssessmentReason.UnsupportedAcknowledgmentCode,
        ["Request cancelled; delivery may have occurred. Do not retry automatically."] = AssessmentReason.RequestCancelled,
        ["Response stream failed; delivery may have occurred. Do not retry automatically."] = AssessmentReason.ResponseStreamFailure,
        ["Transport failed; delivery may have occurred. Do not retry automatically."] = AssessmentReason.TransportFailure,
        ["Service response exceeds configured size limit."] = AssessmentReason.ResponseTooLarge,
        ["Service response does not match the documented JSON contract."] = AssessmentReason.InvalidResponseEnvelope,
        ["Application accepted the message."] = AssessmentReason.Accepted,
        ["Accepted with observations."] = AssessmentReason.AcceptedWithObservations,
        ["Application rejected the message; inspect ERR findings."] = AssessmentReason.ApplicationRejected
    };
    public static string Status(Outcome outcome) => outcome switch
    {
        Outcome.Accepted => "Aceptado",
        Outcome.Observed => "Con observaciones",
        Outcome.Rejected => "Rechazado",
        _ => "Sin confirmación"
    };
    public static string Summary(Assessment assessment)
    {
        var reason = Resolve(assessment);
        if (reason == AssessmentReason.AcceptedWithObservations || reason == AssessmentReason.ApplicationRejected)
        {
            var description = assessment.Findings?.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Description))?.Description;
            if (description is not null) return description;
            if (!LegacyReasons.ContainsKey(assessment.Reason)) return assessment.Reason;
        }
        return reason switch
        {
            AssessmentReason.Accepted => "El registro aceptó el mensaje.",
            AssessmentReason.AcceptedWithObservations => "Aceptado con observaciones del registro.",
            AssessmentReason.ApplicationRejected => "El registro informó un error de aplicación.",
            AssessmentReason.SourceIdentifierMissing => "El mensaje no tiene identificador para correlacionar la respuesta.",
            AssessmentReason.InvalidAcknowledgment => "La respuesta no contiene un acuse HL7 válido.",
            AssessmentReason.UnexpectedMessageType => "La respuesta no corresponde a un único mensaje ACK.",
            AssessmentReason.AmbiguousAcknowledgment => "La respuesta no contiene un único segmento MSA.",
            AssessmentReason.CorrelationMismatch => "El identificador de la respuesta no coincide con el del envío.",
            AssessmentReason.ContradictoryAcknowledgment => "El acuse indica aceptación, pero también contiene errores.",
            AssessmentReason.CommitAcknowledgmentOnly => "Se confirmó la recepción, pero no el procesamiento del mensaje.",
            AssessmentReason.UnsupportedAcknowledgmentCode => "El servicio devolvió un código de acuse no reconocido.",
            AssessmentReason.RequestTimedOut => "El servicio no confirmó el resultado dentro del tiempo permitido.",
            AssessmentReason.RequestCancelled => "La solicitud se canceló antes de confirmar el resultado.",
            AssessmentReason.TransportFailure => "No se pudo completar la comunicación con el servicio.",
            AssessmentReason.ResponseStreamFailure => "Se interrumpió la lectura de la respuesta.",
            AssessmentReason.HttpFailure => $"El servicio respondió HTTP {HttpStatus(assessment)} sin un acuse verificable.",
            AssessmentReason.ResponseTooLarge => "La respuesta superó el tamaño permitido.",
            AssessmentReason.InvalidResponseEnvelope => "La respuesta no cumple el contrato JSON del servicio.",
            _ => assessment.Reason
        };
    }
    private static AssessmentReason Resolve(Assessment assessment)
    {
        if (assessment.ReasonCode != AssessmentReason.Unspecified) return assessment.ReasonCode;
        if (LegacyReasons.TryGetValue(assessment.Reason, out var reason)) return reason;
        return TryLegacyHttpStatus(assessment.Reason, out _) ? AssessmentReason.HttpFailure : AssessmentReason.Unspecified;
    }
    private static string HttpStatus(Assessment assessment) => assessment.HttpStatusCode?.ToString(CultureInfo.InvariantCulture)
        ?? (TryLegacyHttpStatus(assessment.Reason, out var status) ? status.ToString(CultureInfo.InvariantCulture) : "no disponible");
    private static bool TryLegacyHttpStatus(string reason, out int status)
    {
        status = 0;
        return reason.StartsWith("HTTP ", StringComparison.Ordinal) && reason.Length > 8 && reason[8] == ';'
            && reason[5..8].Length == 3 && int.TryParse(reason[5..8], NumberStyles.None, CultureInfo.InvariantCulture, out status)
            && reason[9..] == " no verified application acknowledgment. Delivery may have occurred.";
    }
}
