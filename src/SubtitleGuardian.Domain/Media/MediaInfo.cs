namespace SubtitleGuardian.Domain.Media;

public sealed record MediaInfo(
    string SourcePath,
    long? DurationMs,
    IReadOnlyList<AudioStreamInfo> AudioStreams
);

