 using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZeniqaDownloadManager.Models
{
    public class DownloadChunk : INotifyPropertyChanged
    {
        private long _startByte;
        private long _endByte;
        private long _downloadedBytes;
        private DownloadChunkStatus _status;
        private string _errorMessage = string.Empty;
        private DateTime _startTime;
        private DateTime? _endTime;

        public DownloadChunk(long startByte, long endByte)
        {
            StartByte = startByte;
            EndByte = endByte;
            Status = DownloadChunkStatus.Pending;
            StartTime = DateTime.Now;
        }

        public long StartByte
        {
            get => _startByte;
            set => SetProperty(ref _startByte, value);
        }

        public long EndByte
        {
            get => _endByte;
            set => SetProperty(ref _endByte, value);
        }

        public long ChunkSize => EndByte - StartByte + 1;

        public long DownloadedBytes
        {
            get => _downloadedBytes;
            set => SetProperty(ref _downloadedBytes, value);
        }

        public double Progress
        {
            get
            {
                if (ChunkSize <= 0) return 0;
                return (double)DownloadedBytes / ChunkSize * 100.0;
            }
        }

        public DownloadChunkStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
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

        public bool IsCompleted => Status == DownloadChunkStatus.Completed;
        public bool IsFailed => Status == DownloadChunkStatus.Failed;
        public bool IsInProgress => Status == DownloadChunkStatus.Downloading;

        public void MarkAsStarted()
        {
            Status = DownloadChunkStatus.Downloading;
            StartTime = DateTime.Now;
        }

        public void MarkAsCompleted()
        {
            Status = DownloadChunkStatus.Completed;
            EndTime = DateTime.Now;
            DownloadedBytes = ChunkSize;
        }

        public void MarkAsFailed(string error)
        {
            Status = DownloadChunkStatus.Failed;
            EndTime = DateTime.Now;
            ErrorMessage = error;
        }

        public TimeSpan GetElapsedTime()
        {
            var endTime = EndTime ?? DateTime.Now;
            return endTime - StartTime;
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
    }

    public enum DownloadChunkStatus
    {
        Pending,
        Downloading,
        Completed,
        Failed
    }
}