using System;
using System.Collections.Generic;

namespace ZeniqaDownloadManager.Models
{
    public enum DownloadType
    {
        DirectFile,
        HLSStream,
        DASHStream,
        YouTube,
        Unknown
    }

    public class DownloadItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string OriginalUrl { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DownloadType Type { get; set; }
        public string? FileExtension { get; set; }
        public long? FileSize { get; set; }
        public TimeSpan? Duration { get; set; }
        public List<string> SegmentUrls { get; set; } = new List<string>();
        public string? MasterPlaylistUrl { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DownloadStatus Status { get; set; } = DownloadStatus.Pending;
        public double Progress { get; set; } = 0.0;
        public string? OutputPath { get; set; }
    }

    public enum DownloadStatus
    {
        Pending,
        Analyzing,
        Downloading,
        Paused,
        Completed,
        Failed,
        Cancelled
    }
} 