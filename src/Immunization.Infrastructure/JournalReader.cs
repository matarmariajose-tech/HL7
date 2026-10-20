using System.Text.Json;
using System.Text.Json.Serialization;
using Immunization.Domain;

namespace Immunization.Infrastructure;

public sealed record PendingTransaction(Guid Id, string FileName, string Sha256, string? MessageId, DateTimeOffset StartedAt);
public sealed record JournalSnapshot(IReadOnlyList<Receipt> Completed, IReadOnlyList<PendingTransaction> Pending, ReportContext? Context = null);

public static class JournalReader
{
    public static JournalSnapshot Read(string path)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        ReportContext? context = null;
        var pending = new Dictionary<Guid, PendingTransaction>();
        var completed = new List<Receipt>();
        var completedIds = new HashSet<Guid>();
        foreach (var line in File.ReadLines(path))
        {
            using var entry = JsonDocument.Parse(line);
            var root = entry.RootElement;
            switch (root.GetProperty("State").GetString())
            {
                case "Batch":
                    if (context is not null || pending.Count > 0 || completed.Count > 0)
                        throw new InvalidDataException("Batch context must precede transaction events.");
                    context = root.GetProperty("Context").Deserialize<ReportContext>(options)
                        ?? throw new InvalidDataException("Missing batch context.");
                    break;
                case "Pending":
                    var started = root.Deserialize<PendingTransaction>(options) ?? throw new InvalidDataException("Invalid pending event.");
                    if (completedIds.Contains(started.Id) || !pending.TryAdd(started.Id, started))
                        throw new InvalidDataException("Duplicate transaction event.");
                    break;
                case "Completed":
                    var receipt = root.GetProperty("Receipt").Deserialize<Receipt>(options) ?? throw new InvalidDataException("Invalid completed event.");
                    if (!pending.Remove(receipt.Id, out var source) || !completedIds.Add(receipt.Id) ||
                        source.Sha256 != receipt.Sha256 || source.FileName != receipt.FileName || source.MessageId != receipt.MessageId)
                        throw new InvalidDataException("Completed event does not match a pending transaction.");
                    completed.Add(receipt);
                    break;
                default: throw new InvalidDataException("Unsupported journal event.");
            }
        }
        return new(completed, pending.Values.OrderBy(x => x.StartedAt).ToArray(), context);
    }
}
