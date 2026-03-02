using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SubtitleGuardian.Application.Formatting;
using SubtitleGuardian.Application.Jobs;
using SubtitleGuardian.Application.Media;
using SubtitleGuardian.Application.Transcription;
using SubtitleGuardian.Domain.Contracts;
using SubtitleGuardian.Domain.Media;
using SubtitleGuardian.Engines.Asr;
using SubtitleGuardian.Infrastructure.Storage;

namespace SubtitleGuardian.App.Views;

public partial class AsrPage : UserControl
{
    private readonly JobScheduler _scheduler;
    private readonly MediaApplicationService _media;
    private readonly TranscriptionUseCase _transcription;
    private readonly ObservableCollection<AudioTrackRow> _audioTracks;
    private readonly ObservableCollection<AsrEngineRow> _asrEngines;
    private readonly ObservableCollection<ModelSizeRow> _modelSizes;
    private readonly ObservableCollection<ProcessingDeviceRow> _devices;
    private readonly ObservableCollection<OutputFormatRow> _outputFormats;
    private readonly ObservableCollection<LanguageRow> _languages;
    private readonly string _settingsPath;

    private string? _sourcePath;
    private string? _standardWavPath;
    private string? _exportDir;
    private bool _isTranscribing;
    private bool _isProbing;
    private OutputFormat _outputFormat;
    private JobHandle? _currentTranscribeHandle;
    private string? _lastExportPath;

    public AsrPage(JobScheduler scheduler, MediaApplicationService media, TranscriptionUseCase transcription)
    {
        InitializeComponent();

        _scheduler = scheduler;
        _media = media;
        _transcription = transcription;

        _audioTracks = new ObservableCollection<AudioTrackRow>();
        _asrEngines = new ObservableCollection<AsrEngineRow>
        {
            new AsrEngineRow(AsrEngineId.Whisper, "Whisper")
        };
        _modelSizes = new ObservableCollection<ModelSizeRow>
        {
            new ModelSizeRow(TranscriptionQuality.Tiny, "Tiny"),
            new ModelSizeRow(TranscriptionQuality.Base, "Base"),
            new ModelSizeRow(TranscriptionQuality.Small, "Small"),
            new ModelSizeRow(TranscriptionQuality.Medium, "Medium"),
            new ModelSizeRow(TranscriptionQuality.Large, "Large")
        };
        _devices = new ObservableCollection<ProcessingDeviceRow>
        {
            new ProcessingDeviceRow(ProcessingDevice.GpuWithFallback, "GPU (自動判定)"),
            new ProcessingDeviceRow(ProcessingDevice.CpuOnly, "CPU (強制)")
        };
        _outputFormats = new ObservableCollection<OutputFormatRow>
        {
            new OutputFormatRow(OutputFormat.None, "不匯出"),
            new OutputFormatRow(OutputFormat.Srt, "SRT"),
            new OutputFormatRow(OutputFormat.Txt, "TXT")
        };
        _languages = new ObservableCollection<LanguageRow>
        {
            new LanguageRow("auto", "Auto"),
            new LanguageRow("zh", "中文"),
            new LanguageRow("zh-TW", "中文（繁體）"),
            new LanguageRow("zh-CN", "中文（簡體）"),
            new LanguageRow("en", "English"),
            new LanguageRow("ja", "日本語"),
            new LanguageRow("ko", "한국어")
        };

        AudioTracksCombo.ItemsSource = _audioTracks;
        AsrEngineCombo.ItemsSource = _asrEngines;
        AsrEngineCombo.SelectedValue = AsrEngineId.Whisper;
        ModelSizeCombo.ItemsSource = _modelSizes;
        ModelSizeCombo.SelectedValue = TranscriptionQuality.Medium;
        DeviceCombo.ItemsSource = _devices;
        DeviceCombo.SelectedValue = ProcessingDevice.GpuWithFallback;
        LanguageCombo.ItemsSource = _languages;
        LanguageCombo.SelectedValue = "zh-TW";
        OutputFormatCombo.ItemsSource = _outputFormats;

        _settingsPath = ResolveUiSettingsPath(out string root);
        UiSettings s = LoadSettingsOrDefault(_settingsPath, root);
        _exportDir = s.ExportDirectory;
        ExportDirText.Text = _exportDir ?? string.Empty;
        _outputFormat = ParseOutputFormat(s.OutputFormat);
        OutputFormatCombo.SelectedValue = _outputFormat;

        UpdateUiState();
    }

    private void OnMediaDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                // Accept first file
                string file = files[0];
                // Simple extension check
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".mp3" || ext == ".wav" || ext == ".mp4" || ext == ".mkv" || 
                    ext == ".m4a" || ext == ".aac" || ext == ".flac" || ext == ".webm")
                {
                    LoadMediaFile(file);
                }
            }
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void OnPickMedia(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "選擇音檔或影片",
            Filter = "Media Files|*.mp3;*.wav;*.mp4;*.mkv;*.m4a;*.aac;*.flac;*.webm|All Files|*.*"
        };

        bool? ok = dialog.ShowDialog(Window.GetWindow(this));
        if (ok != true)
        {
            return;
        }

        LoadMediaFile(dialog.FileName);
    }

    private void LoadMediaFile(string path)
    {
        _sourcePath = path;
        SourcePathText.Text = _sourcePath;
        _standardWavPath = null;
        _audioTracks.Clear();
        AudioTracksCombo.SelectedIndex = -1;
        
        // Notify parent if needed, but currently job list handles cancellation visibility based on selection

        _isProbing = true;
        UpdateUiState();

        JobHandle handle = _scheduler.Enqueue("讀取媒體資訊", async ctx =>
        {
            ctx.Progress.Report(new JobProgress(0, "ffprobe..."));
            MediaInfo info = await _media.ProbeAsync(path, ctx.CancellationToken).ConfigureAwait(false);

            Dispatcher.Invoke(() =>
            {
                _audioTracks.Clear();
                foreach (AudioStreamInfo s in info.AudioStreams)
                {
                    _audioTracks.Add(AudioTrackRow.From(s));
                }

                if (_audioTracks.Count > 0)
                {
                    AudioTracksCombo.SelectedIndex = 0;
                }
            });

            ctx.Progress.Report(new JobProgress(100, $"audio={info.AudioStreams.Count}"));
            return info;
        });

        _ = handle.Completion.ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                _isProbing = false;
                UpdateUiState();
            });
        }, TaskScheduler.Default);
    }

    private void OnBrowseExportDir(object sender, RoutedEventArgs e)
    {
        string start = _exportDir ?? string.Empty;
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
        {
            start = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        var dlg = new OpenFileDialog
        {
            Title = "選擇輸出資料夾",
            InitialDirectory = start,
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = "選擇資料夾"
        };

        bool? ok = dlg.ShowDialog(Window.GetWindow(this));
        if (ok != true)
        {
            return;
        }

        string? selectedDir = Path.GetDirectoryName(dlg.FileName);
        if (string.IsNullOrWhiteSpace(selectedDir))
        {
            return;
        }

        _exportDir = selectedDir.Trim();
        ExportDirText.Text = _exportDir;
        SaveSettings();
        UpdateUiState();
    }

    private void OnClearExportDir(object sender, RoutedEventArgs e)
    {
        _exportDir = null;
        ExportDirText.Text = string.Empty;
        SaveSettings();
        UpdateUiState();
    }

    private void OnExportDirLostFocus(object sender, RoutedEventArgs e)
    {
        _exportDir = string.IsNullOrWhiteSpace(ExportDirText.Text) ? null : ExportDirText.Text.Trim();
        SaveSettings();
        UpdateUiState();
    }

    private void OnOutputFormatSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _outputFormat = OutputFormatCombo.SelectedValue is OutputFormat f ? f : OutputFormat.Srt;
        SaveSettings();
        UpdateUiState();
    }

    private void OnTranscribeAsr(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_sourcePath))
        {
            return;
        }

        if (_isProbing)
        {
            return;
        }

        if (_isTranscribing)
        {
            _currentTranscribeHandle?.Cancel();
            return;
        }

        AsrEngineId engineId = AsrEngineCombo.SelectedValue is AsrEngineId id ? id : AsrEngineId.Whisper;
        TranscriptionQuality quality = ModelSizeCombo.SelectedValue is TranscriptionQuality q ? q : TranscriptionQuality.Medium;
        string? lang = LanguageCombo.SelectedValue as string;
        if (string.Equals(lang, "auto", StringComparison.OrdinalIgnoreCase))
        {
            lang = "auto";
        }
        int? audioIndex = AudioTracksCombo.SelectedValue is int i ? i : null;
        string sourcePath = _sourcePath;
        string? exportDir = _exportDir;
        OutputFormat outputFormat = _outputFormat;
        ProcessingDevice device = DeviceCombo.SelectedValue is ProcessingDevice d ? d : ProcessingDevice.GpuWithFallback;

        _isTranscribing = true;
        UpdateUiState();

        JobHandle handle = _scheduler.Enqueue($"ASR 轉錄 ({engineId})", async ctx =>
        {
            var asrProgress = new Progress<AsrProgress>(p =>
            {
                ctx.Progress.Report(new JobProgress(p.Percent, p.Message));
            });

            string wav = _standardWavPath is not null && File.Exists(_standardWavPath)
                ? _standardWavPath
                : string.Empty;

            if (string.IsNullOrWhiteSpace(wav))
            {
                ctx.Progress.Report(new JobProgress(0, "Probe + 抽取標準音訊..."));
                var resolved = await _media.ResolveToStandardWavAsync(sourcePath, audioIndex, ctx.CancellationToken).ConfigureAwait(false);

                Dispatcher.Invoke(() =>
                {
                    _audioTracks.Clear();
                    foreach (AudioStreamInfo s in resolved.MediaInfo.AudioStreams)
                    {
                        _audioTracks.Add(AudioTrackRow.From(s));
                    }

                    if (_audioTracks.Count > 0)
                    {
                        AudioTracksCombo.SelectedValue = resolved.AudioStreamIndex ?? 0;
                    }

                    _standardWavPath = resolved.StandardWavPath;
                });

                wav = resolved.StandardWavPath;
            }

            var req = new TranscriptionRequest(
                engineId,
                wav,
                new TranscribeOptions(Language: lang, Quality: quality, EnableWordTimestamps: true, Device: device)
            );

            IReadOnlyList<Segment> segments = await _transcription.ExecuteAsync(req, asrProgress, ctx.CancellationToken).ConfigureAwait(false);

            if (outputFormat == OutputFormat.None)
            {
                ctx.Progress.Report(new JobProgress(98, "輸出格式設為不匯出，略過輸出"));
            }
            else if (!string.IsNullOrWhiteSpace(exportDir))
            {
                try
                {
                    Directory.CreateDirectory(exportDir);
                    string ext = outputFormat == OutputFormat.Txt ? ".txt" : ".srt";
                    string outFile = ResolveAutoOutputPath(exportDir, sourcePath, ext);
                    string label = outputFormat == OutputFormat.Txt ? "TXT" : "SRT";
                    ctx.Progress.Report(new JobProgress(95, $"匯出 {label}..."));

                    if (outputFormat == OutputFormat.Txt)
                    {
                        new TxtFormatter().WriteToFile(outFile, segments);
                    }
                    else
                    {
                        new SrtFormatter().WriteToFile(outFile, segments);
                    }

                    Dispatcher.Invoke(() =>
                    {
                        _lastExportPath = outFile;
                        UpdateUiState();
                    });

                    ctx.Progress.Report(new JobProgress(98, $"已匯出：{outFile}"));
                }
                catch (Exception ex)
                {
                    ctx.Progress.Report(new JobProgress(98, $"匯出失敗：{ex.Message}"));
                }
            }
            else
            {
                ctx.Progress.Report(new JobProgress(98, "未設定輸出目錄，略過匯出"));
            }

            ctx.Progress.Report(new JobProgress(100, $"segments={segments.Count}"));
            return segments;
        });

        _currentTranscribeHandle = handle;
        _ = handle.Completion.ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                _isTranscribing = false;
                _currentTranscribeHandle = null;
                UpdateUiState();
            });
        }, TaskScheduler.Default);
    }

    private void OnOpenExportDir(object sender, RoutedEventArgs e)
    {
        try
        {
            string? file = _lastExportPath;
            if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{file}\"",
                    UseShellExecute = true
                });
                return;
            }

            string? dir = _exportDir;
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
        }
    }

    private void OnAudioTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _standardWavPath = null;
    }

    private void UpdateUiState()
    {
        bool canTranscribe = !_isTranscribing && !_isProbing && !string.IsNullOrWhiteSpace(_sourcePath);
        TranscribeButton.IsEnabled = _isTranscribing || canTranscribe;
        TranscribeButton.Content = _isTranscribing ? "取消轉錄" : "轉錄（ASR）";
        PickMediaButton.IsEnabled = !_isTranscribing && !_isProbing;
        ExportDirText.IsEnabled = !_isTranscribing;
        OutputFormatCombo.IsEnabled = !_isTranscribing;
        BrowseExportDirButton.IsEnabled = !_isTranscribing;
        ClearExportDirButton.IsEnabled = !_isTranscribing && !string.IsNullOrWhiteSpace(_exportDir);
        OpenExportDirButton.IsEnabled = !_isTranscribing && !string.IsNullOrWhiteSpace(_exportDir) && Directory.Exists(_exportDir);
    }

    private sealed record AudioTrackRow(int AudioIndex, string Label)
    {
        public static AudioTrackRow From(AudioStreamInfo s)
        {
            string codec = s.Codec ?? "unknown";
            string ch = s.Channels is null ? "?" : s.Channels.Value.ToString();
            string sr = s.SampleRate is null ? "?" : s.SampleRate.Value.ToString();
            string lang = string.IsNullOrWhiteSpace(s.Language) ? string.Empty : $" {s.Language}";
            string title = string.IsNullOrWhiteSpace(s.Title) ? string.Empty : $" {s.Title}";

            return new AudioTrackRow(s.AudioIndex, $"#{s.AudioIndex} ({codec} {ch}ch {sr}Hz){lang}{title}");
        }
    }

    private sealed record AsrEngineRow(AsrEngineId Id, string Label);
    private sealed record ModelSizeRow(TranscriptionQuality Value, string Label);
    private sealed record ProcessingDeviceRow(ProcessingDevice Value, string Label);
    private enum OutputFormat
    {
        None = 0,
        Srt = 1,
        Txt = 2
    }

    private sealed record OutputFormatRow(OutputFormat Id, string Label);
    private sealed record LanguageRow(string Code, string Label);

    private sealed record UiSettings(string? ExportDirectory, string? OutputFormat, string? ExportSrtDirectory);

    private static UiSettings LoadSettingsOrDefault(string settingsPath, string root)
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                UiSettings? s = JsonSerializer.Deserialize<UiSettings>(json);
                if (s is not null)
                {
                    string? exportDir = string.IsNullOrWhiteSpace(s.ExportDirectory) ? null : s.ExportDirectory.Trim();
                    if (string.IsNullOrWhiteSpace(exportDir) && !string.IsNullOrWhiteSpace(s.ExportSrtDirectory))
                    {
                        exportDir = s.ExportSrtDirectory.Trim();
                    }

                    string? fmt = string.IsNullOrWhiteSpace(s.OutputFormat) ? null : s.OutputFormat.Trim().ToLowerInvariant();
                    return new UiSettings(exportDir, fmt, s.ExportSrtDirectory);
                }
            }
        }
        catch
        {
        }

        return new UiSettings(null, null, null);
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            string fmt = _outputFormat switch
            {
                OutputFormat.Txt => "txt",
                OutputFormat.None => "none",
                _ => "srt"
            };
            var s = new UiSettings(_exportDir, fmt, null);
            string json = JsonSerializer.Serialize(s);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
        }
    }

    private static OutputFormat ParseOutputFormat(string? value)
    {
        if (value is null)
        {
            return OutputFormat.Srt;
        }

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return OutputFormat.None;
        }

        if (value.Equals("txt", StringComparison.OrdinalIgnoreCase))
        {
            return OutputFormat.Txt;
        }

        return OutputFormat.Srt;
    }

    private static string ResolveUiSettingsPath(out string root)
    {
        var (installRoot, userRoot) = AppPaths.ResolveRoots("SubtitleGuardian");
        root = userRoot;
        return Path.Combine(root, "ui-settings.json");
    }

    private static string ResolveAutoOutputPath(string exportDir, string sourcePath, string extensionWithDot)
    {
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "output";
        }

        string candidate = Path.Combine(exportDir, baseName + extensionWithDot);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (int i = 1; i <= 9999; i++)
        {
            string c = Path.Combine(exportDir, $"{baseName} ({i}){extensionWithDot}");
            if (!File.Exists(c))
            {
                return c;
            }
        }

        return Path.Combine(exportDir, $"{baseName} ({Guid.NewGuid():N}){extensionWithDot}");
    }
}
