using System.Net;
using Immunization.Domain;
using Immunization.Infrastructure;
using Xunit;

namespace Immunization.Tests;

public sealed class ReportTests
{
    [Fact]
    public async Task SimulationContextSurvivesJournalReload()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        var report = path + ".html";
        var context = new ReportContext("Simulador", true, "America/Argentina/Buenos_Aires", "127.0.0.1");
        try
        {
            using (var journal = new JsonJournal(path, context))
                await journal.RecordPendingAsync(new(Guid.NewGuid(), "a.hl7", "hash", "source", "private payload"), DateTimeOffset.UtcNow, default);
            var snapshot = JournalReader.Read(path);
            Assert.Equal(context, snapshot.Context);
            Assert.Single(snapshot.Pending);
            await HtmlReport.WriteAsync(report, snapshot.Completed, default, snapshot.Pending, snapshot.Context);
            var html = await File.ReadAllTextAsync(report);
            Assert.Contains("Simulación local", html);
            Assert.Contains("Este lote no se envió al registro externo", html);
            Assert.DoesNotContain("private payload", html);
        }
        finally { File.Delete(path); File.Delete(report); }
    }

    [Fact]
    public async Task LegacyJournalDoesNotInventItsEnvironment()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        var report = path + ".html";
        try
        {
            using (var journal = new JsonJournal(path))
                await journal.RecordPendingAsync(new(Guid.NewGuid(), "a.hl7", "hash", "source", "private payload"), DateTimeOffset.UtcNow, default);
            var snapshot = JournalReader.Read(path);
            Assert.Null(snapshot.Context);
            await HtmlReport.WriteAsync(report, snapshot.Completed, default, snapshot.Pending, snapshot.Context);
            Assert.Contains("Origen no registrado", await File.ReadAllTextAsync(report));
        }
        finally { File.Delete(path); File.Delete(report); }
    }

    [Fact]
    public async Task TypedReasonControlsPresentationAndOriginalDiagnosticIsPreserved()
    {
        var assessment = new Assessment(Outcome.Unknown, "Original diagnostic retained verbatim", "AA",
            ReasonCode: AssessmentReason.CorrelationMismatch);
        var html = await RenderAsync(assessment);
        Assert.Contains("El identificador de la respuesta no coincide con el del envío.", WebUtility.HtmlDecode(html));
        Assert.Contains("Original diagnostic retained verbatim", html);
        Assert.Contains("Sin confirmación", html);
        Assert.DoesNotContain("<span class=\"status Unknown\">Unknown", html);
    }

    [Fact]
    public async Task ExistingHttpFailureDiagnosticGetsReadableSummary()
    {
        var html = await RenderAsync(new(Outcome.Unknown, "HTTP 503; no verified application acknowledgment. Delivery may have occurred."));
        Assert.Contains("El servicio respondió HTTP 503 sin un acuse verificable.", WebUtility.HtmlDecode(html));
        Assert.Contains("Delivery may have occurred.", html);
    }

    [Fact]
    public async Task DatesUseConfiguredTimezoneAndUtcEvidenceRemainsAvailable()
    {
        var instant = new DateTimeOffset(2026, 10, 20, 22, 22, 58, TimeSpan.Zero);
        var html = await RenderAsync(new(Outcome.Accepted, "Application accepted the message."), instant);
        Assert.Contains("19:22:58 (UTC-03:00)", html);
        Assert.Contains("2026-10-20T22:22:58.0000000+00:00", html);
    }

    [Fact]
    public async Task DetailsStartClosedAndReportHasNoExternalAssets()
    {
        var html = await RenderAsync(new(Outcome.Accepted, "Application accepted the message."));
        Assert.Contains("<details id=", html);
        Assert.DoesNotContain("<details open", html);
        Assert.DoesNotContain("<link ", html);
        Assert.DoesNotContain("<script src=", html);
        Assert.Contains("data-filter=\"Unknown\"", html);
        Assert.Contains("SHA-256 del archivo", html);
        Assert.DoesNotContain("<ul></ul>", html);
    }

    [Fact]
    public async Task FindingsAndContextCannotInjectMarkup()
    {
        var assessment = new Assessment(Outcome.Rejected, "<img src=x onerror=alert(1)>", "AE",
            [new("<script>bad()</script>", "E", "RXA^1^5", "<svg onload=alert(1)>")], ReasonCode: AssessmentReason.ApplicationRejected);
        var html = await RenderAsync(assessment, context: new("<img src=x onerror=alert(2)>", false, "UTC", "<iframe>"));
        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("<script>bad()", html);
        Assert.DoesNotContain("<svg onload", html);
        Assert.DoesNotContain("<iframe>", html);
        Assert.Contains("&lt;svg", html);
    }

    [Fact]
    public async Task ServiceRejectionReasonIsVisibleEvenWithoutErrSegments()
    {
        var reason = "El establecimiento remitente no está habilitado.";
        var html = await RenderAsync(new(Outcome.Rejected, reason, "AR", ReasonCode: AssessmentReason.ApplicationRejected));
        var table = html[..html.IndexOf("</tbody>", StringComparison.Ordinal)];
        Assert.Contains(reason, WebUtility.HtmlDecode(table));
    }

    private static async Task<string> RenderAsync(Assessment assessment, DateTimeOffset? timestamp = null, ReportContext? context = null)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".html");
        var instant = timestamp ?? DateTimeOffset.UtcNow;
        var receipt = new Receipt(Guid.NewGuid(), "a.hl7", "source-hash", "source", instant, instant.AddMilliseconds(16), assessment);
        try
        {
            await HtmlReport.WriteAsync(path, [receipt], default, context: context);
            return await File.ReadAllTextAsync(path);
        }
        finally { File.Delete(path); }
    }
}
