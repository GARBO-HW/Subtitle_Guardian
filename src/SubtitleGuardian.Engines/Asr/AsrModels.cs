namespace SubtitleGuardian.Engines.Asr;

public enum AsrEngineId
{
    Dummy = 0,
    Whisper = 1
}

public sealed record AsrEngineCapabilities(
    bool SupportsLanguageSelection,
    bool SupportsWordTimestamps
);

public sealed record AsrProgress(int Percent, string? Message = null);

