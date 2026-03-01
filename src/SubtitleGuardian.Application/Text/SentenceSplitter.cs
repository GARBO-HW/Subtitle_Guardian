using System.Text;
using SubtitleGuardian.Domain.Text;

namespace SubtitleGuardian.Application.Text;

public sealed class SentenceSplitter
{
    private static readonly HashSet<char> EndPunct = new HashSet<char>
    {
        '.', '!', '?', '。', '！', '？'
    };

    public IReadOnlyList<string> Split(string input, SentenceSplitOptions? options = null)
    {
        options ??= new SentenceSplitOptions();
        string s = input ?? string.Empty;

        var result = new List<string>();
        var buf = new StringBuilder();

        void flush(bool forced)
        {
            string v = buf.ToString().Trim();
            buf.Clear();

            if (v.Length < options.MinSentenceLength)
            {
                return;
            }

            if (v.Length <= options.MaxSentenceLength)
            {
                result.Add(v);
                return;
            }

            if (!forced)
            {
                result.AddRange(SoftWrap(v, options.MaxSentenceLength));
                return;
            }

            result.AddRange(HardWrap(v, options.MaxSentenceLength));
        }

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if (c == '\n')
            {
                flush(forced: false);
                continue;
            }

            buf.Append(c);

            if (EndPunct.Contains(c))
            {
                // Special handling for '.' to avoid splitting decimal numbers or abbreviations
                if (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
                {
                    continue;
                }

                if (!options.KeepPunctuation)
                {
                    buf.Length = Math.Max(0, buf.Length - 1);
                }

                flush(forced: false);
            }
        }

        flush(forced: true);
        return result;
    }

    private static IEnumerable<string> SoftWrap(string text, int maxLen)
    {
        int start = 0;
        while (start < text.Length)
        {
            int len = Math.Min(maxLen, text.Length - start);
            int cut = FindCut(text, start, len);
            string part = text.Substring(start, cut).Trim();
            if (part.Length > 0)
            {
                yield return part;
            }
            start += cut;
        }
    }

    private static IEnumerable<string> HardWrap(string text, int maxLen)
    {
        for (int i = 0; i < text.Length; i += maxLen)
        {
            string part = text.Substring(i, Math.Min(maxLen, text.Length - i)).Trim();
            if (part.Length > 0)
            {
                yield return part;
            }
        }
    }

    private static int FindCut(string text, int start, int len)
    {
        int end = start + len;
        int lastSpace = -1;

        for (int i = end - 1; i > start; i--)
        {
            char c = text[i];
            if (c == ' ' || c == '，' || c == '、' || c == ',' || c == ';' || c == '；')
            {
                lastSpace = i;
                break;
            }
        }

        if (lastSpace <= start)
        {
            return len;
        }

        return (lastSpace - start) + 1;
    }
}

