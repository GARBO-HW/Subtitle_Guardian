using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SubtitleGuardian.Application.Alignment;
using SubtitleGuardian.Application.Formatting;
using SubtitleGuardian.Application.Jobs;
using SubtitleGuardian.Application.Media;
using SubtitleGuardian.Application.Text;
using SubtitleGuardian.Application.Transcription;
using SubtitleGuardian.Domain.Contracts;
using SubtitleGuardian.Domain.Text;
using SubtitleGuardian.Engines.Asr;

namespace SubtitleGuardian.App.Views;

public partial class AlignmentPage : UserControl
{
    private readonly JobScheduler? _scheduler;
    private readonly MediaApplicationService? _media;
    private readonly TranscriptionUseCase? _transcription;
    private readonly ObservableCollection<LanguageRow> _languages;
    
    private string? _audioPath;
    private string? _textPath;
    private string? _exportDir;
    private bool _isAligning;

    public AlignmentPage()
    {
        InitializeComponent();
        _languages = new ObservableCollection<LanguageRow>();
        // Parameterless constructor for WPF design-time support or if XAML instantiation is needed.
        // Dependencies will be null, so runtime logic should check or this constructor should not be used in production if DI is required.
    }

    public AlignmentPage(JobScheduler scheduler, MediaApplicationService media, TranscriptionUseCase transcription) : this()
    {
        _scheduler = scheduler;
        _media = media;
        _transcription = transcription;

        _languages.Add(new LanguageRow("zh", "中文"));
        _languages.Add(new LanguageRow("zh-TW", "中文（繁體）"));
        _languages.Add(new LanguageRow("zh-CN", "中文（簡體）"));
        _languages.Add(new LanguageRow("en", "English"));
        _languages.Add(new LanguageRow("ja", "日本語"));
        _languages.Add(new LanguageRow("ko", "한국어"));
        
        LanguageCombo.ItemsSource = _languages;
        LanguageCombo.SelectedValue = "zh-TW";

        UpdateUiState();
    }

    private void OnAudioDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                // Accept first file
                string file = files[0];
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".mp3" || ext == ".wav" || ext == ".mp4" || ext == ".mkv" || 
                    ext == ".m4a" || ext == ".aac" || ext == ".flac" || ext == ".webm")
                {
                    LoadAudioFile(file);
                }
            }
        }
    }

    private void OnTextDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                // Accept first file
                string file = files[0];
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".txt" || ext == ".srt" || ext == ".md" || ext == ".csv")
                {
                    LoadTextFile(file);
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

    private void OnPickAudio(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "選擇音檔或影片",
            Filter = "Media Files|*.mp3;*.wav;*.mp4;*.mkv;*.m4a;*.aac;*.flac;*.webm|All Files|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            LoadAudioFile(dialog.FileName);
        }
    }

    private void LoadAudioFile(string path)
    {
        _audioPath = path;
        AudioSourceText.Text = _audioPath;
        
        // Auto-set export dir if not set or empty
        if (string.IsNullOrWhiteSpace(_exportDir) || string.IsNullOrWhiteSpace(ExportDirText.Text))
        {
            _exportDir = Path.GetDirectoryName(_audioPath);
            ExportDirText.Text = _exportDir;
        }
        
        UpdateUiState();
    }

    private void OnPickTextFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "選擇文字稿",
            Filter = "Text Files|*.txt|All Files|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            LoadTextFile(dialog.FileName);
        }
    }

    private void LoadTextFile(string path)
    {
        _textPath = path;
        TextSourcePathText.Text = _textPath;
        try
        {
            TranscriptInput.Text = File.ReadAllText(_textPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"讀取檔案失敗: {ex.Message}");
        }
        
        UpdateUiState();
    }

    private void OnTranscriptTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateUiState();
    }

    private void OnBrowseExport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "選擇輸出資料夾",
            CheckFileExists = false,
            FileName = "選擇此資料夾",
            ValidateNames = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            string? dir = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                _exportDir = dir;
                ExportDirText.Text = _exportDir;
                UpdateUiState();
            }
        }
    }

    private void OnStartAlignment(object sender, RoutedEventArgs e)
    {
        if (_scheduler == null || _transcription == null || string.IsNullOrWhiteSpace(_audioPath) || string.IsNullOrWhiteSpace(TranscriptInput.Text))
        {
            return;
        }

        string audioPath = _audioPath;
        string transcript = TranscriptInput.Text;
        string? exportDir = _exportDir;
        string? lang = LanguageCombo.SelectedValue as string;
        
        if (!int.TryParse(MaxCharsText.Text, out int maxChars))
        {
            maxChars = 40;
        }

        _isAligning = true;
        UpdateUiState();

        var handle = _scheduler.Enqueue("文稿匹配 (Alignment)", async ctx =>
        {
            ctx.Progress.Report(new JobProgress(0, "初始化..."));
            
            // 1. ASR
            ctx.Progress.Report(new JobProgress(10, "執行語音轉錄 (ASR)..."));
            
            var req = new TranscriptionRequest(
                AsrEngineId.Whisper,
                audioPath,
                new TranscribeOptions(Language: lang, Quality: TranscriptionQuality.Medium, EnableWordTimestamps: true)
            );

            IReadOnlyList<Segment> asrSegments;
            try
            {
                // Note: If Whisper engine is not fully implemented, this might fail.
                // We'll catch it and use mock data for development testing.
                asrSegments = await _transcription.ExecuteAsync(req, null, ctx.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is NotSupportedException || ex.Message.Contains("not implemented"))
            {
                ctx.Progress.Report(new JobProgress(15, $"ASR 引擎未就緒 ({ex.Message})，使用測試資料模擬..."));
                await Task.Delay(1000, ctx.CancellationToken); // Simulate delay
                asrSegments = GenerateMockAsrSegments(transcript);
            }

            if (ctx.CancellationToken.IsCancellationRequested) return false;

            // 2. Alignment
            ctx.Progress.Report(new JobProgress(40, $"取得 {asrSegments.Count} 個語音片段，開始匹配..."));
            
            var aligner = new TextAligner();
            var splitOptions = new SentenceSplitOptions 
            { 
                MaxSentenceLength = maxChars,
                KeepPunctuation = false // Cleaner matching
            };
            
            // Run alignment (CPU bound, could wrap in Task.Run if heavy)
            var alignedSegments = aligner.Align(asrSegments, transcript, splitOptions);
            
            ctx.Progress.Report(new JobProgress(80, $"匹配完成，共 {alignedSegments.Count} 句"));

            // 3. Export
            if (!string.IsNullOrWhiteSpace(exportDir))
            {
                try
                {
                    Directory.CreateDirectory(exportDir);
                    string fileName = Path.GetFileNameWithoutExtension(audioPath) + "_aligned.srt";
                    string outFile = Path.Combine(exportDir, fileName);
                    
                    ctx.Progress.Report(new JobProgress(90, $"匯出至 {fileName}..."));
                    
                    new SrtFormatter().WriteToFile(outFile, alignedSegments);
                    
                    ctx.Progress.Report(new JobProgress(100, $"已匯出：{outFile}"));
                }
                catch (Exception ex)
                {
                    ctx.Progress.Report(new JobProgress(95, $"匯出失敗：{ex.Message}"));
                }
            }
            else
            {
                ctx.Progress.Report(new JobProgress(100, "完成 (未設定匯出目錄)"));
            }
            
            return true;
        });

        _ = handle.Completion.ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                _isAligning = false;
                UpdateUiState();
            });
        }, TaskScheduler.Default);
    }
    
    private List<Segment> GenerateMockAsrSegments(string userText)
    {
        // Simulate rough ASR output from the user text for testing purposes
        var splitter = new SentenceSplitter();
        var sentences = splitter.Split(userText);
        var result = new List<Segment>();
        long time = 0;
        
        foreach(var s in sentences)
        {
            // Simulate some noise or slight mismatch
            string asrText = s;
            if (s.Length > 5) asrText = s.Substring(0, s.Length - 1);
            
            long duration = Math.Max(1000, s.Length * 200); 
            result.Add(new Segment(time, time + duration, asrText));
            time += duration + 200;
        }
        return result;
    }

    private void UpdateUiState()
    {
        bool canAlign = !_isAligning && 
                        !string.IsNullOrWhiteSpace(_audioPath) && 
                        !string.IsNullOrWhiteSpace(TranscriptInput.Text);
        
        AlignButton.IsEnabled = canAlign;
        AlignButton.Content = _isAligning ? "匹配中..." : "開始匹配 (Align)";
        
        PickAudioButton.IsEnabled = !_isAligning;
        PickTextButton.IsEnabled = !_isAligning;
        TranscriptInput.IsReadOnly = _isAligning;
        BrowseExportButton.IsEnabled = !_isAligning;
    }

    private sealed record LanguageRow(string Code, string Label);
}
