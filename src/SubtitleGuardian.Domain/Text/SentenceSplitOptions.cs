namespace SubtitleGuardian.Domain.Text;

public sealed record SentenceSplitOptions(
    int MinSentenceLength = 1,
    int MaxSentenceLength = 180,
    bool KeepPunctuation = true
);

