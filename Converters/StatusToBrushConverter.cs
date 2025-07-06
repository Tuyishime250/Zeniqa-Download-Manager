using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Converters
{
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DownloadStatus status)
            {
                return status switch
                {
                    DownloadStatus.Pending => Application.Current.Resources["StatusPendingBrush"],
                    DownloadStatus.Downloading => Application.Current.Resources["StatusInProgressBrush"],
                    DownloadStatus.Completed => Application.Current.Resources["StatusCompletedBrush"],
                    DownloadStatus.Failed => Application.Current.Resources["StatusFailedBrush"],
                    DownloadStatus.Paused => Application.Current.Resources["StatusPausedBrush"],
                    _ => Application.Current.Resources["BorderBrush"]
                };
            }
            return Application.Current.Resources["BorderBrush"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 