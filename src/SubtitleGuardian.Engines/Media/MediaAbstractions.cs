using SubtitleGuardian.Domain.Media;

namespace SubtitleGuardian.Engines.Media;

public interface IMediaProbe
{
    Task<MediaInfo> ProbeAsync(string sourcePath, CancellationToken cancellationToken);
}

public interface IMediaTranscoder
{
    Task TranscodeToStandardWavAsync(
        string sourcePath,
        int? audioStreamIndex,
        string outputWavPath,
        CancellationToken cancellationToken);
}

public interface ITempFileStore
{
    string CreateTempFilePath(string extensionWithDot);
}

