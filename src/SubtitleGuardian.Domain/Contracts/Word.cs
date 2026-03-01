namespace SubtitleGuardian.Domain.Contracts;

public sealed record Word(
    long StartMs,
    long EndMs,
    string Text
);

