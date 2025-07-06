using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    /// <summary>
    /// Enhanced networking service with optimized connection management and retry logic
    /// </summary>
    public class NetworkService : IDisposable
    {
        internal readonly HttpClient _httpClient;
        private readonly DownloadSettings _settings;
        private readonly SemaphoreSlim _connectionSemaphore;
        private bool _disposed = false;

        public NetworkService(DownloadSettings settings)
        {
            _settings = settings;
            
            // Configure HttpClient with optimized settings
            var handler = new HttpClientHandler
            {
                MaxConnectionsPerServer = settings.MaxConcurrentChunks * 2,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                UseCookies = false, // Disable cookies for better performance
                UseProxy = false,   // Disable proxy for direct connections
            };
            
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
            };

            // Configure global connection limits
            ServicePointManager.DefaultConnectionLimit = settings.MaxConcurrentChunks * 2;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            
            // Optimize connection pooling
            ServicePointManager.UseNagleAlgorithm = false;
            ServicePointManager.Expect100Continue = false;

            _connectionSemaphore = new SemaphoreSlim(settings.MaxConcurrentChunks, settings.MaxConcurrentChunks);
        }

        /// <summary>
        /// Downloads a chunk with enhanced retry logic and error handling
        /// </summary>
        public async Task<DownloadChunkResult> DownloadChunkAsync(
            string url, 
            DownloadChunk chunk, 
            string outputPath, 
            CancellationToken cancellationToken)
        {
            await _connectionSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                return await DownloadChunkWithRetryAsync(url, chunk, outputPath, cancellationToken);
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        private async Task<DownloadChunkResult> DownloadChunkWithRetryAsync(
            string url, 
            DownloadChunk chunk, 
            string outputPath, 
            CancellationToken cancellationToken)
        {
            const int maxRetries = 5;
            var attempt = 0;
            var delay = 1000; // Start with 1s

            while (true)
            {
                try
                {
                    chunk.MarkAsStarted();
                    
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(chunk.StartByte, chunk.EndByte);
                    
                    // Add performance headers
                    request.Headers.ConnectionClose = false; // Keep connection alive
                    request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    // Validate range response
                    if (response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        throw new NotSupportedException($"Server does not support range requests. Status: {response.StatusCode}");
                    }

                    // Verify content range header
                    if (response.Content.Headers.ContentRange == null)
                    {
                        throw new InvalidDataException("Server did not return Content-Range header");
                    }

                    var contentRange = response.Content.Headers.ContentRange;
                    if (contentRange.From != chunk.StartByte || contentRange.To != chunk.EndByte)
                    {
                        throw new InvalidDataException($"Range mismatch. Expected: {chunk.StartByte}-{chunk.EndByte}, Got: {contentRange.From}-{contentRange.To}");
                    }

                    // Download to file with optimized streaming
                    await DownloadChunkToFileAsync(response, outputPath, chunk, cancellationToken);
                    
                    chunk.MarkAsCompleted();
                    return new DownloadChunkResult { Success = true, DownloadedBytes = chunk.ChunkSize };
                }
                catch (Exception ex) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
                {
                    attempt++;
                    chunk.Status = DownloadChunkStatus.Pending;
                    chunk.ErrorMessage = $"Retry {attempt}/{maxRetries}: {ex.Message}";
                    
                    // Exponential backoff with jitter
                    var jitter = new Random().Next(100, 500);
                    await Task.Delay(delay + jitter, cancellationToken);
                    delay = Math.Min(delay * 2, 16000); // Max 16s delay
                }
                catch (Exception ex)
                {
                    chunk.MarkAsFailed($"All retries failed: {ex.Message}");
                    return new DownloadChunkResult { Success = false, ErrorMessage = ex.Message };
                }
            }
        }

        private async Task DownloadChunkToFileAsync(
            HttpResponseMessage response, 
            string outputPath, 
            DownloadChunk chunk, 
            CancellationToken cancellationToken)
        {
            // Use FileStream with optimized settings
            using var fileStream = new FileStream(
                outputPath, 
                FileMode.Create, 
                FileAccess.Write, 
                FileShare.None, 
                _settings.BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[_settings.BufferSize];
            var totalBytesRead = 0L;
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            while (totalBytesRead < chunk.ChunkSize)
            {
                var bytesToRead = Math.Min(buffer.Length, chunk.ChunkSize - totalBytesRead);
                var bytesRead = await stream.ReadAsync(buffer, 0, (int)bytesToRead, cancellationToken);
                
                if (bytesRead == 0) break; // End of stream
                
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalBytesRead += bytesRead;
                
                // Update progress
                chunk.DownloadedBytes = totalBytesRead;
                
                // Trigger UI update more frequently
                if (totalBytesRead % (_settings.BufferSize * 10) == 0) // Update every 10 buffer reads
                {
                    // Force UI refresh
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() => { });
                }
            }

            await fileStream.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Checks if a URL supports range requests
        /// </summary>
        public async Task<bool> SupportsRangeRequestsAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                
                return response.Headers.AcceptRanges.Contains("bytes");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the content length of a URL
        /// </summary>
        public async Task<long> GetContentLengthAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                return response.Content.Headers.ContentLength ?? 0;
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to get content length: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs a HEAD request to get file information
        /// </summary>
        public async Task<FileInfo> GetFileInfoAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                return new FileInfo
                {
                    Size = response.Content.Headers.ContentLength ?? 0,
                    SupportsRange = response.Headers.AcceptRanges.Contains("bytes"),
                    LastModified = response.Content.Headers.LastModified?.DateTime,
                    ContentType = response.Content.Headers.ContentType?.ToString(),
                    ETag = response.Headers.ETag?.ToString()
                };
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to get file info: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _connectionSemaphore?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Result of a chunk download operation
    /// </summary>
    public class DownloadChunkResult
    {
        public bool Success { get; set; }
        public long DownloadedBytes { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// File information from HEAD request
    /// </summary>
    public class FileInfo
    {
        public long Size { get; set; }
        public bool SupportsRange { get; set; }
        public DateTime? LastModified { get; set; }
        public string? ContentType { get; set; }
        public string? ETag { get; set; }
    }
} 