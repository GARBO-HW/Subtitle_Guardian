using System;
using System.IO;

namespace SubtitleGuardian.Infrastructure.Storage;

public sealed class AppPaths
{
    public AppPaths(string appName, string? overrideRoot = null)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            throw new ArgumentException("appName is required", nameof(appName));
        }

        var (installRoot, userRoot) = ResolveRoots(appName, overrideRoot);

        InstallRoot = installRoot;
        UserRoot = userRoot;
        Root = InstallRoot;

        // Models and Runtime are in InstallRoot (read-only in Program Files)
        Models = Path.Combine(InstallRoot, "models");
        Runtime = Path.Combine(InstallRoot, "runtime");

        // Cache and Temp MUST be in UserRoot (writable)
        Cache = Path.Combine(UserRoot, "cache");
        Temp = Path.Combine(Cache, "temp");
    }

    public string Root { get; }
    public string InstallRoot { get; }
    public string UserRoot { get; }
    
    public string Models { get; }
    public string Runtime { get; }
    public string Cache { get; }
    public string Temp { get; }

    public void EnsureCreated()
    {
        // Try to create InstallRoot directories if possible (might fail in Program Files)
        if (InstallRoot == UserRoot)
        {
            try { Directory.CreateDirectory(Root); } catch {}
            try { Directory.CreateDirectory(Models); } catch {}
            try { Directory.CreateDirectory(Runtime); } catch {}
        }
        
        // Ensure UserRoot directories exist (must succeed)
        Directory.CreateDirectory(UserRoot);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Temp);
    }

    public static (string InstallRoot, string UserRoot) ResolveRoots(string appName, string? overrideRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return (overrideRoot, overrideRoot);
        }

        string? env = Environment.GetEnvironmentVariable("SUBTITLE_GUARDIAN_HOME");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return (env, env);
        }

        // Check for portable data folder next to executable
        string portable = Path.Combine(AppContext.BaseDirectory, ".subtitleguardian");
        if (Directory.Exists(portable))
        {
            if (IsDirectoryWritable(portable))
            {
                // True Portable Mode (USB)
                return (portable, portable);
            }
            else
            {
                // Read-only Portable (Program Files)
                string local = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    appName
                );
                return (portable, local);
            }
        }

        string? workspace = FindWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            string wsRoot = Path.Combine(workspace, ".subtitleguardian");
            return (wsRoot, wsRoot);
        }

        string defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appName
        );
        return (defaultPath, defaultPath);
    }

    private static bool IsDirectoryWritable(string dirPath)
    {
        try
        {
            string testFile = Path.Combine(dirPath, $"test_write_{Guid.NewGuid()}.tmp");
            using (File.Create(testFile, 1, FileOptions.DeleteOnClose))
            { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindWorkspaceRoot()
    {
        string baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);

        for (int i = 0; i < 12 && dir is not null; i++)
        {
            string marker = Path.Combine(dir.FullName, "SubtitleGuardian.slnx");
            if (File.Exists(marker))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return null;
    }
}
