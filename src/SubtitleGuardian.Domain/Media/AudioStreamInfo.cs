namespace SubtitleGuardian.Domain.Media;

public sealed record AudioStreamInfo(
    int AudioIndex,
    int StreamIndex,
    string? Codec,
    int? Channels,
    int? SampleRate,
    string? Language,
    string? Title
);
