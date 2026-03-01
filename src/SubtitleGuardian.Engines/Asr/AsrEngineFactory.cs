namespace SubtitleGuardian.Engines.Asr;

public sealed class AsrEngineFactory
{
    public IAsrEngine Create(AsrEngineId id)
    {
        return id switch
        {
            AsrEngineId.Dummy => new DummyAsrEngine(),
            AsrEngineId.Whisper => new WhisperAsrEngine(),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unknown engine id")
        };
    }
}

