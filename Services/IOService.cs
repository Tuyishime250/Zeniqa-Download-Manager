using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Services
{
    /// <summary>
    /// Enhanced I/O service with optimized file operations and error handling
    /// </summary>
    public class IOService
    {
        private readonly DownloadSettings _settings;

        public IOService(DownloadSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Creates a file stream with optimized settings for downloading
        /// </summary>
        public FileStream CreateDownloadStream(string filePath, FileMode mode = FileMode.Create)
        {
            return new FileStream(
                filePath,
                mode,
                FileAccess.Write,
                FileShare.Read, // Allow other processes to read the file
                _settings.BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        /// <summary>
        /// Creates a file stream with retry logic for handling file locks
        /// </summary>
        public FileStream CreateDownloadStreamWithRetry(string filePath, FileMode mode = FileMode.Create, int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return new FileStream(
                        filePath,
                        mode,
                        FileAccess.Write,
                        FileShare.Read, // Allow other processes to read the file
                        _settings.BufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                }
                catch (IOException ex) when (attempt < maxRetries && 
                    (ex.Message.Contains("being used by another process") || 
                     ex.Message.Contains("The process cannot access the file") ||
                     ex.Message.Contains("being used by another system")))
                {
                    // Wait a bit before retrying
                    System.Threading.Thread.Sleep(1000 * attempt);
                }
            }
            
            // If all retries failed, throw the last exception
            return new FileStream(
                filePath,
                mode,
                FileAccess.Write,
                FileShare.Read,
                _settings.BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        /// <summary>
        /// Creates a file stream for reading with optimized settings
        /// </summary>
        public FileStream CreateReadStream(string filePath)
        {
            return new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _settings.BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        /// <summary>
        /// Safely deletes a file with error handling
        /// </summary>
        public bool SafeDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Safely moves a file with error handling
        /// </summary>
        public bool SafeMoveFile(string sourcePath, string destinationPath)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                File.Move(sourcePath, destinationPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures directory exists, creates if it doesn't
        /// </summary>
        public void EnsureDirectoryExists(string directoryPath)
        {
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        /// <summary>
        /// Gets available disk space for a path
        /// </summary>
        public long GetAvailableDiskSpace(string path)
        {
            try
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(path) ?? "C:\\");
                return driveInfo.AvailableFreeSpace;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Checks if there's enough disk space for a file
        /// </summary>
        public bool HasEnoughDiskSpace(string path, long requiredBytes)
        {
            var availableSpace = GetAvailableDiskSpace(path);
            return availableSpace >= requiredBytes;
        }

        /// <summary>
        /// Merges multiple chunk files into a single output file
        /// </summary>
        public async Task MergeChunkFilesAsync(
            string[] chunkFiles, 
            string outputPath, 
            CancellationToken cancellationToken = default)
        {
            using var outputStream = CreateDownloadStream(outputPath);

            foreach (var chunkFile in chunkFiles)
            {
                if (!File.Exists(chunkFile))
                {
                    throw new FileNotFoundException($"Chunk file not found: {chunkFile}");
                }

                using var chunkStream = CreateReadStream(chunkFile);
                await chunkStream.CopyToAsync(outputStream, cancellationToken);
            }

            await outputStream.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Copies a stream to a file with progress reporting
        /// </summary>
        public async Task CopyStreamToFileAsync(
            Stream sourceStream, 
            string outputPath, 
            long expectedSize = 0,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using var fileStream = CreateDownloadStream(outputPath);
            var buffer = new byte[_settings.BufferSize];
            var totalBytesRead = 0L;

            while (true)
            {
                var bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0) break;

                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalBytesRead += bytesRead;

                progress?.Report(totalBytesRead);

                // Check if we've exceeded expected size (safety check)
                if (expectedSize > 0 && totalBytesRead > expectedSize)
                {
                    throw new InvalidOperationException($"Downloaded more bytes than expected: {totalBytesRead} > {expectedSize}");
                }
            }

            await fileStream.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Validates file integrity by checking file size
        /// </summary>
        public bool ValidateFileSize(string filePath, long expectedSize)
        {
            try
            {
                var fileInfo = new System.IO.FileInfo(filePath);
                return fileInfo.Length == expectedSize;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets file size safely
        /// </summary>
        public long GetFileSize(string filePath)
        {
            try
            {
                var fileInfo = new System.IO.FileInfo(filePath);
                return fileInfo.Length;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Cleans up temporary files
        /// </summary>
        public void CleanupTempFiles(params string[] filePaths)
        {
            foreach (var filePath in filePaths)
            {
                SafeDeleteFile(filePath);
            }
        }

        /// <summary>
        /// Creates a temporary file path for chunk downloads
        /// </summary>
        public string CreateTempFilePath(string basePath, int chunkIndex)
        {
            return $"{basePath}.part{chunkIndex:D3}";
        }

        /// <summary>
        /// Formats file size for display
        /// </summary>
        public static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Checks if a file is locked by another process
        /// </summary>
        public bool IsFileLocked(string filePath)
        {
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false; // File is not locked
            }
            catch (IOException)
            {
                return true; // File is locked
            }
            catch
            {
                return false; // File doesn't exist or other error
            }
        }

        /// <summary>
        /// Gets a list of processes that might be using the file
        /// </summary>
        public string GetFileUsageInfo(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return "File does not exist";

                if (IsFileLocked(filePath))
                {
                    return "File is locked by another process. Common causes:\n" +
                           "• Media player is playing the file\n" +
                           "• File Explorer is showing the file\n" +
                           "• Antivirus is scanning the file\n" +
                           "• Another download is in progress\n" +
                           "• File is open in an editor";
                }

                return "File is accessible";
            }
            catch
            {
                return "Unable to check file status";
            }
        }
    }
} 