using SubtitleGuardian.Domain.Contracts;

namespace SubtitleGuardian.Engines.Asr;

public sealed class WhisperAsrEngine : IAsrEngine
{
    public AsrEngineId Id => AsrEngineId.Whisper;

    public AsrEngineCapabilities GetCapabilities()
    {
        return new AsrEngineCapabilities(
            SupportsLanguageSelection: true,
            SupportsWordTimestamps: true
        );
    }

    public Task<IReadOnlyList<Segment>> TranscribeAsync(
        string audioPath,
        TranscribeOptions options,
        IProgress<AsrProgress>? progress,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Whisper engine is not implemented yet.");
    }
}

