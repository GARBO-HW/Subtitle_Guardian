using SubtitleGuardian.Infrastructure.Processes;

namespace SubtitleGuardian.Infrastructure.FFmpeg;

public sealed class FfmpegClient
{
    private readonly FfmpegLocator _locator;
    private readonly ProcessRunner _runner;

    public FfmpegClient(FfmpegLocator locator, ProcessRunner runner)
    {
        _locator = locator;
        _runner = runner;
    }

    public async Task TranscodeToStandardWavAsync(
        string sourcePath,
        int? audioStreamIndex,
        string outputWavPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("sourcePath is required", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(outputWavPath))
        {
            throw new ArgumentException("outputWavPath is required", nameof(outputWavPath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);

        string ffmpeg = _locator.ResolveFfmpeg();
        string map = audioStreamIndex is null ? string.Empty : $"-map 0:a:{audioStreamIndex.Value} ";

        string args =
            $"-y -v error -i \"{sourcePath}\" {map}-vn -ac 1 -ar 16000 -c:a pcm_s16le \"{outputWavPath}\"";

        ProcessRunResult r = await _runner.RunAsync(ffmpeg, args, cancellationToken).ConfigureAwait(false);
        if (r.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed ({r.ExitCode}): {r.StdErr}");
        }
    }
}

