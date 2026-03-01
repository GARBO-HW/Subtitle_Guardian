using System.Text;
using SubtitleGuardian.Domain.Contracts;

namespace SubtitleGuardian.Application.Formatting;

public sealed record TxtFormatOptions(
    bool BlankLineBetweenSegments = true
);

public sealed class TxtFormatter
{
    public string Format(IReadOnlyList<Segment> segments, TxtFormatOptions? options = null)
    {
        options ??= new TxtFormatOptions();
        SegmentContract.EnsureValid(segments);

        var sb = new StringBuilder(segments.Count * 32);
        bool first = true;

        foreach (Segment s in segments)
        {
            string text = NormalizeText(s.Text);
            if (text.Length == 0)
            {
                continue;
            }

            if (!first)
            {
                sb.Append('\n');
                if (options.BlankLineBetweenSegments)
                {
                    sb.Append('\n');
                }
            }

            sb.Append(text);
            first = false;
        }

        if (sb.Length > 0)
        {
            sb.Append('\n');
        }

        return sb.ToString();
    }

    public void WriteToFile(string filePath, IReadOnlyList<Segment> segments, TxtFormatOptions? options = null)
    {
        string content = Format(segments, options);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string NormalizeText(string text)
    {
        return string.Join(' ', text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }
}

