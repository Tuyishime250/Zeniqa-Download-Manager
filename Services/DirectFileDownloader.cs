using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    public class DirectFileDownloader : IDownloader
    {
        private readonly NetworkService _networkService;
        private readonly ChunkScheduler _chunkScheduler;
        private readonly IOService _ioService;
        private readonly DownloadSettings _settings;

        public DirectFileDownloader(DownloadSettings settings)
        {
            _settings = settings;
            _networkService = new NetworkService(settings);
            _chunkScheduler = new ChunkScheduler(settings);
            _ioService = new IOService(settings);
        }

        public async Task DownloadAsync(DownloadJob job, CancellationToken cancellationToken)
        {
            try
            {
                // Check if output file is locked before starting download
                if (File.Exists(job.OutputPath) && _ioService.IsFileLocked(job.OutputPath))
                {
                    var usageInfo = _ioService.GetFileUsageInfo(job.OutputPath);
                    throw new IOException($"Cannot access output file: {job.OutputPath}\n\n{usageInfo}");
                }

                // Use the first URL from segment URLs (for direct files, this is the main URL)
                var downloadUrl = job.SegmentUrls.Count > 0 ? job.SegmentUrls[0] : job.OriginalUrl;

                // Check if file supports range requests
                var supportsRange = await _networkService.SupportsRangeRequestsAsync(downloadUrl);
                
                if (supportsRange && job.TotalSize > 1024 * 1024) // Use chunks for files > 1MB
                {
                    try
                    {
                        // Try chunked download
                        await _chunkScheduler.DownloadFileWithChunksAsync(downloadUrl, job.OutputPath, job, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // Fallback to single-threaded download if chunked download fails
                        job.Metadata["ChunkedDownloadError"] = ex.Message;
                        await DownloadSingleThreadedAsync(downloadUrl, job, cancellationToken);
                    }
                }
                else
                {
                    // Fall back to single-threaded download for small files or servers that don't support range requests
                    await DownloadSingleThreadedAsync(downloadUrl, job, cancellationToken);
                }
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process") || 
                                        ex.Message.Contains("The process cannot access the file") ||
                                        ex.Message.Contains("being used by another system"))
            {
                var usageInfo = _ioService.GetFileUsageInfo(job.OutputPath);
                throw new IOException($"File access error: {ex.Message}\n\n{usageInfo}", ex);
            }
            catch (Exception ex)
            {
                throw new System.IO.IOException($"Failed to download file: {ex.Message}");
            }
        }

        private async Task DownloadSingleThreadedAsync(string downloadUrl, DownloadJob job, CancellationToken cancellationToken)
        {
            // Check if file supports resume
            var supportsResume = await _networkService.SupportsRangeRequestsAsync(downloadUrl);
            var existingSize = 0L;

            if (supportsResume && File.Exists(job.OutputPath))
            {
                var fileInfo = new System.IO.FileInfo(job.OutputPath);
                existingSize = fileInfo.Length;
                job.DownloadedBytes = existingSize;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            
            if (supportsResume && existingSize > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingSize, null);
            }

            using var response = await _networkService._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Update total size if not already set
            if (job.TotalSize == 0 && response.Content.Headers.ContentLength.HasValue)
            {
                job.TotalSize = response.Content.Headers.ContentLength.Value;
            }

            // If resuming, add the existing size to total
            if (existingSize > 0 && response.Content.Headers.ContentLength.HasValue)
            {
                job.TotalSize = existingSize + response.Content.Headers.ContentLength.Value;
            }

            using var fileStream = _ioService.CreateDownloadStreamWithRetry(job.OutputPath, FileMode.OpenOrCreate);

            if (existingSize > 0)
            {
                fileStream.Seek(existingSize, SeekOrigin.Begin);
            }

            // Copy content with progress tracking
            var buffer = new byte[_settings.BufferSize];
            var totalBytesRead = existingSize;
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0) break;

                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalBytesRead += bytesRead;
                
                // Update progress in real-time
                job.DownloadedBytes = totalBytesRead;
                
                // Update estimated time remaining
                UpdateEstimatedTimeRemaining(job, totalBytesRead);
            }

            await fileStream.FlushAsync(cancellationToken);
        }

        private void UpdateEstimatedTimeRemaining(DownloadJob job, long totalBytesRead)
        {
            if (job.TotalSize <= 0 || totalBytesRead <= 0) return;

            var elapsed = DateTime.Now - job.StartTime;
            if (elapsed.TotalSeconds <= 0) return;

            var bytesPerSecond = totalBytesRead / elapsed.TotalSeconds;
            var remainingBytes = job.TotalSize - totalBytesRead;
            var estimatedSeconds = remainingBytes / bytesPerSecond;

            job.EstimatedTimeRemaining = TimeSpan.FromSeconds(Math.Max(0, estimatedSeconds));
        }

        public void Dispose()
        {
            _networkService?.Dispose();
            _chunkScheduler?.Dispose();
        }
    }
} 