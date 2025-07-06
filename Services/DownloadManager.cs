using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    public class DownloadManager : INotifyPropertyChanged
    {
        private readonly ConcurrentQueue<DownloadJob> _pendingQueue;
        private readonly ConcurrentDictionary<string, DownloadJob> _activeDownloads;
        private readonly ObservableCollection<DownloadJob> _allJobs;
        private readonly SemaphoreSlim _concurrencySemaphore;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly string _downloadDirectory;
        private readonly int _maxConcurrentDownloads;
        private readonly DownloadSettings _settings;
        private bool _isRunning;

        public DownloadManager(DownloadSettings settings, string? downloadDirectory = null)
        {
            _settings = settings;
            _maxConcurrentDownloads = settings.MaxConcurrentChunks;
            _pendingQueue = new ConcurrentQueue<DownloadJob>();
            _activeDownloads = new ConcurrentDictionary<string, DownloadJob>();
            _allJobs = new ObservableCollection<DownloadJob>();
            _concurrencySemaphore = new SemaphoreSlim(_maxConcurrentDownloads, _maxConcurrentDownloads);
            _cancellationTokenSource = new CancellationTokenSource();
            
            _downloadDirectory = downloadDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                "Downloads", 
                "ZeniqaDownloadManager");
            
            // Ensure download directory exists
            Directory.CreateDirectory(_downloadDirectory);
        }

        public ObservableCollection<DownloadJob> AllJobs => _allJobs;
        public int PendingCount => _pendingQueue.Count;
        public int ActiveCount => _activeDownloads.Count;
        public int TotalCount => _allJobs.Count;
        public bool IsRunning => _isRunning;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<DownloadJob>? JobAdded;
        public event EventHandler<DownloadJob>? JobStarted;
        public event EventHandler<DownloadJob>? JobCompleted;
        public event EventHandler<DownloadJob>? JobFailed;
        public event EventHandler<DownloadJob>? JobCancelled;

        public async Task<DownloadJob> AddJobAsync(DownloadItem item, string? customOutputPath = null)
        {
            var job = new DownloadJob
            {
                Title = item.Title,
                OriginalUrl = item.OriginalUrl,
                Type = item.Type,
                TotalSize = item.FileSize ?? 0,
                FileExtension = item.FileExtension,
                Duration = item.Duration,
                SegmentUrls = new List<string>(item.SegmentUrls),
                Metadata = new Dictionary<string, string>(item.Metadata)
            };

            // Generate output path
            job.OutputPath = GenerateOutputPath(job, customOutputPath);

            // Add to collections
            _allJobs.Add(job);
            _pendingQueue.Enqueue(job);

            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(TotalCount));
            JobAdded?.Invoke(this, job);

            // Start processing if not already running
            if (!_isRunning)
            {
                _ = Task.Run(ProcessQueueAsync);
            }

            return job;
        }

        public async Task StartAsync()
        {
            if (_isRunning) return;

            _isRunning = true;
            OnPropertyChanged(nameof(IsRunning));

            await ProcessQueueAsync();
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            _cancellationTokenSource.Cancel();
            OnPropertyChanged(nameof(IsRunning));

            // Wait for active downloads to complete
            while (_activeDownloads.Count > 0)
            {
                await Task.Delay(100);
            }
        }

        public Task StartJobAsync(DownloadJob job)
        {
            if (job.Status == DownloadStatus.Pending)
            {
                // Move job to front of queue and start immediately
                _pendingQueue.Enqueue(job);
                
                // Ensure processing is running
                if (!_isRunning)
                {
                    _isRunning = true;
                    OnPropertyChanged(nameof(IsRunning));
                    _ = Task.Run(ProcessQueueAsync);
                }
            }
            return Task.CompletedTask;
        }

        public void PauseJob(string jobId)
        {
            if (_activeDownloads.TryGetValue(jobId, out var job))
            {
                job.Status = DownloadStatus.Paused;
                _activeDownloads.TryRemove(jobId, out _);
                _concurrencySemaphore.Release();
                OnPropertyChanged(nameof(ActiveCount));
                OnPropertyChanged(nameof(PendingCount));
            }
        }

        public void ResumeJob(string jobId)
        {
            var job = _allJobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null && job.Status == DownloadStatus.Paused)
            {
                job.Status = DownloadStatus.Pending;
                _pendingQueue.Enqueue(job);
                OnPropertyChanged(nameof(PendingCount));
                
                // Ensure processing is running
                if (!_isRunning)
                {
                    _isRunning = true;
                    OnPropertyChanged(nameof(IsRunning));
                    _ = Task.Run(ProcessQueueAsync);
                }
            }
        }

        public void CancelJob(string jobId)
        {
            if (_activeDownloads.TryGetValue(jobId, out var job))
            {
                job.MarkAsCancelled();
                _activeDownloads.TryRemove(jobId, out _);
                _concurrencySemaphore.Release();
                OnPropertyChanged(nameof(ActiveCount));
                JobCancelled?.Invoke(this, job);
            }
        }

        public void RetryJob(string jobId)
        {
            var job = _allJobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null && job.CanRetry)
            {
                job.IncrementRetry();
                job.Status = DownloadStatus.Pending;
                job.ErrorMessage = string.Empty;
                _pendingQueue.Enqueue(job);
                OnPropertyChanged(nameof(PendingCount));
            }
        }

        public void ClearCompletedJobs()
        {
            var completedJobs = _allJobs.Where(j => j.IsCompleted || j.IsFailed).ToList();
            foreach (var job in completedJobs)
            {
                _allJobs.Remove(job);
            }
            OnPropertyChanged(nameof(TotalCount));
        }

        public void ClearAllJobs()
        {
            _allJobs.Clear();
            _pendingQueue.Clear();
            _activeDownloads.Clear();
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(ActiveCount));
        }

        private async Task ProcessQueueAsync()
        {
            while (_isRunning && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                if (_pendingQueue.TryDequeue(out var job))
                {
                    await _concurrencySemaphore.WaitAsync(_cancellationTokenSource.Token);
                    
                    _activeDownloads.TryAdd(job.Id, job);
                    OnPropertyChanged(nameof(ActiveCount));
                    OnPropertyChanged(nameof(PendingCount));

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessJobAsync(job);
                        }
                        finally
                        {
                            _activeDownloads.TryRemove(job.Id, out _);
                            _concurrencySemaphore.Release();
                            OnPropertyChanged(nameof(ActiveCount));
                        }
                    });
                }
                else
                {
                    await Task.Delay(100, _cancellationTokenSource.Token);
                }
            }
        }

        private async Task ProcessJobAsync(DownloadJob job)
        {
            try
            {
                job.MarkAsStarted();
                JobStarted?.Invoke(this, job);

                var downloader = CreateDownloader(job.Type, job.OriginalUrl);
                await downloader.DownloadAsync(job, _cancellationTokenSource.Token);

                job.MarkAsCompleted();
                JobCompleted?.Invoke(this, job);
            }
            catch (OperationCanceledException)
            {
                job.MarkAsCancelled();
                JobCancelled?.Invoke(this, job);
            }
            catch (Exception ex)
            {
                job.MarkAsFailed(ex.Message);
                JobFailed?.Invoke(this, job);
            }
        }

        private IDownloader CreateDownloader(DownloadType type, string url = "")
        {
            // Check if this is a MediaFire URL
            if (IsMediaFireUrl(url))
            {
                return new MediaFireDownloader(_settings);
            }

            return type switch
            {
                DownloadType.DirectFile => new DirectFileDownloader(_settings),
                DownloadType.HLSStream => new HLSStreamDownloader(),
                DownloadType.DASHStream => new DASHStreamDownloader(),
                DownloadType.YouTube => new YouTubeDownloader(),
                _ => throw new NotSupportedException($"Download type {type} is not supported")
            };
        }

        private bool IsMediaFireUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            
            try
            {
                var uri = new Uri(url);
                return uri.Host.Contains("mediafire.com", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private string GenerateOutputPath(DownloadJob job, string? customOutputPath)
        {
            if (!string.IsNullOrEmpty(customOutputPath))
            {
                return customOutputPath;
            }

            var fileName = string.IsNullOrEmpty(job.Title) 
                ? $"download_{DateTime.Now:yyyyMMdd_HHmmss}" 
                : SanitizeFileName(job.Title);

            if (!string.IsNullOrEmpty(job.FileExtension))
            {
                fileName += job.FileExtension;
            }

            return Path.Combine(_downloadDirectory, fileName);
        }

        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries))
                .Replace(" ", "_");
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _concurrencySemaphore?.Dispose();
        }
    }
} 