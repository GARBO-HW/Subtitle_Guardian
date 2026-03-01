namespace SubtitleGuardian.Infrastructure.FFmpeg;

public sealed class FfmpegLocator
{
    private readonly string? _ffmpegPath;
    private readonly string? _ffprobePath;

    public FfmpegLocator(string? ffmpegPath, string? ffprobePath)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
    }

    public static FfmpegLocator FromConventionalLocations(string runtimeRoot)
    {
        string ffmpeg = Path.Combine(runtimeRoot, "ffmpeg", "bin", "ffmpeg.exe");
        string ffprobe = Path.Combine(runtimeRoot, "ffmpeg", "bin", "ffprobe.exe");

        return new FfmpegLocator(
            File.Exists(ffmpeg) ? ffmpeg : null,
            File.Exists(ffprobe) ? ffprobe : null
        );
    }

    public string ResolveFfmpeg()
    {
        return _ffmpegPath ?? "ffmpeg";
    }

    public string ResolveFfprobe()
    {
        return _ffprobePath ?? "ffprobe";
    }
}

