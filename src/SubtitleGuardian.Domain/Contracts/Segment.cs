namespace SubtitleGuardian.Domain.Contracts;

public sealed record Segment(
    long StartMs,
    long EndMs,
    string Text,
    IReadOnlyList<Word>? Words = null
);

