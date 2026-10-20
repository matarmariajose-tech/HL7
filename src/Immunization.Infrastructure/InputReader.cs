using System.Security.Cryptography;
using System.Text;
using Immunization.Domain;

namespace Immunization.Infrastructure;

public static class InputReader
{
    public static async Task<IReadOnlyList<Submission>> ReadAsync(string directory, long maxBytes, CancellationToken token)
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(p => string.Equals(Path.GetExtension(p), ".hl7", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0) throw new InvalidDataException("No HL7 files found.");
        var submissions = new List<Submission>();
        foreach (var file in files)
        {
            if (new FileInfo(file).Length > maxBytes) throw new InvalidDataException("Input exceeds configured size limit.");
            var bytes = await File.ReadAllBytesAsync(file, token);
            if (bytes.LongLength > maxBytes) throw new InvalidDataException("Input exceeds configured size limit.");
            var text = new UTF8Encoding(false, true).GetString(bytes);
            string? messageId;
            try { messageId = new Hl7(text).MessageId; }
            catch (FormatException) { messageId = null; }
            submissions.Add(new(Guid.NewGuid(), Path.GetFileName(file), Convert.ToHexString(SHA256.HashData(bytes)), messageId, text));
        }
        return submissions;
    }
}
