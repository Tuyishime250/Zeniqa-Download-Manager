using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    public class UrlAnalyzer
    {
        private readonly HttpClient _httpClient;
        private readonly string[] _videoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v" };
        private readonly string[] _audioExtensions = { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" };
        private readonly string[] _archiveExtensions = { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2" };
        private readonly string[] _documentExtensions = { ".pdf", ".doc", ".docx", ".txt", ".rtf" };

        public UrlAnalyzer()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<DownloadItem> AnalyzeUrlAsync(string url)
        {
            var downloadItem = new DownloadItem
            {
                OriginalUrl = url,
                Status = DownloadStatus.Analyzing
            };

            try
            {
                // Detect URL type
                downloadItem.Type = DetectUrlType(url);

                switch (downloadItem.Type)
                {
                    case DownloadType.DirectFile:
                        await AnalyzeDirectFileAsync(downloadItem);
                        break;
                    case DownloadType.HLSStream:
                        await AnalyzeHLSStreamAsync(downloadItem);
                        break;
                    case DownloadType.DASHStream:
                        await AnalyzeDASHStreamAsync(downloadItem);
                        break;
                    case DownloadType.YouTube:
                        await AnalyzeYouTubeUrlAsync(downloadItem);
                        break;
                    default:
                        downloadItem.Status = DownloadStatus.Failed;
                        break;
                }
            }
            catch (Exception ex)
            {
                downloadItem.Status = DownloadStatus.Failed;
                downloadItem.Metadata["Error"] = ex.Message;
            }

            return downloadItem;
        }

        private DownloadType DetectUrlType(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return DownloadType.Unknown;

            url = url.ToLowerInvariant();

            // Check for YouTube URLs
            if (IsYouTubeUrl(url))
                return DownloadType.YouTube;

            // Check for direct file links
            if (IsDirectFileUrl(url))
                return DownloadType.DirectFile;

            // Check for HLS streams
            if (url.Contains(".m3u8") || url.Contains("hls"))
                return DownloadType.HLSStream;

            // Check for DASH streams
            if (url.Contains(".mpd") || url.Contains("dash"))
                return DownloadType.DASHStream;

            return DownloadType.Unknown;
        }

        private bool IsYouTubeUrl(string url)
        {
            var youtubePatterns = new[]
            {
                @"youtube\.com/watch\?v=",
                @"youtu\.be/",
                @"youtube\.com/embed/",
                @"youtube\.com/v/",
                @"youtube\.com/shorts/"
            };

            return youtubePatterns.Any(pattern => Regex.IsMatch(url, pattern));
        }

        private bool IsDirectFileUrl(string url)
        {
            var allExtensions = _videoExtensions
                .Concat(_audioExtensions)
                .Concat(_archiveExtensions)
                .Concat(_documentExtensions);

            return allExtensions.Any(ext => url.Contains(ext));
        }

        private async Task AnalyzeDirectFileAsync(DownloadItem item)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, item.OriginalUrl);
                using var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    // Extract file extension from URL
                    var uri = new Uri(item.OriginalUrl);
                    var path = uri.AbsolutePath;
                    item.FileExtension = Path.GetExtension(path);

                    // Get file size if available
                    if (response.Content.Headers.ContentLength.HasValue)
                    {
                        item.FileSize = response.Content.Headers.ContentLength.Value;
                    }

                    // Generate title from filename
                    item.Title = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrWhiteSpace(item.Title))
                    {
                        item.Title = $"Download_{DateTime.Now:yyyyMMdd_HHmmss}";
                    }

                    item.Status = DownloadStatus.Pending;
                }
                else
                {
                    item.Status = DownloadStatus.Failed;
                    item.Metadata["Error"] = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                }
            }
            catch (Exception ex)
            {
                item.Status = DownloadStatus.Failed;
                item.Metadata["Error"] = ex.Message;
            }
        }

        private async Task AnalyzeHLSStreamAsync(DownloadItem item)
        {
            try
            {
                var hlsParser = new HLSPlaylistParser();
                var segmentUrls = await hlsParser.ParsePlaylistAsync(item.OriginalUrl);

                item.MasterPlaylistUrl = item.OriginalUrl;
                item.Title = $"HLS_Stream_{DateTime.Now:yyyyMMdd_HHmmss}";
                item.SegmentUrls = segmentUrls;
                item.FileExtension = ".ts"; // HLS typically uses .ts segments
                item.Status = DownloadStatus.Pending;

                hlsParser.Dispose();
            }
            catch (Exception ex)
            {
                item.Status = DownloadStatus.Failed;
                item.Metadata["Error"] = ex.Message;
            }
        }

        private async Task AnalyzeDASHStreamAsync(DownloadItem item)
        {
            try
            {
                var content = await _httpClient.GetStringAsync(item.OriginalUrl);
                var baseUrl = GetBaseUrl(item.OriginalUrl);

                item.MasterPlaylistUrl = item.OriginalUrl;
                item.Title = $"DASH_Stream_{DateTime.Now:yyyyMMdd_HHmmss}";

                // Parse MPD content (simplified)
                var segmentUrls = new List<string>();
                
                // Extract segment URLs from MPD (basic implementation)
                var urlMatches = Regex.Matches(content, @"<SegmentURL media=""([^""]+)""");
                foreach (Match match in urlMatches)
                {
                    var segmentUrl = match.Groups[1].Value;
                    if (!segmentUrl.StartsWith("http"))
                    {
                        segmentUrl = baseUrl + "/" + segmentUrl.TrimStart('/');
                    }
                    segmentUrls.Add(segmentUrl);
                }

                item.SegmentUrls = segmentUrls;
                item.FileExtension = ".mp4"; // DASH typically uses .mp4 segments
                item.Status = DownloadStatus.Pending;
            }
            catch (Exception ex)
            {
                item.Status = DownloadStatus.Failed;
                item.Metadata["Error"] = ex.Message;
            }
        }

        private async Task AnalyzeYouTubeUrlAsync(DownloadItem item)
        {
            try
            {
                var youtubeService = new YouTubeService();
                var youtubeItem = await youtubeService.AnalyzeYouTubeUrlAsync(item.OriginalUrl);
                
                // Copy properties from YouTube analysis
                item.Title = youtubeItem.Title;
                item.Duration = youtubeItem.Duration;
                item.FileExtension = youtubeItem.FileExtension;
                item.FileSize = youtubeItem.FileSize;
                item.SegmentUrls = youtubeItem.SegmentUrls;
                item.Status = youtubeItem.Status;
                item.Metadata = youtubeItem.Metadata;
            }
            catch (Exception ex)
            {
                item.Status = DownloadStatus.Failed;
                item.Metadata["Error"] = ex.Message;
            }
        }

        private string GetBaseUrl(string url)
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
} 