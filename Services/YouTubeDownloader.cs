using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    public class YouTubeDownloader : IDownloader
    {
        private readonly string _ytdlpPath;

        public YouTubeDownloader()
        {
            _ytdlpPath = FindYtDlpPath();
        }

        public async Task DownloadAsync(DownloadJob job, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrEmpty(_ytdlpPath))
                {
                    throw new System.IO.FileNotFoundException("yt-dlp not found. Please install yt-dlp to download YouTube videos.");
                }

                // Create output directory if it doesn't exist
                var outputDir = Path.GetDirectoryName(job.OutputPath);
                if (!string.IsNullOrEmpty(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _ytdlpPath,
                    Arguments = BuildYtDlpArguments(job),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                // Monitor progress
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(100, cancellationToken);
                            
                            // Check if output file exists and update progress
                            if (File.Exists(job.OutputPath))
                            {
                                var fileInfo = new System.IO.FileInfo(job.OutputPath);
                                job.DownloadedBytes = fileInfo.Length;
                                
                                if (job.TotalSize > 0)
                                {
                                    UpdateEstimatedTimeRemaining(job, fileInfo.Length);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancelled
                    }
                });

                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException($"yt-dlp failed with exit code {process.ExitCode}: {error}");
                }

                // Verify file was downloaded
                if (!File.Exists(job.OutputPath))
                {
                    throw new IOException("Download completed but output file not found");
                }

                // Update final size
                var finalFileInfo = new System.IO.FileInfo(job.OutputPath);
                job.TotalSize = finalFileInfo.Length;
                job.DownloadedBytes = finalFileInfo.Length;
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to download YouTube video: {ex.Message}");
            }
        }

        private string BuildYtDlpArguments(DownloadJob job)
        {
            var args = new System.Text.StringBuilder();
            
            // Output format
            args.Append($"-o \"{job.OutputPath}\" ");
            
            // Best quality with audio
            args.Append("-f best ");
            
            // Progress format (for monitoring)
            args.Append("--progress-template \"download:%(progress.downloaded_bytes)s/%(progress.total_bytes)s\" ");
            
            // URL
            args.Append($"\"{job.OriginalUrl}\"");
            
            return args.ToString();
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

        private void UpdateEstimatedTimeRemaining(DownloadJob job, long downloadedBytes)
        {
            if (job.TotalSize <= 0 || downloadedBytes <= 0) return;

            var elapsed = DateTime.Now - job.StartTime;
            if (elapsed.TotalSeconds <= 0) return;

            var bytesPerSecond = downloadedBytes / elapsed.TotalSeconds;
            var remainingBytes = job.TotalSize - downloadedBytes;
            var estimatedSeconds = remainingBytes / bytesPerSecond;

            job.EstimatedTimeRemaining = TimeSpan.FromSeconds(Math.Max(0, estimatedSeconds));
        }
    }
} 