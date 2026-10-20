using System.Net;
using System.Text;
using Immunization.Domain;

namespace Immunization.Infrastructure;

public static class HtmlReport
{
    public static async Task WriteAsync(string path, IReadOnlyList<Receipt> receipts, CancellationToken token, IReadOnlyList<PendingTransaction>? pending = null)
    {
        var html = new StringBuilder("<!doctype html><html lang=\"es\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Informe de inmunizaciones</title><style>body{font:16px system-ui;margin:2rem auto;max-width:1100px;padding:0 1rem;color:#172c3b;background:#f4f7fa}article{background:white;border:1px solid #d8e1e8;border-radius:12px;padding:1.5rem;margin:1rem 0}dt{font-weight:600;margin-top:.8rem}dd{margin:.2rem 0;overflow-wrap:anywhere}small{color:#4f6070}li{margin:.5rem 0}.Accepted{border-left:6px solid #157347}.Observed{border-left:6px solid #997404}.Rejected{border-left:6px solid #b02a37}.Unknown{border-left:6px solid #526477}</style><h1>Informe de inmunizaciones</h1><p>Documento restringido. Contiene identificadores y motivos del servicio. No compartir públicamente.</p>");
        html.Append("<p>").Append(receipts.Count).Append(" transacciones completadas.</p><ul>");
        foreach (var outcome in Enum.GetValues<Outcome>())
            html.Append("<li>").Append(outcome).Append(": ").Append(receipts.Count(r => r.Assessment.Outcome == outcome)).Append("</li>");
        html.Append("</ul><p>Intentos sin cierre: ").Append(pending?.Count ?? 0).Append(".</p>");
        foreach (var receipt in receipts)
        {
            html.Append("<article class=\"").Append(receipt.Assessment.Outcome).Append("\"><h2>")
                .Append(Encode(receipt.FileName)).Append("</h2><p><strong>").Append(receipt.Assessment.Outcome)
                .Append("</strong> — ").Append(Encode(receipt.Assessment.Reason)).Append("</p><dl>");
            Entry(html, "Transacción", receipt.Id.ToString());
            Entry(html, "MSH-10", receipt.MessageId ?? "Ausente");
            Entry(html, "Identificador del servicio", receipt.Assessment.ServiceMessageId?.ToString() ?? "No disponible");
            Entry(html, "Acuse MSA-1", receipt.Assessment.AcknowledgmentCode ?? "No disponible");
            Entry(html, "Inicio UTC", receipt.StartedAt.ToString("O"));
            Entry(html, "Fin UTC", receipt.CompletedAt.ToString("O"));
            Entry(html, "SHA-256 del archivo", receipt.Sha256);
            html.Append("</dl><ul>");
            foreach (var finding in receipt.Assessment.Findings ?? [])
                html.Append("<li>").Append(Encode($"{finding.Severity} · {finding.Code} · {finding.Location} · {finding.Description}")).Append("</li>");
            html.Append("</ul></article>");
        }
        foreach (var transaction in pending ?? [])
        {
            html.Append("<article class=\"Unknown\"><h2>").Append(Encode(transaction.FileName))
                .Append("</h2><p><strong>Unknown · Pending</strong> — Inicio registrado sin respuesta persistida. Entrega incierta; reconciliar antes de reenviar.</p><dl>");
            Entry(html, "Transacción", transaction.Id.ToString());
            Entry(html, "Inicio UTC", transaction.StartedAt.ToString("O"));
            Entry(html, "SHA-256 del archivo", transaction.Sha256);
            html.Append("</dl></article>");
        }
        html.Append("<p><small>Unknown no confirma aceptación ni rechazo. Reconciliar antes de reenviar.</small></p></html>");
        var settings = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read };
        if (!OperatingSystem.IsWindows()) settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var stream = new FileStream(path, settings);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(html.ToString().AsMemory(), token);
    }
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static void Entry(StringBuilder html, string label, string value) =>
        html.Append("<dt>").Append(Encode(label)).Append("</dt><dd>").Append(Encode(value)).Append("</dd>");
}
