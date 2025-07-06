using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZeniqaDownloadManager.Models
{
    public class DownloadJob : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        private string _originalUrl = string.Empty;
        private string _outputPath = string.Empty;
        private DownloadType _type;
        private DownloadStatus _status;
        private long _totalSize;
        private long _downloadedBytes;
        private double _progress;
        private TimeSpan _estimatedTimeRemaining;
        private DateTime _startTime;
        private DateTime? _endTime;
        private string _errorMessage = string.Empty;
        private int _retryCount;
        private readonly int _maxRetries = 3;

        public DownloadJob()
        {
            Id = Guid.NewGuid().ToString();
            Status = DownloadStatus.Pending;
            StartTime = DateTime.Now;
            RetryCount = 0;
        }

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string OriginalUrl
        {
            get => _originalUrl;
            set => SetProperty(ref _originalUrl, value);
        }

        public string OutputPath
        {
            get => _outputPath;
            set => SetProperty(ref _outputPath, value);
        }

        public DownloadType Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        public DownloadStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public long TotalSize
        {
            get => _totalSize;
            set => SetProperty(ref _totalSize, value);
        }

        public long DownloadedBytes
        {
            get => _downloadedBytes;
            set
            {
                if (SetProperty(ref _downloadedBytes, value))
                {
                    RunOnUIThread(UpdateProgress);
                }
            }
        }

        public double Progress
        {
            get => _progress;
            set
            {
                RunOnUIThread(() =>
                {
                    SetProperty(ref _progress, value);
                });
            }
        }

        public TimeSpan EstimatedTimeRemaining
        {
            get => _estimatedTimeRemaining;
            set => SetProperty(ref _estimatedTimeRemaining, value);
        }

        public DateTime StartTime
        {
            get => _startTime;
            set => SetProperty(ref _startTime, value);
        }

        public DateTime? EndTime
        {
            get => _endTime;
            set => SetProperty(ref _endTime, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public int RetryCount
        {
            get => _retryCount;
            set => SetProperty(ref _retryCount, value);
        }

        public bool CanRetry => Status == DownloadStatus.Failed && RetryCount < _maxRetries;
        public bool IsCompleted => Status == DownloadStatus.Completed;
        public bool IsFailed => Status == DownloadStatus.Failed;
        public bool IsInProgress => Status == DownloadStatus.Downloading;
        public bool IsPending => Status == DownloadStatus.Pending;
        public bool IsPaused => Status == DownloadStatus.Paused;

        // Additional metadata
        public string? FileExtension { get; set; }
        public TimeSpan? Duration { get; set; }
        public List<string> SegmentUrls { get; set; } = new List<string>();
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        private void UpdateProgress()
        {
            if (TotalSize > 0)
            {
                Progress = (double)DownloadedBytes / TotalSize * 100.0;
            }
        }

        public void MarkAsStarted()
        {
            Status = DownloadStatus.Downloading;
            StartTime = DateTime.Now;
        }

        public void MarkAsCompleted()
        {
            Status = DownloadStatus.Completed;
            EndTime = DateTime.Now;
            Progress = 100.0;
            DownloadedBytes = TotalSize;
        }

        public void MarkAsFailed(string error)
        {
            Status = DownloadStatus.Failed;
            EndTime = DateTime.Now;
            ErrorMessage = error;
        }

        public void MarkAsCancelled()
        {
            Status = DownloadStatus.Cancelled;
            EndTime = DateTime.Now;
        }

        public void IncrementRetry()
        {
            RetryCount++;
        }

        public string GetFormattedSize()
        {
            return FormatFileSize(TotalSize);
        }

        public string GetFormattedDownloadedSize()
        {
            return FormatFileSize(DownloadedBytes);
        }

        public string GetFormattedSpeed(double bytesPerSecond)
        {
            return $"{FormatFileSize((long)bytesPerSecond)}/s";
        }

        public TimeSpan GetElapsedTime()
        {
            var endTime = EndTime ?? DateTime.Now;
            return endTime - StartTime;
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void RunOnUIThread(Action action)
        {
            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }
    }
} 