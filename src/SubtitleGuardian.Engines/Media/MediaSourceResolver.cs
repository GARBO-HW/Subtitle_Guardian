using SubtitleGuardian.Domain.Media;

namespace SubtitleGuardian.Engines.Media;

public sealed record ResolvedAudio(
    string SourcePath,
    int? AudioStreamIndex,
    MediaInfo MediaInfo,
    string StandardWavPath
);

public sealed class MediaSourceResolver
{
    private readonly IMediaProbe _probe;
    private readonly IMediaTranscoder _transcoder;
    private readonly ITempFileStore _tempFiles;

    public MediaSourceResolver(IMediaProbe probe, IMediaTranscoder transcoder, ITempFileStore tempFiles)
    {
        _probe = probe;
        _transcoder = transcoder;
        _tempFiles = tempFiles;
    }

    public async Task<ResolvedAudio> ResolveToStandardWavAsync(
        string sourcePath,
        int? audioStreamIndex,
        CancellationToken cancellationToken)
    {
        MediaInfo info = await _probe.ProbeAsync(sourcePath, cancellationToken).ConfigureAwait(false);

        if (info.AudioStreams.Count == 0)
        {
            throw new InvalidOperationException("no audio streams found");
        }

        int? selected = audioStreamIndex;
        if (selected is null)
        {
            selected = 0;
        }

        string wavPath = _tempFiles.CreateTempFilePath(".wav");
        await _transcoder.TranscodeToStandardWavAsync(sourcePath, selected, wavPath, cancellationToken).ConfigureAwait(false);

        return new ResolvedAudio(sourcePath, selected, info, wavPath);
    }
}
