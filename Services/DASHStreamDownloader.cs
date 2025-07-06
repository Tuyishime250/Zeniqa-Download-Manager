using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    public class DASHStreamDownloader : IDownloader
    {
        private readonly HttpClient _httpClient;

        public DASHStreamDownloader()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
        }

        public async Task DownloadAsync(DownloadJob job, CancellationToken cancellationToken)
        {
            try
            {
                if (job.SegmentUrls.Count == 0)
                {
                    throw new System.IO.InvalidDataException("No segment URLs available for DASH download");
                }

                // Create output directory if it doesn't exist
                var outputDir = Path.GetDirectoryName(job.OutputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                using var outputStream = new FileStream(job.OutputPath, FileMode.Create, FileAccess.Write);
                var totalBytesDownloaded = 0L;
                var lastProgressUpdate = DateTime.Now;

                // Download each segment
                for (int i = 0; i < job.SegmentUrls.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var segmentUrl = job.SegmentUrls[i];
                    var segmentBytes = await DownloadSegmentAsync(segmentUrl, cancellationToken);
                    
                    await outputStream.WriteAsync(segmentBytes, 0, segmentBytes.Length, cancellationToken);
                    totalBytesDownloaded += segmentBytes.Length;
                    job.DownloadedBytes = totalBytesDownloaded;

                    // Update total size if not set
                    if (job.TotalSize == 0)
                    {
                        job.TotalSize = job.SegmentUrls.Count * segmentBytes.Length; // Rough estimate
                    }

                    // Update progress every 100ms
                    if (DateTime.Now - lastProgressUpdate > TimeSpan.FromMilliseconds(100))
                    {
                        UpdateEstimatedTimeRemaining(job, totalBytesDownloaded, i + 1, job.SegmentUrls.Count);
                        lastProgressUpdate = DateTime.Now;
                    }
                }

                await outputStream.FlushAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                throw new System.IO.IOException($"Failed to download DASH stream: {ex.Message}");
            }
        }

        private async Task<byte[]> DownloadSegmentAsync(string segmentUrl, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.GetAsync(segmentUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                throw new System.IO.IOException($"Failed to download segment {segmentUrl}: {ex.Message}");
            }
        }

        private void UpdateEstimatedTimeRemaining(DownloadJob job, long totalBytesDownloaded, int segmentsCompleted, int totalSegments)
        {
            if (segmentsCompleted <= 0) return;

            var elapsed = DateTime.Now - job.StartTime;
            if (elapsed.TotalSeconds <= 0) return;

            var segmentsPerSecond = segmentsCompleted / elapsed.TotalSeconds;
            var remainingSegments = totalSegments - segmentsCompleted;
            var estimatedSeconds = remainingSegments / segmentsPerSecond;

            job.EstimatedTimeRemaining = TimeSpan.FromSeconds(Math.Max(0, estimatedSeconds));
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
} 