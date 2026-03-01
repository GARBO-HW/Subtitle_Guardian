using System.Text;
using SubtitleGuardian.Application.Text;
using SubtitleGuardian.Domain.Contracts;
using SubtitleGuardian.Domain.Text;

namespace SubtitleGuardian.Application.Alignment;

public sealed class TextAligner
{
    private readonly SentenceSplitter _sentenceSplitter;

    public TextAligner()
    {
        _sentenceSplitter = new SentenceSplitter();
    }

    public IReadOnlyList<Segment> Align(
        IReadOnlyList<Segment> asrSegments,
        string userText,
        SentenceSplitOptions? splitOptions = null)
    {
        if (asrSegments == null || asrSegments.Count == 0)
        {
            return Array.Empty<Segment>();
        }

        if (string.IsNullOrWhiteSpace(userText))
        {
            return Array.Empty<Segment>();
        }

        // 1. Prepare ASR tokens with timestamps
        var asrTokens = FlattenAsrSegments(asrSegments);

        // 2. Prepare User tokens (grouped by sentence)
        var sentences = _sentenceSplitter.Split(userText, splitOptions);
        var userTokens = FlattenUserSentences(sentences, out var sentenceTokenRanges);

        // 3. Align tokens
        var alignedIndices = PerformAlignment(asrTokens, userTokens);

        // 4. Project timestamps to sentences
        return ProjectTimestampsToSentences(sentences, sentenceTokenRanges, userTokens, asrTokens, alignedIndices);
    }

    private List<TokenInfo> FlattenAsrSegments(IReadOnlyList<Segment> segments)
    {
        var result = new List<TokenInfo>();

        foreach (var seg in segments)
        {
            if (seg.Words != null && seg.Words.Count > 0)
            {
                foreach (var word in seg.Words)
                {
                    // Use the same tokenizer for ASR words to ensure consistency with User text
                    // This handles CJK splitting (e.g. "你好" -> "你", "好") and punctuation removal
                    var tokens = Tokenize(word.Text).Select(Normalize).Where(t => !string.IsNullOrEmpty(t)).ToList();

                    if (tokens.Count == 0) continue;

                    if (tokens.Count == 1)
                    {
                        result.Add(new TokenInfo(tokens[0], word.StartMs, word.EndMs));
                    }
                    else
                    {
                        // Interpolate within the word (e.g. for CJK phrases)
                        double duration = word.EndMs - word.StartMs;
                        double step = duration / tokens.Count;

                        for (int i = 0; i < tokens.Count; i++)
                        {
                            long start = word.StartMs + (long)(i * step);
                            long end = word.StartMs + (long)((i + 1) * step);
                            result.Add(new TokenInfo(tokens[i], start, end));
                        }
                    }
                }
            }
            else
            {
                // Fallback: tokenize text and interpolate time
                var tokens = Tokenize(seg.Text).Select(Normalize).Where(t => !string.IsNullOrEmpty(t)).ToList();
                if (tokens.Count == 0) continue;

                double duration = seg.EndMs - seg.StartMs;
                double step = duration / tokens.Count;

                for (int i = 0; i < tokens.Count; i++)
                {
                    long start = seg.StartMs + (long)(i * step);
                    long end = seg.StartMs + (long)((i + 1) * step);
                    result.Add(new TokenInfo(tokens[i], start, end));
                }
            }
        }

        return result;
    }

    private List<UserTokenInfo> FlattenUserSentences(
        IReadOnlyList<string> sentences, 
        out List<(int Start, int Count)> ranges)
    {
        var result = new List<UserTokenInfo>();
        ranges = new List<(int Start, int Count)>();

        for (int i = 0; i < sentences.Count; i++)
        {
            int startIdx = result.Count;
            var tokens = Tokenize(sentences[i]).Select(Normalize).Where(t => !string.IsNullOrEmpty(t));
            foreach (var token in tokens)
            {
                result.Add(new UserTokenInfo(token, i));
            }
            ranges.Add((startIdx, result.Count - startIdx));
        }

        return result;
    }

    private int[] PerformAlignment(List<TokenInfo> asr, List<UserTokenInfo> user)
    {
        int n = asr.Count;
        int m = user.Count;
        
        // Window size for Sakoe-Chiba band to prevent alignment drift
        // 500 tokens is usually enough to cover large gaps while keeping alignment local.
        int window = 500; 

        // Use ushort to save memory, assuming cost < 65535.
        // If texts are very different, cost can be high. Cap at ushort.MaxValue.
        var costs = new ushort[n + 1, m + 1];

        // Initialize with infinity (using ushort.MaxValue)
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= m; j++)
                costs[i, j] = ushort.MaxValue;

        costs[0, 0] = 0;

        for (int i = 0; i <= n; i++)
        {
            int start = Math.Max(1, i - window);
            int end = Math.Min(m, i + window);
            
            // Base cases for first row/col within window
            if (i == 0)
            {
                for (int j = 1; j <= end; j++) costs[0, j] = (ushort)Math.Min(j, ushort.MaxValue);
                continue;
            }
            if (start == 1) costs[i, 0] = (ushort)Math.Min(i, ushort.MaxValue);

            for (int j = start; j <= end; j++)
            {
                int cost = (asr[i - 1].Text == user[j - 1].Text) ? 0 : 1;
                
                int del = (costs[i - 1, j] == ushort.MaxValue) ? ushort.MaxValue : costs[i - 1, j] + 1;
                int ins = (costs[i, j - 1] == ushort.MaxValue) ? ushort.MaxValue : costs[i, j - 1] + 1;
                int sub = (costs[i - 1, j - 1] == ushort.MaxValue) ? ushort.MaxValue : costs[i - 1, j - 1] + cost;
                
                costs[i, j] = (ushort)Math.Min(del, Math.Min(ins, sub));
            }
        }

        // Backtrack
        var alignment = new int[m];
        for (int k = 0; k < m; k++) alignment[k] = -1;

        int r = n;
        int c = m;

        while (r > 0 && c > 0)
        {
            int cost = (asr[r - 1].Text == user[c - 1].Text) ? 0 : 1;
            int current = costs[r, c];
            
            // Check neighbors. Prefer Insert/Delete over Match if costs are equal
            // to favor alignment to earlier tokens (since we backtrack from end).
            
            int delCost = (r > 0) ? costs[r - 1, c] : ushort.MaxValue;
            int insCost = (c > 0) ? costs[r, c - 1] : ushort.MaxValue;
            int subCost = (r > 0 && c > 0) ? costs[r - 1, c - 1] : ushort.MaxValue;

            if (current == insCost + 1) // Insertion (skip User word)
            {
                c--;
            }
            else if (current == delCost + 1) // Deletion (skip ASR word)
            {
                r--;
            }
            else // Match/Sub
            {
                if (cost == 0) // Match
                {
                    alignment[c - 1] = r - 1;
                }
                r--;
                c--;
            }
        }

        return alignment;
    }

    private IReadOnlyList<Segment> ProjectTimestampsToSentences(
        IReadOnlyList<string> sentences,
        List<(int Start, int Count)> sentenceTokenRanges,
        List<UserTokenInfo> userTokens,
        List<TokenInfo> asrTokens,
        int[] alignedIndices)
    {
        var result = new Segment[sentences.Count];
        
        // Pass 1: Assign aligned timestamps
        for (int i = 0; i < sentences.Count; i++)
        {
            var range = sentenceTokenRanges[i];
            if (range.Count == 0) continue; // Should not happen with splitter

            long start = -1;
            long end = -1;

            // Find first aligned token
            for (int k = 0; k < range.Count; k++)
            {
                int userIdx = range.Start + k;
                int asrIdx = alignedIndices[userIdx];
                // Console.WriteLine($"UserToken {userIdx} ({userTokens[userIdx].Text}) -> AsrToken {asrIdx}");
                if (asrIdx != -1)
                {
                    start = asrTokens[asrIdx].Start;
                    break;
                }
            }

            // Find last aligned token
            for (int k = range.Count - 1; k >= 0; k--)
            {
                int userIdx = range.Start + k;
                int asrIdx = alignedIndices[userIdx];
                if (asrIdx != -1)
                {
                    end = asrTokens[asrIdx].End;
                    break;
                }
            }
            
            if (start != -1 && end != -1)
            {
                // Ensure end >= start
                if (end < start) end = start;
                result[i] = new Segment(start, end, sentences[i]);
            }
            else if (start != -1)
            {
                result[i] = new Segment(start, start + 1000, sentences[i]);
            }
            else if (end != -1)
            {
                result[i] = new Segment(Math.Max(0, end - 1000), end, sentences[i]);
            }
            // Else: Leave null for interpolation
        }

        // Pass 2: Interpolate missing timestamps
        long lastEnd = 0;
        for (int i = 0; i < sentences.Count; i++)
        {
            if (result[i] != null)
            {
                var seg = result[i]!;
                // Ensure monotonicity with previous
                if (seg.StartMs < lastEnd)
                {
                    // Adjust start, maybe compress previous?
                    // For now, just clamp start
                    long newStart = lastEnd;
                    long newEnd = Math.Max(newStart + 100, seg.EndMs);
                    result[i] = new Segment(newStart, newEnd, seg.Text);
                }
                lastEnd = result[i]!.EndMs;
            }
            else
            {
                // Missing. Find next aligned segment.
                int nextAligned = -1;
                for (int j = i + 1; j < sentences.Count; j++)
                {
                    if (result[j] != null)
                    {
                        nextAligned = j;
                        break;
                    }
                }

                long nextStart = (nextAligned != -1) ? result[nextAligned]!.StartMs : lastEnd + 1000 * (sentences.Count - i);
                
                // Distribute time between lastEnd and nextStart among missing sentences
                int missingCount = (nextAligned != -1) ? (nextAligned - i) : (sentences.Count - i);
                double duration = nextStart - lastEnd;
                double step = duration / missingCount;
                
                // Ensure step is positive
                if (step < 100) step = 100;

                long start = lastEnd;
                long end = start + (long)step;
                
                result[i] = new Segment(start, end, sentences[i]);
                lastEnd = end;
            }
        }

        return result.Where(s => s != null).Select(s => s!).ToList();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (IsCjk(c)) 
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                yield return c.ToString();
            }
            else if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private static bool IsCjk(char c)
    {
        return (c >= 0x4E00 && c <= 0x9FFF) || 
               (c >= 0x3400 && c <= 0x4DBF) || 
               (c >= 0x3000 && c <= 0x303F) || 
               (c >= 0xFF00 && c <= 0xFFEF);
    }

    private static string Normalize(string s)
    {
        // Remove punctuation and symbols for better matching
        // e.g. "Hello," -> "hello"
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (!char.IsPunctuation(c) && !char.IsSymbol(c) && !char.IsSeparator(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString().ToLowerInvariant().Trim();
    }

    private record struct TokenInfo(string Text, long Start, long End);
    private record struct UserTokenInfo(string Text, int SentenceIndex);
}
