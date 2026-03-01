using SubtitleGuardian.Domain.Contracts;

namespace SubtitleGuardian.Engines.Asr;

public sealed class DummyAsrEngine : IAsrEngine
{
    public AsrEngineId Id => AsrEngineId.Dummy;

    public AsrEngineCapabilities GetCapabilities()
    {
        return new AsrEngineCapabilities(
            SupportsLanguageSelection: true,
            SupportsWordTimestamps: false
        );
    }

    public async Task<IReadOnlyList<Segment>> TranscribeAsync(
        string audioPath,
        TranscribeOptions options,
        IProgress<AsrProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new AsrProgress(0, "dummy start"));

        for (int i = 0; i <= 100; i += 10)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new AsrProgress(i, $"dummy {i}%"));
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
        }

        string lang = string.IsNullOrWhiteSpace(options.Language) ? "auto" : options.Language;
        var segments = new[]
        {
            new Segment(0, 1500, $"[Dummy ASR] audio={Path.GetFileName(audioPath)} lang={lang}"),
            new Segment(1600, 4200, "下一步把 Whisper 接到同一份 segments 契約")
        };

        return segments;
    }
}

