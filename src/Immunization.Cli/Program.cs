using Immunization.Application;
using Immunization.Domain;
using Immunization.Infrastructure;

if (args.Length == 3 && args[0] == "report")
{
    try
    {
        var snapshot = JournalReader.Read(args[1]);
        await HtmlReport.WriteAsync(args[2], snapshot.Completed, CancellationToken.None, snapshot.Pending, snapshot.Context);
        Console.WriteLine($"Restricted HTML report: {args[2]}");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
    {
        Console.Error.WriteLine($"Report generation failed ({ex.GetType().Name}). Existing files are never overwritten.");
        return 3;
    }
}
if (args.Length != 4 || args[0] is not ("run" or "echo"))
{
    Console.Error.WriteLine("Usage: immunization <run|echo> <config.json> <input-directory> <new-report.jsonl> | report <journal.jsonl> <new-report.html>");
    return 64;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var options = RegistryOptions.Load(args[1]);
    var credentials = RegistryCredentials.Load();
    using var client = HttpRegistryGateway.CreateClient(options, options.UseBasicAuthentication ? ApiCredentials.Load() : null);
    var gateway = new HttpRegistryGateway(client, options, credentials, new AcknowledgmentInterpreter(), TimeProvider.System);
    if (!await gateway.EchoAsync(cancellation.Token))
    {
        Console.Error.WriteLine("Echo failed. No messages sent.");
        return 2;
    }
    Console.WriteLine("Echo: service returned the documented echoBack response.");
    if (args[0] == "echo") return 0;
    var submissions = await InputReader.ReadAsync(args[2], options.MaxInputBytes, cancellation.Token, options.MaxBatchBytes, options.MaxFiles);
    var context = new ReportContext(options.EnvironmentName, options.IsSimulation, options.ReportTimeZoneId, new Uri(options.BaseUrl).Host);
    using var journal = new JsonJournal(args[3], context);
    var receipts = await new BatchProcessor(gateway, journal, TimeProvider.System).RunAsync(submissions, cancellation.Token);
    await HtmlReport.WriteAsync(args[3] + ".html", receipts, CancellationToken.None, context: context);
    foreach (var receipt in receipts) Console.WriteLine($"{receipt.Id} {receipt.Assessment.Outcome}");
    foreach (var group in receipts.GroupBy(x => x.Assessment.Outcome)) Console.WriteLine($"{group.Key}: {group.Count()}");
    Console.WriteLine($"Completed: {receipts.Count}/{submissions.Count}. Restricted report: {args[3]}");
    return receipts.Count != submissions.Count || receipts.Any(x => x.Assessment.Outcome == Outcome.Unknown) ? 2 :
        receipts.Any(x => x.Assessment.Outcome is Outcome.Rejected or Outcome.Observed) ? 1 : 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
    System.Text.Json.JsonException or ArgumentException or FormatException or HttpRequestException or OperationCanceledException)
{
    Console.Error.WriteLine($"Execution stopped ({ex.GetType().Name}). Inspect configuration and restricted journal; do not retry uncertain deliveries automatically.");
    return 3;
}
