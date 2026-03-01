namespace SubtitleGuardian.Domain.Text;

public sealed record TextNormalizeOptions(
    bool NormalizeWhitespace = true,
    bool NormalizeLineEndings = true,
    bool NormalizePunctuationSpacing = true
);

