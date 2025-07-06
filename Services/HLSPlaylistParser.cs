using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    public class HLSPlaylistParser
    {
        private readonly HttpClient _httpClient;

        public HLSPlaylistParser()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public class HLSStreamInfo
        {
            public string Url { get; set; } = string.Empty;
            public int? Bandwidth { get; set; }
            public int? Width { get; set; }
            public int? Height { get; set; }
            public string? Codecs { get; set; }
            public string? AudioGroup { get; set; }
            public string? SubtitleGroup { get; set; }
        }

        public async Task<List<string>> ParsePlaylistAsync(string playlistUrl)
        {
            try
            {
                var content = await _httpClient.GetStringAsync(playlistUrl);
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var baseUrl = GetBaseUrl(playlistUrl);

                // Check if this is a master playlist
                if (IsMasterPlaylist(lines))
                {
                    return await ParseMasterPlaylistAsync(lines, baseUrl);
                }
                else
                {
                    return ParseMediaPlaylist(lines, baseUrl);
                }
            }
            catch (Exception ex)
            {
                throw new System.IO.IOException($"Failed to parse HLS playlist: {ex.Message}");
            }
        }

        private bool IsMasterPlaylist(string[] lines)
        {
            return lines.Any(line => line.Trim().StartsWith("#EXT-X-STREAM-INF"));
        }

        private async Task<List<string>> ParseMasterPlaylistAsync(string[] lines, string baseUrl)
        {
            var streams = new List<HLSStreamInfo>();
            var currentStream = new HLSStreamInfo();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (line.StartsWith("#EXT-X-STREAM-INF"))
                {
                    // Parse stream info
                    currentStream = ParseStreamInfo(line);
                }
                else if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
                {
                    // This is a stream URL
                    currentStream.Url = ResolveUrl(line, baseUrl);
                    streams.Add(currentStream);
                    currentStream = new HLSStreamInfo();
                }
            }

            // Select the best quality stream (highest bandwidth)
            var bestStream = streams.OrderByDescending(s => s.Bandwidth).FirstOrDefault();
            if (bestStream != null)
            {
                return await ParsePlaylistAsync(bestStream.Url);
            }

            return new List<string>();
        }

        private List<string> ParseMediaPlaylist(string[] lines, string baseUrl)
        {
            var segmentUrls = new List<string>();
            var segmentDuration = 0.0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (line.StartsWith("#EXTINF"))
                {
                    // Parse segment duration
                    var durationMatch = Regex.Match(line, @"#EXTINF:([\d.]+)");
                    if (durationMatch.Success)
                    {
                        segmentDuration = double.Parse(durationMatch.Groups[1].Value);
                    }
                }
                else if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
                {
                    // This is a segment URL
                    var segmentUrl = ResolveUrl(line, baseUrl);
                    segmentUrls.Add(segmentUrl);
                }
            }

            return segmentUrls;
        }

        private HLSStreamInfo ParseStreamInfo(string line)
        {
            var streamInfo = new HLSStreamInfo();

            // Parse bandwidth
            var bandwidthMatch = Regex.Match(line, @"BANDWIDTH=(\d+)");
            if (bandwidthMatch.Success)
            {
                streamInfo.Bandwidth = int.Parse(bandwidthMatch.Groups[1].Value);
            }

            // Parse resolution
            var resolutionMatch = Regex.Match(line, @"RESOLUTION=(\d+)x(\d+)");
            if (resolutionMatch.Success)
            {
                streamInfo.Width = int.Parse(resolutionMatch.Groups[1].Value);
                streamInfo.Height = int.Parse(resolutionMatch.Groups[2].Value);
            }

            // Parse codecs
            var codecsMatch = Regex.Match(line, @"CODECS=""([^""]+)""");
            if (codecsMatch.Success)
            {
                streamInfo.Codecs = codecsMatch.Groups[1].Value;
            }

            // Parse audio group
            var audioMatch = Regex.Match(line, @"AUDIO=""([^""]+)""");
            if (audioMatch.Success)
            {
                streamInfo.AudioGroup = audioMatch.Groups[1].Value;
            }

            return streamInfo;
        }

        private string ResolveUrl(string url, string baseUrl)
        {
            if (url.StartsWith("http"))
                return url;

            if (url.StartsWith("/"))
                return baseUrl + url;

            return baseUrl + "/" + url;
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