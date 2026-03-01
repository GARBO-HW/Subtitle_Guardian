using System.Globalization;
using System.Text.Json;
using SubtitleGuardian.Domain.Contracts;

namespace SubtitleGuardian.Infrastructure.Asr;

public sealed class WhisperCppJsonParser
{
    public IReadOnlyList<Segment> Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (TryParseTranscriptionArray(root, out IReadOnlyList<Segment>? segments))
        {
            return segments!;
        }

        if (TryParseSegmentsArray(root, out segments))
        {
            return segments!;
        }

        return Array.Empty<Segment>();
    }

    private static bool TryParseTranscriptionArray(JsonElement root, out IReadOnlyList<Segment>? segments)
    {
        segments = null;

        if (!root.TryGetProperty("transcription", out JsonElement transcription) || transcription.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<Segment>();
        foreach (JsonElement e in transcription.EnumerateArray())
        {
            string text = e.TryGetProperty("text", out JsonElement textEl) ? (textEl.GetString() ?? string.Empty) : string.Empty;

            long? start = null;
            long? end = null;

            if (e.TryGetProperty("offsets", out JsonElement offsets) && offsets.ValueKind == JsonValueKind.Object)
            {
                if (offsets.TryGetProperty("from", out JsonElement fromEl) && fromEl.ValueKind == JsonValueKind.Number)
                {
                    start = fromEl.GetInt64();
                }

                if (offsets.TryGetProperty("to", out JsonElement toEl) && toEl.ValueKind == JsonValueKind.Number)
                {
                    end = toEl.GetInt64();
                }
            }

            if (start is null || end is null)
            {
                continue;
            }

            list.Add(new Segment(start.Value, end.Value, text.Trim()));
        }

        list.Sort(static (a, b) => a.StartMs.CompareTo(b.StartMs));
        segments = list;
        return true;
    }

    private static bool TryParseSegmentsArray(JsonElement root, out IReadOnlyList<Segment>? segments)
    {
        segments = null;

        if (!root.TryGetProperty("segments", out JsonElement segs) || segs.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<Segment>();
        foreach (JsonElement e in segs.EnumerateArray())
        {
            string text = e.TryGetProperty("text", out JsonElement textEl) ? (textEl.GetString() ?? string.Empty) : string.Empty;

            double? startSec = GetDouble(e, "start");
            double? endSec = GetDouble(e, "end");

            if (startSec is null || endSec is null)
            {
                continue;
            }

            long startMs = (long)Math.Round(startSec.Value * 1000.0, MidpointRounding.AwayFromZero);
            long endMs = (long)Math.Round(endSec.Value * 1000.0, MidpointRounding.AwayFromZero);

            list.Add(new Segment(startMs, endMs, text.Trim()));
        }

        list.Sort(static (a, b) => a.StartMs.CompareTo(b.StartMs));
        segments = list;
        return true;
    }

    private static double? GetDouble(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number)
        {
            return el.GetDouble();
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            string? s = el.GetString();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                return v;
            }
        }

        return null;
    }
}
