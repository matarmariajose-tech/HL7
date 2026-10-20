using System.Text.Json;
using System.Text.Json.Serialization;
using Immunization.Application;
using Immunization.Domain;

namespace Immunization.Infrastructure;

public sealed class JsonJournal : ITransactionJournal, IDisposable
{
    private readonly FileStream stream;
    private readonly JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter() } };
    public JsonJournal(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var settings = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read };
        if (!OperatingSystem.IsWindows()) settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        stream = new FileStream(path, settings);
    }
    public Task RecordPendingAsync(Submission submission, DateTimeOffset startedAt, CancellationToken cancellationToken) =>
        AppendAsync(new { State = "Pending", submission.Id, submission.FileName, submission.Sha256, submission.MessageId, StartedAt = startedAt }, cancellationToken);
    public Task RecordCompletedAsync(Receipt receipt, CancellationToken cancellationToken) =>
        AppendAsync(new { State = "Completed", Receipt = receipt }, cancellationToken);
    private async Task AppendAsync<T>(T value, CancellationToken token)
    {
        await JsonSerializer.SerializeAsync(stream, value, options, token);
        await stream.WriteAsync("\n"u8.ToArray(), token);
        await stream.FlushAsync(token);
        stream.Flush(true);
    }
    public void Dispose() => stream.Dispose();
}
