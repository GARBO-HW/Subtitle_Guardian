using SubtitleGuardian.Domain.Text;

namespace SubtitleGuardian.Application.Text;

public sealed class TextApplicationService
{
    private readonly TextCleaner _cleaner;
    private readonly SentenceSplitter _splitter;

    public TextApplicationService()
    {
        _cleaner = new TextCleaner();
        _splitter = new SentenceSplitter();
    }

    public string Normalize(string text, TextNormalizeOptions? options = null)
    {
        return _cleaner.Normalize(text, options);
    }

    public IReadOnlyList<string> SplitSentences(string text, SentenceSplitOptions? options = null)
    {
        return _splitter.Split(text, options);
    }

    public IReadOnlyList<string> NormalizeAndSplit(string text, TextNormalizeOptions? normalize = null, SentenceSplitOptions? split = null)
    {
        string cleaned = _cleaner.Normalize(text, normalize);
        return _splitter.Split(cleaned, split);
    }
}

