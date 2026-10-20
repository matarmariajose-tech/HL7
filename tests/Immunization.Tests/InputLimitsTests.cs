using System.Text;
using Immunization.Infrastructure;
using Xunit;

namespace Immunization.Tests;

public sealed class InputLimitsTests
{
    [Fact]
    public async Task BatchBudgetIsEnforcedEvenWhenEveryFileFitsIndividually()
    {
        var directory = Directory.CreateTempSubdirectory("immunization-input-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "a.hl7"), "123456", new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(directory, "b.hl7"), "123456", new UTF8Encoding(false));
            await Assert.ThrowsAsync<InvalidDataException>(() => InputReader.ReadAsync(directory, 10, default, maxBatchBytes: 10));
            Assert.Equal("123456", await File.ReadAllTextAsync(Path.Combine(directory, "a.hl7")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CardinalityBudgetPreventsUnboundedDirectoryMaterialization()
    {
        var directory = Directory.CreateTempSubdirectory("immunization-input-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "a.hl7"), "a");
            await File.WriteAllTextAsync(Path.Combine(directory, "b.hl7"), "b");
            await Assert.ThrowsAsync<InvalidDataException>(() => InputReader.ReadAsync(directory, 100, default, maxFiles: 1));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InvalidUtf8FailsBeforeAnyBatchCanBeSubmitted()
    {
        var directory = Directory.CreateTempSubdirectory("immunization-input-").FullName;
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "a.hl7"), [0xFF, 0xFE]);
            await Assert.ThrowsAsync<DecoderFallbackException>(() => InputReader.ReadAsync(directory, 100, default));
        }
        finally { Directory.Delete(directory, true); }
    }
}
