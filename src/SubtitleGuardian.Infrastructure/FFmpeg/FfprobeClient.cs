using System.Globalization;
using System.Text.Json;
using SubtitleGuardian.Domain.Media;
using SubtitleGuardian.Infrastructure.Processes;

namespace SubtitleGuardian.Infrastructure.FFmpeg;

public sealed class FfprobeClient
{
    private readonly FfmpegLocator _locator;
    private readonly ProcessRunner _runner;

    public FfprobeClient(FfmpegLocator locator, ProcessRunner runner)
    {
        _locator = locator;
        _runner = runner;
    }

    public async Task<MediaInfo> ProbeAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("sourcePath is required", nameof(sourcePath));
        }

        string ffprobe = _locator.ResolveFfprobe();
        string args =
            $"-v error -print_format json -show_format -show_streams \"{sourcePath}\"";

        ProcessRunResult r = await _runner.RunAsync(ffprobe, args, cancellationToken).ConfigureAwait(false);
        if (r.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe failed ({r.ExitCode}): {r.StdErr}");
        }

        using JsonDocument doc = JsonDocument.Parse(r.StdOut);
        JsonElement root = doc.RootElement;

        long? durationMs = null;
        if (root.TryGetProperty("format", out JsonElement format))
        {
            if (format.TryGetProperty("duration", out JsonElement durationEl))
            {
                string? durationStr = durationEl.GetString();
                if (double.TryParse(durationStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                {
                    durationMs = (long)Math.Round(seconds * 1000.0);
                }
            }
        }

        var streams = new List<AudioStreamInfo>();
        int audioIndex = 0;
        if (root.TryGetProperty("streams", out JsonElement streamsEl) && streamsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement s in streamsEl.EnumerateArray())
            {
                if (!s.TryGetProperty("codec_type", out JsonElement codecTypeEl))
                {
                    continue;
                }

                if (!string.Equals(codecTypeEl.GetString(), "audio", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int index = s.TryGetProperty("index", out JsonElement indexEl) && indexEl.ValueKind == JsonValueKind.Number
                    ? indexEl.GetInt32()
                    : -1;

                string? codec = s.TryGetProperty("codec_name", out JsonElement codecEl) ? codecEl.GetString() : null;

                int? channels = s.TryGetProperty("channels", out JsonElement channelsEl) && channelsEl.ValueKind == JsonValueKind.Number
                    ? channelsEl.GetInt32()
                    : null;

                int? sampleRate = null;
                if (s.TryGetProperty("sample_rate", out JsonElement srEl))
                {
                    string? srStr = srEl.GetString();
                    if (int.TryParse(srStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sr))
                    {
                        sampleRate = sr;
                    }
                }

                string? language = null;
                string? title = null;
                if (s.TryGetProperty("tags", out JsonElement tagsEl) && tagsEl.ValueKind == JsonValueKind.Object)
                {
                    if (tagsEl.TryGetProperty("language", out JsonElement langEl))
                    {
                        language = langEl.GetString();
                    }
                    if (tagsEl.TryGetProperty("title", out JsonElement titleEl))
                    {
                        title = titleEl.GetString();
                    }
                }

                streams.Add(new AudioStreamInfo(audioIndex, index, codec, channels, sampleRate, language, title));
                audioIndex++;
            }
        }

        return new MediaInfo(sourcePath, durationMs, streams);
    }
}
