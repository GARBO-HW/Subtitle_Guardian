namespace SubtitleGuardian.Infrastructure.Asr;

public sealed class WhisperCppLocator
{
    private readonly string? _whisperCliPath;

    public WhisperCppLocator(string? whisperCliPath)
    {
        _whisperCliPath = whisperCliPath;
    }

    public static WhisperCppLocator FromConventionalLocations(string runtimeRoot)
    {
        string a = Path.Combine(runtimeRoot, "whispercpp", "bin", "whisper-cli.exe");
        string b = Path.Combine(runtimeRoot, "whispercpp", "bin", "main.exe");
        string c = Path.Combine(runtimeRoot, "whispercpp", "bin", "whisper-cli");
        string d = Path.Combine(runtimeRoot, "whispercpp", "bin", "main");

        string? found =
            File.Exists(a) ? a :
            File.Exists(b) ? b :
            File.Exists(c) ? c :
            File.Exists(d) ? d :
            null;

        return new WhisperCppLocator(found);
    }

    public string ResolveExecutable()
    {
        return _whisperCliPath ?? "whisper-cli";
    }
}

