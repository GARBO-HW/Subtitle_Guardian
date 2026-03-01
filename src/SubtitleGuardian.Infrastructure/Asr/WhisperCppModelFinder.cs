using SubtitleGuardian.Domain.Contracts;
using SubtitleGuardian.Infrastructure.Storage;

namespace SubtitleGuardian.Infrastructure.Asr;

public sealed class WhisperCppModelFinder
{
    private readonly AppPaths _paths;

    public WhisperCppModelFinder(AppPaths paths)
    {
        _paths = paths;
    }

    public string ResolveModelFile(TranscribeOptions options)
    {
        _paths.EnsureCreated();

        string root = Path.Combine(_paths.Models, "whisper");
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"whisper model folder not found: {root}");
        }

        var candidates = Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories)
            .Where(p => p.Contains("ggml-", StringComparison.OrdinalIgnoreCase))
            .Where(IsLikelyCompleteModelFile)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException($"no ggml model found under: {root}");
        }

        string? lang = options.Language;
        bool preferEn = !string.IsNullOrWhiteSpace(lang) && lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        string key = options.Quality switch
        {
            TranscriptionQuality.Tiny => "tiny",
            TranscriptionQuality.Base => "base",
            TranscriptionQuality.Small => "small",
            TranscriptionQuality.Medium => "medium",
            TranscriptionQuality.Large => "large",
            _ => "medium"
        };

        var ordered = candidates
            .OrderByDescending(p => Score(p, key, preferEn))
            .ThenByDescending(p => new FileInfo(p).Length)
            .ToArray();

        return ordered[0];
    }

    private static bool IsLikelyCompleteModelFile(string path)
    {
        long len;
        try
        {
            len = new FileInfo(path).Length;
        }
        catch
        {
            return false;
        }

        if (len < 50_000_000)
        {
            return false;
        }

        string name = Path.GetFileName(path).ToLowerInvariant();

        if (name.Contains("tiny"))
        {
            return len >= 60_000_000;
        }

        if (name.Contains("base"))
        {
            return len >= 120_000_000;
        }

        if (name.Contains("small"))
        {
            return len >= 350_000_000;
        }

        if (name.Contains("medium"))
        {
            return len >= 1_000_000_000;
        }

        if (name.Contains("large"))
        {
            return len >= 1_000_000_000;
        }

        return true;
    }

    private static int Score(string path, string key, bool preferEn)
    {
        string name = Path.GetFileName(path).ToLowerInvariant();
        int s = 0;

        if (name.Contains(key))
        {
            s += 100;
        }

        if (name.Contains("large"))
        {
            s += 70;
        }
        else if (name.Contains("medium"))
        {
            s += 60;
        }
        else if (name.Contains("small"))
        {
            s += 50;
        }
        else if (name.Contains("base"))
        {
            s += 40;
        }
        else if (name.Contains("tiny"))
        {
            s += 30;
        }

        if (preferEn)
        {
            if (name.Contains(".en"))
            {
                s += 15;
            }
        }
        else
        {
            if (name.Contains(".en"))
            {
                s -= 3;
            }
        }

        return s;
    }
}
