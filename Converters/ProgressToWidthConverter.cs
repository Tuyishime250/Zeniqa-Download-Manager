using System;
using System.Globalization;
using System.Windows.Data;

namespace ZeniqaDownloadManager.Converters
{
    public class ProgressToWidthConverter : IValueConverter
    {
        public static readonly ProgressToWidthConverter Instance = new ProgressToWidthConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double progress)
            {
                // Convert progress (0-100) to width percentage (0-1)
                return Math.Max(0, Math.Min(1, progress / 100.0));
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 