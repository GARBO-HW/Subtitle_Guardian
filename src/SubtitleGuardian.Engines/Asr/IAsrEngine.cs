using SubtitleGuardian.Domain.Contracts;

namespace SubtitleGuardian.Engines.Asr;

public interface IAsrEngine
{
    AsrEngineId Id { get; }

    AsrEngineCapabilities GetCapabilities();

    Task<IReadOnlyList<Segment>> TranscribeAsync(
        string audioPath,
        TranscribeOptions options,
        IProgress<AsrProgress>? progress,
        CancellationToken cancellationToken);
}

