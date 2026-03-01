using SubtitleGuardian.Domain.Contracts;
using SubtitleGuardian.Engines.Asr;
using SubtitleGuardian.Infrastructure.Asr;

namespace SubtitleGuardian.Application.Transcription;

public sealed record TranscriptionRequest(
    AsrEngineId EngineId,
    string AudioPath,
    TranscribeOptions Options
);

public sealed class TranscriptionUseCase
{
    public TranscriptionUseCase()
    {
    }

    public async Task<IReadOnlyList<Segment>> ExecuteAsync(
        TranscriptionRequest request,
        IProgress<AsrProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AudioPath))
        {
            throw new ArgumentException("audio path is required", nameof(request));
        }

        IAsrEngine engine = CreateEngine(request.EngineId);
        return await engine.TranscribeAsync(request.AudioPath, request.Options, progress, cancellationToken).ConfigureAwait(false);
    }

    private static IAsrEngine CreateEngine(AsrEngineId id)
    {
        return id switch
        {
            AsrEngineId.Dummy => new DummyAsrEngine(),
            AsrEngineId.Whisper => new WhisperCppCliAsrEngine(),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown engine id")
        };
    }
}
