using System.Text.RegularExpressions;
using SubtitleGuardian.Domain.Text;

namespace SubtitleGuardian.Application.Text;

public sealed class TextCleaner
{
    public string Normalize(string input, TextNormalizeOptions? options = null)
    {
        options ??= new TextNormalizeOptions();

        string s = input ?? string.Empty;

        if (options.NormalizeLineEndings)
        {
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        if (options.NormalizeWhitespace)
        {
            s = Regex.Replace(s, @"[ \t]+", " ");
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            s = s.Trim();
        }

        if (options.NormalizePunctuationSpacing)
        {
            s = Regex.Replace(s, @"\s+([,.;:!?])", "$1");
            s = Regex.Replace(s, @"([,.;:!?])([^\s\n])", "$1 $2");

            s = Regex.Replace(s, @"\s+([，。！？；：、])", "$1");
            s = Regex.Replace(s, @"([，。！？；：、])([^\s\n])", "$1$2");
        }

        return s;
    }
}

