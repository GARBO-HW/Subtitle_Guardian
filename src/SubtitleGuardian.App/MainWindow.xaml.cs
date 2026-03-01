﻿﻿﻿﻿using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using SubtitleGuardian.App.Views;
using SubtitleGuardian.Application.Jobs;
using SubtitleGuardian.Application.Media;
using SubtitleGuardian.Application.Transcription;

namespace SubtitleGuardian.App;

public partial class MainWindow : Window
{
    private readonly JobScheduler _scheduler;
    private readonly MediaApplicationService _media;
    private readonly TranscriptionUseCase _transcription;
    private readonly ObservableCollection<JobRow> _jobs;
    private readonly Dictionary<Guid, JobRow> _jobIndex;

    public MainWindow()
    {
        InitializeComponent();

        _scheduler = new JobScheduler();
        _media = new MediaApplicationService();
        _transcription = new TranscriptionUseCase();
        _jobs = new ObservableCollection<JobRow>();
        _jobIndex = new Dictionary<Guid, JobRow>();

        JobsList.ItemsSource = _jobs;
        _scheduler.JobUpdated += OnJobUpdated;

        // Initialize Tabs
        var asrPage = new AsrPage(_scheduler, _media, _transcription);
        var alignmentPage = new AlignmentPage(_scheduler, _media, _transcription);

        MainTabs.Items.Add(new TabItem { Header = "語音轉錄 (ASR)", Content = asrPage });
        MainTabs.Items.Add(new TabItem { Header = "文稿匹配 (Alignment)", Content = alignmentPage });
    }

    protected override void OnClosed(EventArgs e)
    {
        _scheduler.JobUpdated -= OnJobUpdated;
        _scheduler.Dispose();
        base.OnClosed(e);
    }

    private void OnCancelSelectedTask(object sender, RoutedEventArgs e)
    {
        if (JobsList.SelectedItem is not JobRow row)
        {
            return;
        }

        _scheduler.TryCancel(row.JobId);
    }

    private void OnJobSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JobsList.SelectedItem is JobRow row)
        {
            SelectedJobProgress.Value = row.Percent;
            CancelSelectedTaskButton.Visibility = Visibility.Visible;
        }
        else
        {
            SelectedJobProgress.Value = 0;
            CancelSelectedTaskButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnJobUpdated(JobSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_jobIndex.TryGetValue(snapshot.JobId, out JobRow? row))
            {
                row = new JobRow(snapshot.JobId, snapshot.Title);
                _jobIndex.Add(snapshot.JobId, row);
                _jobs.Insert(0, row);
            }

            row.Status = FormatJobStatus(snapshot.Status);
            row.Percent = snapshot.Percent;
            row.Message = snapshot.Message ?? string.Empty;
            row.ErrorMessage = snapshot.ErrorMessage ?? string.Empty;
            row.UpdateTimestamps(snapshot.Status);

            if (ReferenceEquals(JobsList.SelectedItem, row))
            {
                SelectedJobProgress.Value = row.Percent;
            }
        });
    }

    private static string FormatJobStatus(JobStatus status)
    {
        return status switch
        {
            JobStatus.Pending => "等待",
            JobStatus.Running => "執行中",
            JobStatus.Completed => "完成",
            JobStatus.Failed => "失敗",
            JobStatus.Canceled => "已取消",
            _ => status.ToString()
        };
    }

    private sealed class JobRow : INotifyPropertyChanged
    {
        private string _status;
        private int _percent;
        private string _message;
        private string _errorMessage;
        private string _startTime;
        private string _endTime;
        private string _duration;

        private DateTime? _startDt;
        private DateTime? _endDt;

        public JobRow(Guid jobId, string title)
        {
            JobId = jobId;
            Title = title;
            _status = FormatJobStatus(JobStatus.Pending);
            _percent = 0;
            _message = string.Empty;
            _errorMessage = string.Empty;
            _startTime = "-";
            _endTime = "-";
            _duration = "-";
        }

        public Guid JobId { get; }
        public string Title { get; }

        public string Status
        {
            get => _status;
            set => Set(ref _status, value);
        }

        public int Percent
        {
            get => _percent;
            set => Set(ref _percent, value);
        }

        public string Message
        {
            get => _message;
            set => Set(ref _message, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => Set(ref _errorMessage, value);
        }

        public string StartTime
        {
            get => _startTime;
            set => Set(ref _startTime, value);
        }

        public string EndTime
        {
            get => _endTime;
            set => Set(ref _endTime, value);
        }

        public string Duration
        {
            get => _duration;
            set => Set(ref _duration, value);
        }

        public void UpdateTimestamps(JobStatus status)
        {
            if (status == JobStatus.Running && _startDt == null)
            {
                _startDt = DateTime.Now;
                StartTime = _startDt.Value.ToString("HH:mm:ss");
            }

            if ((status == JobStatus.Completed || status == JobStatus.Failed || status == JobStatus.Canceled) && _endDt == null)
            {
                _endDt = DateTime.Now;
                EndTime = _endDt.Value.ToString("HH:mm:ss");
            }

            if (_startDt != null)
            {
                var end = _endDt ?? DateTime.Now;
                var span = end - _startDt.Value;
                Duration = span.ToString(@"hh\:mm\:ss");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}