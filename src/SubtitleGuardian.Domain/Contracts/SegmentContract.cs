namespace SubtitleGuardian.Domain.Contracts;

public static class SegmentContract
{
    public static void EnsureValid(IReadOnlyList<Segment> segments)
    {
        if (segments.Count == 0)
        {
            return;
        }

        long lastEnd = -1;
        for (int i = 0; i < segments.Count; i++)
        {
            Segment s = segments[i];

            if (s.StartMs < 0 || s.EndMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(segments), "segment time must be non-negative");
            }

            if (s.EndMs < s.StartMs)
            {
                throw new ArgumentException("segment end must be >= start", nameof(segments));
            }

            if (i > 0 && s.StartMs < lastEnd)
            {
                throw new ArgumentException("segments must be non-overlapping and sorted", nameof(segments));
            }

            lastEnd = s.EndMs;
        }
    }
}

