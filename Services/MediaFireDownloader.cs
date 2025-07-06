using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    /// <summary>
    /// Specialized downloader for MediaFire URLs that handles their download key generation process
    /// </summary>
    public class MediaFireDownloader : IDownloader
    {
        private readonly NetworkService _networkService;
        private readonly ChunkScheduler _chunkScheduler;
        private readonly IOService _ioService;
        private readonly DownloadSettings _settings;

        public MediaFireDownloader(DownloadSettings settings)
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

                // Extract the actual download URL from MediaFire
                var actualDownloadUrl = await ExtractMediaFireDownloadUrlAsync(job.OriginalUrl, cancellationToken);
                
                if (string.IsNullOrEmpty(actualDownloadUrl))
                {
                    throw new System.IO.InvalidDataException("Failed to extract download URL from MediaFire");
                }

                // Update job with the actual download URL
                job.SegmentUrls.Clear();
                job.SegmentUrls.Add(actualDownloadUrl);

                // Check if file supports range requests
                var supportsRange = await _networkService.SupportsRangeRequestsAsync(actualDownloadUrl);
                
                if (supportsRange && job.TotalSize > 1024 * 1024) // Use chunks for files > 1MB
                {
                    // Use chunked download for better performance
                    await _chunkScheduler.DownloadFileWithChunksAsync(actualDownloadUrl, job.OutputPath, job, cancellationToken);
                }
                else
                {
                    // Fall back to single-threaded download for small files or servers that don't support range requests
                    await DownloadSingleThreadedAsync(actualDownloadUrl, job, cancellationToken);
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
                throw new System.IO.IOException($"Failed to download MediaFire file: {ex.Message}");
            }
        }

        private async Task<string> ExtractMediaFireDownloadUrlAsync(string mediaFireUrl, CancellationToken cancellationToken)
        {
            try
            {
                // First, get the MediaFire page content
                using var request = new HttpRequestMessage(HttpMethod.Get, mediaFireUrl);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                
                using var response = await _networkService._httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var pageContent = await response.Content.ReadAsStringAsync(cancellationToken);
                
                // Look for the download URL pattern in the page
                // MediaFire typically has a pattern like: "href=\"(https://download[^\"]+)\""
                var downloadUrlMatch = Regex.Match(pageContent, @"href=""(https://download[^""]+)""");
                
                if (downloadUrlMatch.Success)
                {
                    return downloadUrlMatch.Groups[1].Value;
                }
                
                // Alternative pattern for newer MediaFire pages
                var alternativeMatch = Regex.Match(pageContent, @"""downloadUrl"":\s*""([^""]+)""");
                if (alternativeMatch.Success)
                {
                    return alternativeMatch.Groups[1].Value.Replace("\\/", "/");
                }
                
                // If no direct download URL found, try to find the download key and construct the URL
                var downloadKeyMatch = Regex.Match(pageContent, @"""downloadKey"":\s*""([^""]+)""");
                if (downloadKeyMatch.Success)
                {
                    var downloadKey = downloadKeyMatch.Groups[1].Value;
                    // Construct the download URL with the key
                    return $"https://download.mediafire.com/file/{downloadKey}";
                }
                
                throw new System.IO.InvalidDataException("Could not extract download URL from MediaFire page");
            }
            catch (Exception ex)
            {
                throw new System.IO.InvalidDataException($"Failed to extract MediaFire download URL: {ex.Message}");
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
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            
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