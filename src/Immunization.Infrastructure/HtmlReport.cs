using System.Globalization;
using System.Net;
using System.Text;
using Immunization.Domain;
using Immunization.Infrastructure.Reporting;

namespace Immunization.Infrastructure;

public static class HtmlReport
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-AR");
    private static readonly Outcome[] Outcomes = Enum.GetValues<Outcome>();
    private static readonly string[] Plurals = ["Aceptados", "Con observaciones", "Rechazados", "Sin confirmación"];

    public static async Task WriteAsync(string path, IReadOnlyList<Receipt> receipts, CancellationToken token,
        IReadOnlyList<PendingTransaction>? pending = null, ReportContext? context = null)
    {
        pending ??= [];
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(context?.TimeZoneId ?? "America/Argentina/Buenos_Aires");
        var total = receipts.Count + pending.Count;
        var times = receipts.Select(r => r.StartedAt).Concat(pending.Select(p => p.StartedAt)).ToArray();
        var html = new StringBuilder("<!doctype html><html lang=\"es-AR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"referrer\" content=\"no-referrer\"><title>Resultado del lote · Inmunizaciones</title><style>");
        html.Append(Asset("report.css")).Append("</style></head><body><main class=\"page\"><div class=\"masthead\"><div class=\"identity\"><span class=\"mark\">HL7</span><div><div class=\"product\">Registro de inmunizaciones</div><div class=\"department\">Departamento de Interfaces</div></div></div><span class=\"internal\">Uso interno · Acceso restringido</span></div><header><div><h1>Resultado del lote</h1><p class=\"metadata\">");
        if (times.Length > 0) html.Append(Encode(LocalTime(times.Min(), timezone))).Append(" · ");
        html.Append(total).Append(" archivos · ").Append(receipts.Count).Append(" resultados registrados</p></div><button class=\"print\" data-print type=\"button\">Imprimir informe</button></header>");
        RenderEnvironment(html, context);
        html.Append("<section class=\"metrics\" aria-label=\"Resumen del lote\"><div class=\"metric\"><strong>").Append(total).Append("</strong><span>Archivos del lote</span>");
        if (pending.Count > 0) html.Append("<small>").Append(pending.Count).Append(" sin cierre registrado</small>");
        html.Append("</div>");
        foreach (var outcome in Outcomes)
        {
            var count = receipts.Count(r => r.Assessment.Outcome == outcome) + (outcome == Outcome.Unknown ? pending.Count : 0);
            html.Append("<div class=\"metric\"><strong>").Append(count).Append("</strong><span>").Append(Plurals[(int)outcome]).Append("</span></div>");
        }
        html.Append("</section><section aria-labelledby=\"results-title\"><div class=\"section-title\"><h2 id=\"results-title\">Resultados por archivo</h2><p id=\"visible-count\" aria-live=\"polite\">").Append(total).Append(" de ").Append(total).Append(" archivos</p></div><div class=\"tools\"><div class=\"filters\" role=\"group\" aria-label=\"Filtrar por resultado\"><button type=\"button\" data-filter=\"\" aria-pressed=\"true\">Todos</button>");
        foreach (var outcome in Outcomes)
            html.Append("<button type=\"button\" data-filter=\"").Append(outcome).Append("\" aria-pressed=\"false\">").Append(Plurals[(int)outcome]).Append("</button>");
        html.Append("</div><label class=\"sr-only\" for=\"search\">Buscar por archivo o identificador</label><input class=\"search\" id=\"search\" type=\"search\" placeholder=\"Buscar archivo o identificador\" autocomplete=\"off\"></div><div class=\"table-frame\"><table><caption class=\"sr-only\">Resultado, motivo y acuse de cada archivo del lote</caption><thead><tr><th scope=\"col\">Archivo</th><th scope=\"col\">Resultado</th><th scope=\"col\">Motivo</th><th scope=\"col\">Acuse</th><th scope=\"col\" class=\"duration\">Tiempo</th></tr></thead><tbody>");
        var index = 0;
        foreach (var receipt in receipts) RenderRow(html, receipt, ++index);
        foreach (var transaction in pending) RenderPendingRow(html, transaction, ++index);
        html.Append("</tbody></table><p id=\"empty\" class=\"empty\" hidden>No hay archivos que coincidan con la búsqueda.</p></div><p class=\"uncertainty\"><strong>Sin confirmación</strong> significa que no hay evidencia suficiente para afirmar aceptación o rechazo. Verificar el estado con el registro antes de reenviar.</p></section><section aria-labelledby=\"audit-title\"><div class=\"section-title\"><h2 id=\"audit-title\">Detalle de las transacciones</h2><p>Identificadores, acuses y hallazgos originales</p></div><div class=\"audit-list\">");
        foreach (var receipt in receipts) RenderAudit(html, receipt, timezone);
        foreach (var transaction in pending) RenderPendingAudit(html, transaction, timezone);
        html.Append("</div></section><footer class=\"footer\"><span>Informe de uso interno. Puede contener información sensible del servicio.</span><span>Los motivos originales se conservan en el detalle y en el diario JSONL.</span></footer></main><script>")
            .Append(Asset("report.js")).Append("</script></body></html>");
        var settings = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read };
        if (!OperatingSystem.IsWindows()) settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var stream = new FileStream(path, settings);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(html.ToString().AsMemory(), token);
    }

    private static void RenderEnvironment(StringBuilder html, ReportContext? context)
    {
        html.Append("<div class=\"environment").Append(context?.IsSimulation == true ? "" : " external").Append("\">");
        if (context is null) html.Append("<strong>Origen no registrado</strong><span>Este diario no incluye información del entorno de ejecución.</span>");
        else if (context.IsSimulation) html.Append("<strong>Simulación local</strong><span>Respuestas de prueba. Este lote no se envió al registro externo.</span>");
        else html.Append("<strong>").Append(Encode(context.EnvironmentName)).Append("</strong><span>").Append(Encode(context.Host ?? "Servicio externo")).Append("</span>");
        html.Append("</div>");
    }

    private static void RenderRow(StringBuilder html, Receipt receipt, int index)
    {
        var assessment = receipt.Assessment;
        html.Append("<tr data-status=\"").Append(assessment.Outcome).Append("\" data-search=\"")
            .Append(Encode(Search(receipt))).Append("\"><td class=\"file\"><a href=\"#tx-").Append(receipt.Id)
            .Append("\" data-audit=\"tx-").Append(receipt.Id).Append("\">").Append(Encode(receipt.FileName)).Append("</a><small>Archivo ")
            .Append(index.ToString("D2", CultureInfo.InvariantCulture)).Append("</small></td><td>");
        Status(html, assessment.Outcome);
        html.Append("</td><td class=\"reason\">").Append(Encode(ReportText.Summary(assessment)));
        if (assessment.Findings is { Count: > 0 } findings)
            html.Append("<small>").Append(findings.Count).Append(findings.Count == 1 ? " hallazgo del servicio" : " hallazgos del servicio").Append(" · Ver detalle</small>");
        html.Append("</td><td class=\"ack\">").Append(Encode(assessment.AcknowledgmentCode ?? "—"))
            .Append("</td><td class=\"duration\">").Append(Duration(receipt.CompletedAt - receipt.StartedAt)).Append("</td></tr>");
    }

    private static void RenderPendingRow(StringBuilder html, PendingTransaction transaction, int index)
    {
        html.Append("<tr data-status=\"Unknown\" data-search=\"").Append(Encode(Search(transaction)))
            .Append("\"><td class=\"file\"><a href=\"#tx-").Append(transaction.Id).Append("\" data-audit=\"tx-")
            .Append(transaction.Id).Append("\">").Append(Encode(transaction.FileName)).Append("</a><small>Archivo ")
            .Append(index.ToString("D2", CultureInfo.InvariantCulture)).Append(" · Sin cierre</small></td><td>");
        Status(html, Outcome.Unknown);
        html.Append("</td><td class=\"reason\">El envío comenzó, pero no se registró su resultado.</td><td class=\"ack\">—</td><td class=\"duration\">—</td></tr>");
    }

    private static void RenderAudit(StringBuilder html, Receipt receipt, TimeZoneInfo timezone)
    {
        OpenAudit(html, receipt.Id, receipt.FileName, receipt.Assessment.Outcome, Search(receipt));
        html.Append("<dl class=\"audit-grid\">");
        Entry(html, "Transacción", receipt.Id.ToString());
        Entry(html, "MSH-10 · ID del mensaje", receipt.MessageId ?? "No disponible");
        Entry(html, "ID del servicio", receipt.Assessment.ServiceMessageId?.ToString() ?? "No disponible");
        Entry(html, "Acuse MSA-1", receipt.Assessment.AcknowledgmentCode ?? "No disponible");
        Entry(html, "Inicio", LocalTime(receipt.StartedAt, timezone));
        Entry(html, "Fin", LocalTime(receipt.CompletedAt, timezone));
        Entry(html, "Fechas UTC originales", $"{receipt.StartedAt:O} → {receipt.CompletedAt:O}", true);
        Entry(html, "SHA-256 del archivo", receipt.Sha256, true);
        if (receipt.Assessment.HttpStatusCode is { } httpStatus)
            Entry(html, "Estado HTTP", httpStatus.ToString(CultureInfo.InvariantCulture));
        if (receipt.Assessment.Evidence is { } evidence)
        {
            Entry(html, "Tipo de contenido", evidence.MediaType ?? "No informado");
            if (evidence.ContractField is not null) Entry(html, "Campo de contrato inválido", evidence.ContractField);
            Entry(html, "Tamaño de la respuesta", evidence.BodyBytes.ToString("N0", Culture) + " bytes");
            Entry(html, "Formato aparente de result", KindLabel(evidence.ResultKind));
            Entry(html, "Longitud de result", evidence.ResultCharacters?.ToString("N0", Culture) ?? "No disponible");
            Entry(html, "SHA-256 de la respuesta HTTP", evidence.BodySha256, true);
            if (evidence.ResultSha256 is not null) Entry(html, "SHA-256 de result", evidence.ResultSha256, true);
        }
        html.Append("</dl><p class=\"diagnostic\"><strong>Diagnóstico original</strong>")
            .Append(Encode(receipt.Assessment.Reason)).Append("</p>");
        if (receipt.Assessment.Findings is { Count: > 0 } findings)
        {
            html.Append("<ul class=\"findings\" aria-label=\"Hallazgos del registro\">");
            foreach (var finding in findings)
            {
                html.Append("<li>").Append(Encode(string.IsNullOrWhiteSpace(finding.Description) ? "El servicio no proporcionó una descripción." : finding.Description))
                    .Append("<span>Severidad: ").Append(Encode(finding.Severity)).Append(" · Ubicación: ")
                    .Append(Encode(finding.Location)).Append(" · Código: ").Append(Encode(finding.Code)).Append("</span></li>");
            }
            html.Append("</ul>");
        }
        html.Append("</div></details>");
    }

    private static void RenderPendingAudit(StringBuilder html, PendingTransaction transaction, TimeZoneInfo timezone)
    {
        OpenAudit(html, transaction.Id, transaction.FileName, Outcome.Unknown, Search(transaction));
        html.Append("<p class=\"diagnostic\"><strong>Sin cierre registrado</strong>Hay un evento de inicio, pero no una respuesta persistida. La entrega es incierta.</p><dl class=\"audit-grid\">");
        Entry(html, "Transacción", transaction.Id.ToString());
        Entry(html, "MSH-10 · ID del mensaje", transaction.MessageId ?? "No disponible");
        Entry(html, "Inicio", LocalTime(transaction.StartedAt, timezone));
        Entry(html, "SHA-256 del archivo", transaction.Sha256, true);
        html.Append("</dl></div></details>");
    }

    private static void OpenAudit(StringBuilder html, Guid id, string fileName, Outcome outcome, string search)
    {
        html.Append("<details id=\"tx-").Append(id).Append("\" data-status=\"").Append(outcome).Append("\" data-search=\"")
            .Append(Encode(search)).Append("\"><summary><span class=\"name\">").Append(Encode(fileName))
            .Append("</span><span class=\"transaction\">").Append(id.ToString()[..8]).Append("</span>");
        Status(html, outcome);
        html.Append("</summary><div class=\"audit-content\">");
    }
    private static void Status(StringBuilder html, Outcome outcome) => html.Append("<span class=\"status ")
        .Append(outcome).Append("\">").Append(ReportText.Status(outcome)).Append("</span>");
    private static void Entry(StringBuilder html, string label, string value, bool wide = false) => html.Append(wide ? "<div class=\"wide\">" : "<div>")
        .Append("<dt>").Append(Encode(label)).Append("</dt><dd>").Append(Encode(value)).Append("</dd></div>");
    private static string LocalTime(DateTimeOffset instant, TimeZoneInfo timezone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, timezone);
        return local.ToString("dd MMM yyyy · HH:mm:ss", Culture) + " (UTC" + local.ToString("zzz", CultureInfo.InvariantCulture) + ")";
    }
    private static string Duration(TimeSpan duration) => duration.TotalSeconds < 1
        ? Math.Max(0, duration.TotalMilliseconds).ToString("N0", Culture) + " ms"
        : duration.TotalSeconds.ToString("N2", Culture) + " s";
    private static string KindLabel(ResponseKind kind) => kind switch
    {
        ResponseKind.Empty => "Vacío",
        ResponseKind.Missing => "Ausente o nulo",
        ResponseKind.Hl7Like => "Inicio de mensaje HL7",
        ResponseKind.JsonLike => "Estructura JSON aparente",
        ResponseKind.Markup => "Marcado XML/HTML",
        _ => "Texto"
    };
    private static string Search(Receipt receipt) => $"{receipt.FileName} {receipt.Id} {receipt.MessageId} {receipt.Assessment.ServiceMessageId}";
    private static string Search(PendingTransaction transaction) => $"{transaction.FileName} {transaction.Id} {transaction.MessageId}";
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static string Asset(string name)
    {
        using var stream = typeof(HtmlReport).Assembly.GetManifestResourceStream($"Immunization.Infrastructure.Reporting.{name}")
            ?? throw new InvalidOperationException($"Missing report resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
