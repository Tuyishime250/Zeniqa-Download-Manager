using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ZeniqaDownloadManager.Models;
using ZeniqaDownloadManager.Services;

namespace ZeniqaDownloadManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
    public partial class MainWindow : Window
    {
        private readonly UrlAnalyzer _urlAnalyzer;
        private readonly DownloadManager _downloadManager;
        private readonly DownloadSettings _settings;
        private DownloadItem? _currentItem;

        public MainWindow()
        {
            InitializeComponent();
            _settings = DownloadSettings.Load();
            _urlAnalyzer = new UrlAnalyzer();
            _downloadManager = new DownloadManager(_settings);
            
            // Set DataContext to enable binding
            DataContext = _downloadManager;
            
            // Subscribe to download manager events
            _downloadManager.JobAdded += OnJobAdded;
            _downloadManager.JobStarted += OnJobStarted;
            _downloadManager.JobCompleted += OnJobCompleted;
            _downloadManager.JobFailed += OnJobFailed;
        }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Please enter a valid URL.", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Disable UI during analysis
            AnalyzeButton.IsEnabled = false;
            StatusTextBlock.Text = "Analyzing...";

            // Analyze the URL
            _currentItem = await _urlAnalyzer.AnalyzeUrlAsync(url);

            // Update UI with results
            UpdateResultsDisplay(_currentItem);

            // Enable download button if analysis was successful
            DownloadButton.IsEnabled = _currentItem.Status == DownloadStatus.Pending;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error analyzing URL: {ex.Message}", "Analysis Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ClearResults();
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
        }
    }

    private void UpdateResultsDisplay(DownloadItem item)
    {
        TypeTextBlock.Text = item.Type.ToString();
        TitleTextBlock.Text = item.Title;
        ExtensionTextBlock.Text = item.FileExtension ?? "Unknown";
        
        SizeTextBlock.Text = item.FileSize.HasValue 
            ? FormatFileSize(item.FileSize.Value) 
            : "Unknown";
        
        DurationTextBlock.Text = item.Duration.HasValue 
            ? item.Duration.Value.ToString(@"hh\:mm\:ss") 
            : "Unknown";
        
        StatusTextBlock.Text = item.Status.ToString();

        // Build details text
        var details = new System.Text.StringBuilder();
        details.AppendLine($"Original URL: {item.OriginalUrl}");
        details.AppendLine($"Download Type: {item.Type}");
        
        if (item.SegmentUrls.Any())
        {
            details.AppendLine($"\nSegment URLs ({item.SegmentUrls.Count}):");
            foreach (var segmentUrl in item.SegmentUrls.Take(5)) // Show first 5
            {
                details.AppendLine($"  • {segmentUrl}");
            }
            if (item.SegmentUrls.Count > 5)
            {
                details.AppendLine($"  ... and {item.SegmentUrls.Count - 5} more");
            }
        }

        if (item.Metadata.Any())
        {
            details.AppendLine("\nMetadata:");
            foreach (var kvp in item.Metadata)
            {
                details.AppendLine($"  • {kvp.Key}: {kvp.Value}");
            }
        }

        if (item.Status == DownloadStatus.Failed)
        {
            details.AppendLine($"\n Analysis failed: {item.Metadata.GetValueOrDefault("Error", "Unknown error")}");
        }
        else if (item.Status == DownloadStatus.Pending)
        {
            details.AppendLine("\n URL analysis completed successfully!");
        }

        DetailsTextBlock.Text = details.ToString();
    }

    private void ClearResults()
    {
        TypeTextBlock.Text = "-";
        TitleTextBlock.Text = "-";
        ExtensionTextBlock.Text = "-";
        SizeTextBlock.Text = "-";
        DurationTextBlock.Text = "-";
        StatusTextBlock.Text = "-";
        DetailsTextBlock.Text = "Enter a URL and click Analyze to see details.";
        DownloadButton.IsEnabled = false;
        _currentItem = null;
    }

    private string FormatFileSize(long bytes)
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

            private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentItem != null)
            {
                try
                {
                    DownloadButton.IsEnabled = false;
                    var job = await _downloadManager.AddJobAsync(_currentItem);
                    
                    MessageBox.Show($"Added to download queue!\n\n" +
                                  $"Title: {job.Title}\n" +
                                  $"Type: {job.Type}\n" +
                                  $"Output: {job.OutputPath}", 
                                  "Added to Queue", MessageBoxButton.OK, MessageBoxImage.Information);
                    // Reset UI for next download
                    ClearResults();
                    UrlTextBox.Text = "";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to add to queue: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    DownloadButton.IsEnabled = true;
                }
            }
        }

            private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            UrlTextBox.Text = "";
            ClearResults();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = new Views.SettingsWindow(_settings);
                settingsWindow.Owner = this;
                
                if (settingsWindow.ShowDialog() == true)
                {
                    // Settings were saved, show a message
                    MessageBox.Show("Settings saved! New downloads will use these settings.", 
                        "Settings Updated", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

            private void OnJobAdded(object? sender, DownloadJob job)
        {
            Dispatcher.Invoke(() =>
            {
                // Update UI to show job was added
                DetailsTextBlock.Text += $"\n\n Added to download queue: {job.Title}";
            });
        }

        private void OnJobStarted(object? sender, DownloadJob job)
        {
            Dispatcher.Invoke(() =>
            {
                DetailsTextBlock.Text += $"\n Started downloading: {job.Title}";
                DetailsTextBlock.Text += $"\n File size: {FormatFileSize(job.TotalSize)}";
                DetailsTextBlock.Text += $"\n Output: {job.OutputPath}";
            });
        }

        private void OnJobCompleted(object? sender, DownloadJob job)
        {
            Dispatcher.Invoke(() =>
            {
                DetailsTextBlock.Text += $"\n Completed: {job.Title}";
            });
        }

        private void OnJobFailed(object? sender, DownloadJob job)
        {
            Dispatcher.Invoke(() =>
            {
                DetailsTextBlock.Text += $"\n Failed: {job.Title} - {job.ErrorMessage}";
            });
        }

        private void PauseAllButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement pause all functionality
        }

        private void ResumeAllButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement resume all functionality
        }

        private void PauseJob_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is DownloadJob job)
            {
                try
                {
                    // Pause the active job
                    _downloadManager.PauseJob(job.Id);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to pause job: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void StartDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is DownloadJob job)
            {
                try
                {
                    // Start the download immediately
                    _downloadManager.StartJobAsync(job);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to start download: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ResumeJob_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is DownloadJob job)
            {
                try
                {
                    // Resume the paused job
                    _downloadManager.ResumeJob(job.Id);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to resume job: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _urlAnalyzer?.Dispose();
            _downloadManager?.Dispose();
            base.OnClosed(e);
        }
}