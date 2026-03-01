using SubtitleGuardian.Domain.Contracts;
using SubtitleGuardian.Engines.Asr;
using SubtitleGuardian.Infrastructure.Processes;
using SubtitleGuardian.Infrastructure.Storage;
using System.Text.RegularExpressions;

namespace SubtitleGuardian.Infrastructure.Asr;

public sealed class WhisperCppCliAsrEngine : IAsrEngine
{
    private static readonly Regex OutputJsonRegex = new Regex(@"output_json:\s+saving output to '([^']+)'", RegexOptions.Compiled);
    private static readonly string WhisperTempRoot = Path.Combine(Path.GetTempPath(), "SubtitleGuardian", "whispercpp");
    private static readonly object GpuPolicyLock = new object();
    private static bool? CachedGpuSupport;
    private static string? CachedGpuArg;
    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;
    private readonly WhisperCppLocator _locator;
    private readonly WhisperCppModelFinder _models;
    private readonly WhisperCppJsonParser _parser;

    public WhisperCppCliAsrEngine()
    {
        _paths = new AppPaths("SubtitleGuardian");
        _paths.EnsureCreated();
        _runner = new ProcessRunner();
        _locator = WhisperCppLocator.FromConventionalLocations(_paths.Runtime);
        _models = new WhisperCppModelFinder(_paths);
        _parser = new WhisperCppJsonParser();
    }

    public AsrEngineId Id => AsrEngineId.Whisper;

    public AsrEngineCapabilities GetCapabilities()
    {
        return new AsrEngineCapabilities(
            SupportsLanguageSelection: true,
            SupportsWordTimestamps: true
        );
    }

    public async Task<IReadOnlyList<Segment>> TranscribeAsync(
        string audioPath,
        TranscribeOptions options,
        IProgress<AsrProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("audio file not found", audioPath);
        }

        string modelPath = _models.ResolveModelFile(options);
        string exe = _locator.ResolveExecutable();

        string safeModelPath = EnsureSafePathForWhisper(modelPath, isLargeFile: true);
        bool createdAudioCopy = false;
        string safeAudioPath = EnsureSafePathForWhisper(audioPath, isLargeFile: false, createdCopy: out createdAudioCopy);

        ProcessRunResult r;
        try
        {
            Directory.CreateDirectory(WhisperTempRoot);

            string lang = NormalizeLanguage(options.Language);
            WhisperCppRunPlan plan = await ResolveRunPlanAsync(exe, cancellationToken).ConfigureAwait(false);

            string outputBase = CreateOutputBase();
            string args = BuildArgs(safeModelPath, safeAudioPath, outputBase, lang, plan.ExtraArgs);
            progress?.Report(new AsrProgress(0, plan.Mode == WhisperCppComputeMode.Gpu ? "whisper.cpp starting (GPU)..." : "whisper.cpp starting (CPU)..."));

            r = await _runner.RunAsync(exe, args, cancellationToken).ConfigureAwait(false);

            if (plan.Mode == WhisperCppComputeMode.Gpu && r.ExitCode != 0 && ShouldFallbackToCpu(r))
            {
                progress?.Report(new AsrProgress(1, "GPU 推理不可用，改用 CPU..."));

                outputBase = CreateOutputBase();
                args = BuildArgs(safeModelPath, safeAudioPath, outputBase, lang, extraArgs: null);
                r = await _runner.RunAsync(exe, args, cancellationToken).ConfigureAwait(false);
            }

            if (r.ExitCode != 0)
            {
                throw new InvalidOperationException($"whisper.cpp failed ({r.ExitCode}): {r.StdErr}");
            }

            string jsonPath = ResolveJsonPath(outputBase, r);
            if (!File.Exists(jsonPath))
            {
                throw new InvalidOperationException($"whisper.cpp did not produce json: {jsonPath}\n{r.StdErr}");
            }

            string json = await File.ReadAllTextAsync(jsonPath, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<Segment> segments = _parser.Parse(json);
            SegmentContract.EnsureValid(segments);

            TryDelete(jsonPath);

            progress?.Report(new AsrProgress(100, $"segments={segments.Count}"));
            return segments;
        }
        finally
        {
            if (createdAudioCopy)
            {
                TryDelete(safeAudioPath);
            }
        }
    }

    private string CreateOutputBase()
    {
        string outputBase = Path.Combine(WhisperTempRoot, "out", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.GetDirectoryName(outputBase)!);
        return outputBase;
    }

    private static string BuildArgs(string safeModelPath, string safeAudioPath, string outputBase, string lang, string? extraArgs)
    {
        string args = $"-m \"{safeModelPath}\" -f \"{safeAudioPath}\" -of \"{outputBase}\" -oj -l {lang}";
        if (!string.IsNullOrWhiteSpace(extraArgs))
        {
            args += " " + extraArgs;
        }
        return args;
    }

    private async Task<WhisperCppRunPlan> ResolveRunPlanAsync(string exe, CancellationToken cancellationToken)
    {
        lock (GpuPolicyLock)
        {
            if (CachedGpuSupport is not null)
            {
                return new WhisperCppRunPlan(CachedGpuSupport.Value ? WhisperCppComputeMode.Gpu : WhisperCppComputeMode.Cpu, CachedGpuArg);
            }
        }

        string? gpuArg = await TryDetectGpuArgAsync(exe, cancellationToken).ConfigureAwait(false);
        lock (GpuPolicyLock)
        {
            bool supported = !string.IsNullOrWhiteSpace(gpuArg);
            CachedGpuSupport = supported;
            CachedGpuArg = supported ? gpuArg : null;
        }

        return string.IsNullOrWhiteSpace(gpuArg)
            ? new WhisperCppRunPlan(WhisperCppComputeMode.Cpu, null)
            : new WhisperCppRunPlan(WhisperCppComputeMode.Gpu, gpuArg);
    }

    private async Task<string?> TryDetectGpuArgAsync(string exe, CancellationToken cancellationToken)
    {
        ProcessRunResult rHelp = await _runner.RunAsync(exe, "--help", cancellationToken).ConfigureAwait(false);
        string help = (rHelp.StdOut + "\n" + rHelp.StdErr).ToLowerInvariant();

        if (help.Contains("-ngl"))
        {
            return "-ngl 999";
        }

        if (help.Contains("--gpu-layers"))
        {
            return "--gpu-layers 999";
        }

        if (help.Contains("--gpu_layers"))
        {
            return "--gpu_layers 999";
        }

        return null;
    }

    private static bool ShouldFallbackToCpu(ProcessRunResult r)
    {
        string combined = (r.StdOut + "\n" + r.StdErr).ToLowerInvariant();

        if (combined.Contains("unknown argument") || combined.Contains("unknown option") || combined.Contains("unrecognized option"))
        {
            return true;
        }

        if (combined.Contains("cuda") || combined.Contains("cublas") || combined.Contains("vulkan") || combined.Contains("metal") || combined.Contains("hip"))
        {
            return true;
        }

        if (combined.Contains("no device") || combined.Contains("no gpu") || combined.Contains("device not found") || combined.Contains("failed to initialize"))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "auto";
        }

        string s = language.Trim().Replace('_', '-').ToLowerInvariant();
        if (s == "auto")
        {
            return "auto";
        }

        if (s.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh";
        }

        int dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0)
        {
            s = s.Substring(0, dash);
        }

        return s;
    }

    private string ResolveJsonPath(string outputBase, ProcessRunResult r)
    {
        string combined = $"{r.StdOut}\n{r.StdErr}";
        Match m = OutputJsonRegex.Match(combined);
        if (m.Success)
        {
            string path = m.Groups[1].Value.Trim();
            if (File.Exists(path))
            {
                return path;
            }
        }

        string expected = outputBase + ".json";
        if (File.Exists(expected))
        {
            return expected;
        }

        string prefix = Path.GetFileName(outputBase);
        string dir = Path.GetDirectoryName(outputBase) ?? WhisperTempRoot;
        var candidates = Directory.EnumerateFiles(dir, $"{prefix}*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
            .ToArray();

        if (candidates.Length > 0)
        {
            return candidates[0];
        }

        return expected;
    }

    private static bool IsAsciiPath(string path)
    {
        foreach (char c in path)
        {
            if (c > 127)
            {
                return false;
            }
        }
        return true;
    }

    private static string EnsureSafePathForWhisper(string path, bool isLargeFile)
    {
        if (IsAsciiPath(path))
        {
            return path;
        }

        string dir = Path.Combine(WhisperTempRoot, isLargeFile ? "models" : "audio");
        Directory.CreateDirectory(dir);

        var fi = new FileInfo(path);
        string key = $"{fi.Length:x}-{fi.LastWriteTimeUtc.Ticks:x}";
        string safeName = isLargeFile ? $"{key}-{fi.Name}" : $"{Guid.NewGuid():N}{fi.Extension}";
        string dst = Path.Combine(dir, safeName);

        if (isLargeFile)
        {
            if (File.Exists(dst) && new FileInfo(dst).Length == fi.Length)
            {
                return dst;
            }

            CopyAtomic(path, dst, fi.Length);
            return dst;
        }

        CopyAtomic(path, dst, fi.Length);
        return dst;
    }

    private static string EnsureSafePathForWhisper(string path, bool isLargeFile, out bool createdCopy)
    {
        if (IsAsciiPath(path))
        {
            createdCopy = false;
            return path;
        }

        createdCopy = !isLargeFile;
        return EnsureSafePathForWhisper(path, isLargeFile);
    }

    private static void CopyAtomic(string src, string dst, long expectedLength)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        string tmp = dst + ".tmp";
        TryDelete(tmp);
        File.Copy(src, tmp, overwrite: true);
        long len = new FileInfo(tmp).Length;
        if (len != expectedLength)
        {
            TryDelete(tmp);
            throw new InvalidOperationException("copy incomplete");
        }
        File.Move(tmp, dst, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private enum WhisperCppComputeMode
    {
        Cpu = 0,
        Gpu = 1
    }

    private sealed record WhisperCppRunPlan(WhisperCppComputeMode Mode, string? ExtraArgs);
}
