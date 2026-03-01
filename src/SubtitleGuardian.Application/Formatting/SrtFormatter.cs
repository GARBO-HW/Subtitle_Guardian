using System.Globalization;
using System.Text;
using SubtitleGuardian.Domain.Contracts;

namespace SubtitleGuardian.Application.Formatting;

public sealed record SrtFormatOptions(
    int MaxCueChars = 84,
    int MaxCueDurationMs = 6000,
    int MaxLineChars = 42
);

public sealed class SrtFormatter
{
    public string Format(IReadOnlyList<Segment> segments, SrtFormatOptions? options = null)
    {
        options ??= new SrtFormatOptions();
        SegmentContract.EnsureValid(segments);

        var cues = new List<Segment>();
        foreach (Segment s in segments)
        {
            cues.AddRange(SplitSegment(s, options));
        }

        SegmentContract.EnsureValid(cues);

        var sb = new StringBuilder(cues.Count * 64);
        for (int i = 0; i < cues.Count; i++)
        {
            Segment c = cues[i];
            sb.Append(i + 1).Append('\n');
            sb.Append(FormatTime(c.StartMs)).Append(" --> ").Append(FormatTime(c.EndMs)).Append('\n');

            foreach (string line in WrapLines(NormalizeText(c.Text), options.MaxLineChars))
            {
                sb.Append(line).Append('\n');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    public void WriteToFile(string filePath, IReadOnlyList<Segment> segments, SrtFormatOptions? options = null)
    {
        string content = Format(segments, options);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static IEnumerable<Segment> SplitSegment(Segment s, SrtFormatOptions options)
    {
        string text = NormalizeText(s.Text);
        long duration = s.EndMs - s.StartMs;

        if (duration <= options.MaxCueDurationMs && text.Length <= options.MaxCueChars)
        {
            yield return s with { Text = text };
            yield break;
        }

        string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length <= 1)
        {
            int parts = Math.Max(
                (int)Math.Ceiling(duration / (double)options.MaxCueDurationMs),
                (int)Math.Ceiling(text.Length / (double)options.MaxCueChars)
            );

            parts = Math.Clamp(parts, 1, 20);

            long baseLen = duration / parts;
            long rem = duration % parts;

            int baseChars = text.Length / parts;
            int remChars = text.Length % parts;

            long cursor = s.StartMs;
            int charPos = 0;
            for (int i = 0; i < parts; i++)
            {
                long partLen = baseLen + (i < rem ? 1 : 0);
                int charCount = baseChars + (i < remChars ? 1 : 0);
                string partText = text.Substring(charPos, Math.Min(charCount, text.Length - charPos)).Trim();

                long start = cursor;
                long end = i == parts - 1 ? s.EndMs : cursor + partLen;
                cursor = end;
                charPos += charCount;

                if (partText.Length == 0)
                {
                    partText = text;
                }

                yield return new Segment(start, end, partText, null);
            }

            yield break;
        }

        var partsTexts = new List<string>();
        var current = new StringBuilder();

        for (int i = 0; i < tokens.Length; i++)
        {
            string t = tokens[i];
            bool wouldOverflow = current.Length > 0 && current.Length + 1 + t.Length > options.MaxCueChars;
            if (wouldOverflow)
            {
                partsTexts.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }
            current.Append(t);
        }

        if (current.Length > 0)
        {
            partsTexts.Add(current.ToString());
        }

        int minByDuration = (int)Math.Ceiling(duration / (double)options.MaxCueDurationMs);
        while (partsTexts.Count < minByDuration && partsTexts.Count < 20)
        {
            int idx = IndexOfLongest(partsTexts);
            string v = partsTexts[idx];
            int mid = v.Length / 2;
            int splitAt = v.LastIndexOf(' ', mid);
            if (splitAt <= 0 || splitAt >= v.Length - 1)
            {
                break;
            }

            string left = v.Substring(0, splitAt).Trim();
            string right = v.Substring(splitAt + 1).Trim();
            partsTexts[idx] = left;
            partsTexts.Insert(idx + 1, right);
        }

        long totalWeight = partsTexts.Sum(p => Math.Max(1, p.Length));
        long cursorMs = s.StartMs;

        for (int i = 0; i < partsTexts.Count; i++)
        {
            string partText = partsTexts[i].Trim();
            long weight = Math.Max(1, partText.Length);

            long partDur = i == partsTexts.Count - 1
                ? s.EndMs - cursorMs
                : (long)Math.Round(duration * (weight / (double)totalWeight), MidpointRounding.AwayFromZero);

            partDur = Math.Max(1, partDur);
            long start = cursorMs;
            long end = i == partsTexts.Count - 1 ? s.EndMs : Math.Min(s.EndMs, cursorMs + partDur);

            if (end <= start)
            {
                end = Math.Min(s.EndMs, start + 1);
            }

            cursorMs = end;
            yield return new Segment(start, end, partText, null);
        }
    }

    private static int IndexOfLongest(IReadOnlyList<string> items)
    {
        int idx = 0;
        int best = -1;
        for (int i = 0; i < items.Count; i++)
        {
            int len = items[i].Length;
            if (len > best)
            {
                best = len;
                idx = i;
            }
        }
        return idx;
    }

    private static string NormalizeText(string text)
    {
        return string.Join(' ', text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }

    private static IEnumerable<string> WrapLines(string text, int maxLineChars)
    {
        if (maxLineChars <= 0 || text.Length <= maxLineChars)
        {
            yield return text;
            yield break;
        }

        string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();

        foreach (string t in tokens)
        {
            bool wouldOverflow = line.Length > 0 && line.Length + 1 + t.Length > maxLineChars;
            if (wouldOverflow)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }
            line.Append(t);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    private static string FormatTime(long ms)
    {
        if (ms < 0)
        {
            ms = 0;
        }

        long hours = ms / 3_600_000;
        ms %= 3_600_000;
        long minutes = ms / 60_000;
        ms %= 60_000;
        long seconds = ms / 1_000;
        long millis = ms % 1_000;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:00}:{minutes:00}:{seconds:00},{millis:000}"
        );
    }
}

