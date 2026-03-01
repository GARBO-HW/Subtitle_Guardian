using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SubtitleGuardian.Infrastructure.Processes;

public sealed record ProcessRunResult(int ExitCode, string StdOut, string StdErr);

public sealed class ProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken,
        Action<string>? onStdOut = null,
        Action<string>? onStdErr = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var stdoutTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutTcs.TrySetResult();
                return;
            }
            stdout.AppendLine(e.Data);
            onStdOut?.Invoke(e.Data);
        };

        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrTcs.TrySetResult();
                return;
            }
            stderr.AppendLine(e.Data);
            onStdErr?.Invoke(e.Data);
        };

        try
        {
            if (!p.Start())
            {
                throw new InvalidOperationException("failed to start process");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"executable not found or not runnable: {fileName}", ex);
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        try
        {
            await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(p);
            throw;
        }

        await Task.WhenAll(stdoutTcs.Task, stderrTcs.Task).ConfigureAwait(false);

        return new ProcessRunResult(p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void TryKill(Process p)
    {
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
