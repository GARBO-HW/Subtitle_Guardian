using SubtitleGuardian.Domain.Media;
using SubtitleGuardian.Engines.Media;
using SubtitleGuardian.Infrastructure.FFmpeg;
using SubtitleGuardian.Infrastructure.Processes;
using SubtitleGuardian.Infrastructure.Storage;

namespace SubtitleGuardian.Application.Media;

public sealed class MediaApplicationService
{
    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;
    private readonly FfmpegLocator _locator;
    private readonly FfprobeClient _ffprobe;
    private readonly FfmpegClient _ffmpeg;
    private readonly TempFileStore _tempFiles;
    private readonly MediaSourceResolver _resolver;

    public MediaApplicationService()
    {
        _paths = new AppPaths("SubtitleGuardian");
        _paths.EnsureCreated();

        _runner = new ProcessRunner();
        _locator = FfmpegLocator.FromConventionalLocations(_paths.Runtime);
        _ffprobe = new FfprobeClient(_locator, _runner);
        _ffmpeg = new FfmpegClient(_locator, _runner);
        _tempFiles = new TempFileStore(_paths);
        _resolver = new MediaSourceResolver(
            new MediaProbeAdapter(_ffprobe),
            new MediaTranscoderAdapter(_ffmpeg),
            new TempFileStoreAdapter(_tempFiles)
        );
    }

    public Task<MediaInfo> ProbeAsync(string sourcePath, CancellationToken cancellationToken)
    {
        return _ffprobe.ProbeAsync(sourcePath, cancellationToken);
    }

    public Task<ResolvedAudio> ResolveToStandardWavAsync(
        string sourcePath,
        int? audioStreamIndex,
        CancellationToken cancellationToken)
    {
        return _resolver.ResolveToStandardWavAsync(sourcePath, audioStreamIndex, cancellationToken);
    }

    private sealed class MediaProbeAdapter : IMediaProbe
    {
        private readonly FfprobeClient _inner;

        public MediaProbeAdapter(FfprobeClient inner)
        {
            _inner = inner;
        }

        public Task<MediaInfo> ProbeAsync(string sourcePath, CancellationToken cancellationToken)
        {
            return _inner.ProbeAsync(sourcePath, cancellationToken);
        }
    }

    private sealed class MediaTranscoderAdapter : IMediaTranscoder
    {
        private readonly FfmpegClient _inner;

        public MediaTranscoderAdapter(FfmpegClient inner)
        {
            _inner = inner;
        }

        public Task TranscodeToStandardWavAsync(
            string sourcePath,
            int? audioStreamIndex,
            string outputWavPath,
            CancellationToken cancellationToken)
        {
            return _inner.TranscodeToStandardWavAsync(sourcePath, audioStreamIndex, outputWavPath, cancellationToken);
        }
    }

    private sealed class TempFileStoreAdapter : ITempFileStore
    {
        private readonly TempFileStore _inner;

        public TempFileStoreAdapter(TempFileStore inner)
        {
            _inner = inner;
        }

        public string CreateTempFilePath(string extensionWithDot)
        {
            return _inner.CreateTempFilePath(extensionWithDot);
        }
    }
}
