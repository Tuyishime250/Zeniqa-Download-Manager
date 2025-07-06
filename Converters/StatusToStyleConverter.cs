using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Converters
{
    public class StatusToStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DownloadStatus status)
            {
                return status switch
                {
                    DownloadStatus.Pending => Application.Current.Resources["StatusPendingStyle"],
                    DownloadStatus.Downloading => Application.Current.Resources["StatusInProgressStyle"],
                    DownloadStatus.Completed => Application.Current.Resources["StatusCompletedStyle"],
                    DownloadStatus.Failed => Application.Current.Resources["StatusFailedStyle"],
                    DownloadStatus.Paused => Application.Current.Resources["StatusPausedStyle"],
                    _ => Application.Current.Resources["StatusTextStyle"]
                };
            }
            return Application.Current.Resources["StatusTextStyle"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 