using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;
using System.Security.Cryptography;
using System.Net;

namespace ZeniqaDownloadManager.Services
{
    public class ChunkScheduler
    {
        private readonly NetworkService _networkService;
        private readonly IOService _ioService;
        private readonly int _maxConcurrentChunks;
        private readonly SemaphoreSlim _chunkSemaphore;
        private readonly DownloadSettings _settings;

        public ChunkScheduler(DownloadSettings settings)
        {
            _settings = settings;
            _maxConcurrentChunks = settings.MaxConcurrentChunks;
            _chunkSemaphore = new SemaphoreSlim(_maxConcurrentChunks, _maxConcurrentChunks);
            _networkService = new NetworkService(settings);
            _ioService = new IOService(settings);
        }

        public async Task<long> GetContentLengthAsync(string url)
        {
            return await _networkService.GetContentLengthAsync(url);
        }

        public async Task DownloadFileWithChunksAsync(
            string url, 
            string outputPath, 
            DownloadJob job, 
            CancellationToken cancellationToken,
            int? customChunkCount = null)
        {
            try
            {
                // Get file size
                var contentLength = await GetContentLengthAsync(url);
                if (contentLength <= 0)
                {
                    throw new System.IO.IOException("Could not determine file size");
                }

                job.TotalSize = contentLength;

                // Determine chunk count
                var chunkCount = customChunkCount ?? DetermineOptimalChunkCount(contentLength);
                var chunks = CreateChunks(contentLength, chunkCount);

                // Create output directory if needed
                _ioService.EnsureDirectoryExists(Path.GetDirectoryName(outputPath));

                // Download chunks in parallel
                var downloadTasks = new List<Task>();
                var chunkFiles = new List<string>();

                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    var chunkFilePath = _ioService.CreateTempFilePath(outputPath, i);
                    chunkFiles.Add(chunkFilePath);

                    var downloadTask = DownloadChunkAsync(url, chunk, chunkFilePath, job, chunks, cancellationToken);
                    downloadTasks.Add(downloadTask);
                }

                // Wait for all chunks to complete
                await Task.WhenAll(downloadTasks);

                // Check if any chunks failed
                var failedChunks = chunks.FindAll(c => c.IsFailed);
                if (failedChunks.Count > 0)
                {
                    throw new System.IO.IOException($"Failed to download {failedChunks.Count} chunks");
                }

                // Merge chunks into final file
                var tempMergedPath = outputPath + ".merged";
                await _ioService.MergeChunkFilesAsync(chunkFiles.ToArray(), tempMergedPath, cancellationToken);

                // Optional: Verify checksum if provided
                if (job.Metadata.TryGetValue("Checksum", out var expectedChecksum) && !string.IsNullOrWhiteSpace(expectedChecksum))
                {
                    var algo = DetectChecksumAlgorithm(expectedChecksum);
                    var actualChecksum = await ComputeChecksumAsync(tempMergedPath, algo);
                    if (!string.Equals(expectedChecksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new System.IO.InvalidDataException($"Checksum mismatch! Expected: {expectedChecksum}, Actual: {actualChecksum}");
                    }
                }

                // Move/rename merged file to final output
                _ioService.SafeMoveFile(tempMergedPath, outputPath);

                // Clean up chunk files
                _ioService.CleanupTempFiles(chunkFiles.ToArray());
            }
            catch (Exception ex)
            {
                throw new System.IO.IOException($"Chunked download failed: {ex.Message}");
            }
        }

        private int DetermineOptimalChunkCount(long fileSize)
        {
            // Base chunk size of 1MB
            const long baseChunkSize = 1024 * 1024;
            var optimalChunks = (int)Math.Ceiling((double)fileSize / baseChunkSize);

            // Clamp between 4 and 16 chunks
            return Math.Max(4, Math.Min(16, optimalChunks));
        }

        private List<DownloadChunk> CreateChunks(long fileSize, int chunkCount)
        {
            var chunks = new List<DownloadChunk>();
            var chunkSize = fileSize / chunkCount;
            var remainder = fileSize % chunkCount;

            long startByte = 0;
            for (int i = 0; i < chunkCount; i++)
            {
                var currentChunkSize = chunkSize + (i < remainder ? 1 : 0);
                var endByte = startByte + currentChunkSize - 1;

                chunks.Add(new DownloadChunk(startByte, endByte));
                startByte = endByte + 1;
            }

            return chunks;
        }

        private async Task DownloadChunkAsync(
            string url, 
            DownloadChunk chunk, 
            string chunkFilePath, 
            DownloadJob job, 
            List<DownloadChunk> allChunks,
            CancellationToken cancellationToken)
        {
            await _chunkSemaphore.WaitAsync(cancellationToken);

            try
            {
                var result = await _networkService.DownloadChunkAsync(url, chunk, chunkFilePath, cancellationToken);
                
                if (result.Success)
                {
                    UpdateJobProgressFromChunks(job, allChunks);
                }
                else
                {
                    throw new System.IO.IOException(result.ErrorMessage ?? "Download failed");
                }
            }
            finally
            {
                _chunkSemaphore.Release();
            }
        }

        private void UpdateJobProgressFromChunks(DownloadJob job, List<DownloadChunk> chunks)
        {
            var totalDownloaded = chunks.Sum(c => c.DownloadedBytes);
            job.DownloadedBytes = totalDownloaded;
        }

        private void UpdateEstimatedTimeRemaining(DownloadJob job)
        {
            if (job.TotalSize <= 0 || job.DownloadedBytes <= 0) return;

            var elapsed = DateTime.Now - job.StartTime;
            if (elapsed.TotalSeconds <= 0) return;

            var bytesPerSecond = job.DownloadedBytes / elapsed.TotalSeconds;
            var remainingBytes = job.TotalSize - job.DownloadedBytes;
            var estimatedSeconds = remainingBytes / bytesPerSecond;

            job.EstimatedTimeRemaining = TimeSpan.FromSeconds(Math.Max(0, estimatedSeconds));
        }

        private string DetectChecksumAlgorithm(string checksum)
        {
            // Simple heuristic: length
            if (checksum.Length == 32)
                return "MD5";
            if (checksum.Length == 64)
                return "SHA256";
            // Default to SHA256
            return "SHA256";
        }

        private async Task<string> ComputeChecksumAsync(string filePath, string algorithm)
        {
            using var stream = File.OpenRead(filePath);
            if (algorithm == "MD5")
            {
                using var md5 = MD5.Create();
                var hash = await md5.ComputeHashAsync(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
            else // SHA256
            {
                using var sha256 = SHA256.Create();
                var hash = await sha256.ComputeHashAsync(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public void Dispose()
        {
            _chunkSemaphore?.Dispose();
            _networkService?.Dispose();
        }
    }
} 