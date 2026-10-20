namespace Immunization.Domain;

public sealed class Hl7
{
    private readonly string[][] segments;
    private readonly char separator;
    public char Component { get; }
    public char Repetition { get; }
    public char Escape { get; }
    public Hl7(string content)
    {
        var lines = content.Trim('\uFEFF', '\u000b', '\u001c', '\r', '\n')
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("MSH", StringComparison.Ordinal) || lines[0].Length < 9)
            throw new FormatException("Missing MSH header.");
        separator = lines[0][3];
        if (char.IsLetterOrDigit(separator) || char.IsWhiteSpace(separator))
            throw new FormatException("Invalid HL7 separator.");
        segments = lines.Select(x => x.Split(separator)).ToArray();
        var encoding = Field(segments[0], 2);
        if (encoding.Length != 4 || (encoding + separator).Distinct().Count() != 5)
            throw new FormatException("Invalid encoding characters.");
        Component = encoding[0]; Repetition = encoding[1]; Escape = encoding[2];
    }
    public IEnumerable<string[]> Segments(string name) => segments.Where(x => x[0] == name);
    public string Field(string[] segment, int index)
    {
        if (segment[0] == "MSH" && index == 1) return separator.ToString();
        var offset = segment[0] == "MSH" ? index - 1 : index;
        return offset >= 0 && offset < segment.Length ? segment[offset] : "";
    }
    public string MessageId => Field(segments[0], 10);
    public string Part(string value, int index) => value.Split(Component).ElementAtOrDefault(index - 1) ?? "";
    public string Decode(string value) => value
        .Replace($"{Escape}F{Escape}", separator.ToString(), StringComparison.Ordinal)
        .Replace($"{Escape}S{Escape}", Component.ToString(), StringComparison.Ordinal)
        .Replace($"{Escape}R{Escape}", Repetition.ToString(), StringComparison.Ordinal)
        .Replace($"{Escape}E{Escape}", Escape.ToString(), StringComparison.Ordinal);
}
