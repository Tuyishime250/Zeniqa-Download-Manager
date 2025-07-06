using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    public class YouTubeService
    {
        private readonly string _ytdlpPath;

        public YouTubeService()
        {
            // Try to find yt-dlp in common locations
            _ytdlpPath = FindYtDlpPath();
        }

        public class YouTubeVideoInfo
        {
            public string Id { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public TimeSpan Duration { get; set; }
            public string Thumbnail { get; set; } = string.Empty;
            public List<YouTubeFormat> Formats { get; set; } = new List<YouTubeFormat>();
            public string Uploader { get; set; } = string.Empty;
            public DateTime UploadDate { get; set; }
            public long ViewCount { get; set; }
        }

        public class YouTubeFormat
        {
            public string FormatId { get; set; } = string.Empty;
            public string Extension { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public int? Width { get; set; }
            public int? Height { get; set; }
            public int? Bitrate { get; set; }
            public string? Codec { get; set; }
            public string? AudioCodec { get; set; }
            public long? Filesize { get; set; }
            public string? Vcodec { get; set; }
            public string? Acodec { get; set; }
            public bool HasVideo { get; set; }
            public bool HasAudio { get; set; }
        }

        public async Task<DownloadItem> AnalyzeYouTubeUrlAsync(string url)
        {
            var downloadItem = new DownloadItem
            {
                OriginalUrl = url,
                Type = DownloadType.YouTube,
                Status = DownloadStatus.Analyzing
            };

            try
            {
                if (string.IsNullOrEmpty(_ytdlpPath))
                {
                    downloadItem.Status = DownloadStatus.Failed;
                    downloadItem.Metadata["Error"] = "yt-dlp not found. Please install yt-dlp to download YouTube videos.";
                    return downloadItem;
                }

                var videoInfo = await GetVideoInfoAsync(url);
                if (videoInfo != null)
                {
                    downloadItem.Title = videoInfo.Title;
                    downloadItem.Duration = videoInfo.Duration;
                    downloadItem.Metadata["Uploader"] = videoInfo.Uploader;
                    downloadItem.Metadata["ViewCount"] = videoInfo.ViewCount.ToString();
                    downloadItem.Metadata["Thumbnail"] = videoInfo.Thumbnail;

                    // Select best format (video + audio, highest quality)
                    var bestFormat = SelectBestFormat(videoInfo.Formats);
                    if (bestFormat != null)
                    {
                        downloadItem.SegmentUrls.Add(bestFormat.Url);
                        downloadItem.FileExtension = bestFormat.Extension;
                        downloadItem.FileSize = bestFormat.Filesize;
                        downloadItem.Status = DownloadStatus.Pending;
                    }
                    else
                    {
                        downloadItem.Status = DownloadStatus.Failed;
                        downloadItem.Metadata["Error"] = "No suitable format found";
                    }
                }
                else
                {
                    downloadItem.Status = DownloadStatus.Failed;
                    downloadItem.Metadata["Error"] = "Failed to extract video information";
                }
            }
            catch (Exception ex)
            {
                downloadItem.Status = DownloadStatus.Failed;
                downloadItem.Metadata["Error"] = ex.Message;
            }

            return downloadItem;
        }

        private async Task<YouTubeVideoInfo?> GetVideoInfoAsync(string url)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _ytdlpPath,
                    Arguments = $"--dump-json \"{url}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    return ParseVideoInfo(output);
                }
                else
                {
                    throw new InvalidOperationException($"yt-dlp failed: {error}");
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to get video info: {ex.Message}");
            }
        }

        private YouTubeVideoInfo? ParseVideoInfo(string jsonOutput)
        {
            try
            {
                using var document = JsonDocument.Parse(jsonOutput);
                var root = document.RootElement;

                var videoInfo = new YouTubeVideoInfo
                {
                    Id = GetStringProperty(root, "id"),
                    Title = GetStringProperty(root, "title"),
                    Description = GetStringProperty(root, "description"),
                    Duration = TimeSpan.FromSeconds(GetDoubleProperty(root, "duration")),
                    Thumbnail = GetStringProperty(root, "thumbnail"),
                    Uploader = GetStringProperty(root, "uploader"),
                    UploadDate = ParseUploadDate(GetStringProperty(root, "upload_date")),
                    ViewCount = GetLongProperty(root, "view_count") ?? 0
                };

                // Parse formats
                if (root.TryGetProperty("formats", out var formatsElement))
                {
                    foreach (var formatElement in formatsElement.EnumerateArray())
                    {
                        var format = new YouTubeFormat
                        {
                            FormatId = GetStringProperty(formatElement, "format_id"),
                            Extension = GetStringProperty(formatElement, "ext"),
                            Url = GetStringProperty(formatElement, "url"),
                            Width = GetIntProperty(formatElement, "width"),
                            Height = GetIntProperty(formatElement, "height"),
                            Bitrate = GetIntProperty(formatElement, "bitrate"),
                            Codec = GetStringProperty(formatElement, "codec"),
                            AudioCodec = GetStringProperty(formatElement, "acodec"),
                            Filesize = GetLongProperty(formatElement, "filesize"),
                            Vcodec = GetStringProperty(formatElement, "vcodec"),
                            Acodec = GetStringProperty(formatElement, "acodec"),
                            HasVideo = GetBoolProperty(formatElement, "has_video"),
                            HasAudio = GetBoolProperty(formatElement, "has_audio")
                        };

                        videoInfo.Formats.Add(format);
                    }
                }

                return videoInfo;
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to parse video info: {ex.Message}");
            }
        }

        private YouTubeFormat? SelectBestFormat(List<YouTubeFormat> formats)
        {
            // Prefer formats with both video and audio
            var completeFormats = formats.Where(f => f.HasVideo && f.HasAudio).ToList();
            
            if (completeFormats.Any())
            {
                // Select highest quality (highest height, then highest bitrate)
                return completeFormats
                    .OrderByDescending(f => f.Height ?? 0)
                    .ThenByDescending(f => f.Bitrate ?? 0)
                    .FirstOrDefault();
            }

            // Fallback to any format with video
            var videoFormats = formats.Where(f => f.HasVideo).ToList();
            if (videoFormats.Any())
            {
                return videoFormats
                    .OrderByDescending(f => f.Height ?? 0)
                    .ThenByDescending(f => f.Bitrate ?? 0)
                    .FirstOrDefault();
            }

            return formats.FirstOrDefault();
        }

        private string FindYtDlpPath()
        {
            var possiblePaths = new[]
            {
                "yt-dlp",
                "yt-dlp.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages", "yt-dlp.yt-dlp_Microsoft.Winget.Source_8wekyb3d8bbwe", "yt-dlp.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "yt-dlp", "yt-dlp.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "yt-dlp", "yt-dlp.exe")
            };

            foreach (var path in possiblePaths)
            {
                try
                {
                    if (File.Exists(path) || IsExecutableInPath(path))
                    {
                        return path;
                    }
                }
                catch
                {
                    // Continue checking other paths
                }
            }

            return string.Empty;
        }

        private bool IsExecutableInPath(string executableName)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executableName,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                return process != null;
            }
            catch
            {
                return false;
            }
        }

        // Helper methods for JSON parsing
        private string GetStringProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? string.Empty : string.Empty;
        }

        private int? GetIntProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) ? prop.GetInt32() : null;
        }

        private long? GetLongProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) ? prop.GetInt64() : null;
        }

        private double GetDoubleProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) ? prop.GetDouble() : 0.0;
        }

        private bool GetBoolProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.GetBoolean();
        }

        private DateTime ParseUploadDate(string uploadDate)
        {
            if (string.IsNullOrEmpty(uploadDate) || uploadDate.Length != 8)
                return DateTime.MinValue;

            try
            {
                var year = int.Parse(uploadDate.Substring(0, 4));
                var month = int.Parse(uploadDate.Substring(4, 2));
                var day = int.Parse(uploadDate.Substring(6, 2));
                return new DateTime(year, month, day);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
} 