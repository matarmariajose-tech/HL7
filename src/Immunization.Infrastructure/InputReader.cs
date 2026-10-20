using System.Security.Cryptography;
using System.Text;
using Immunization.Domain;

namespace Immunization.Infrastructure;

public static class InputReader
{
    public static async Task<IReadOnlyList<Submission>> ReadAsync(string directory, long maxBytes, CancellationToken token,
        long maxBatchBytes = 33554432, int maxFiles = 1000)
    {
        if (maxBytes < 1 || maxBatchBytes < 1 || maxFiles < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        var files = new List<string>();
        long declaredBytes = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetExtension(path), ".hl7", StringComparison.OrdinalIgnoreCase)) continue;
            if (files.Count == maxFiles) throw new InvalidDataException("Input exceeds configured file count limit.");
            var info = new FileInfo(path);
            if (info.LinkTarget is not null) throw new InvalidDataException("Symbolic links are not accepted as input files.");
            if (info.Length > maxBytes || info.Length > maxBatchBytes - declaredBytes)
                throw new InvalidDataException("Input exceeds configured file or batch size limit.");
            declaredBytes += info.Length;
            files.Add(path);
        }
        if (files.Count == 0) throw new InvalidDataException("No HL7 files found.");
        files.Sort(StringComparer.Ordinal);
        var submissions = new List<Submission>(files.Count);
        long consumedBytes = 0;
        foreach (var file in files)
        {
            var limit = Math.Min(maxBytes, maxBatchBytes - consumedBytes);
            var bytes = await ReadBoundedAsync(file, limit, token);
            consumedBytes += bytes.LongLength;
            var text = new UTF8Encoding(false, true).GetString(bytes);
            string? messageId;
            try { messageId = new Hl7(text).MessageId; }
            catch (FormatException) { messageId = null; }
            submissions.Add(new(Guid.NewGuid(), Path.GetFileName(file), Convert.ToHexString(SHA256.HashData(bytes)), messageId, text));
        }
        return submissions;
    }
    private static async Task<byte[]> ReadBoundedAsync(string path, long limit, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        int length;
        while ((length = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (length > limit - content.Length) throw new InvalidDataException("Input grew beyond its configured size limit.");
            content.Write(buffer, 0, length);
        }
        return content.ToArray();
    }
}
