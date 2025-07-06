 using System.Windows;
using ZeniqaDownloadManager.Models;

namespace ZeniqaDownloadManager.Views
{
    public partial class SettingsWindow : Window
    {
        private DownloadSettings _settings;

        public SettingsWindow(DownloadSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            LoadSettings();
            SetupEventHandlers();
        }

        private void LoadSettings()
        {
            MaxChunksSlider.Value = _settings.MaxConcurrentChunks;
            MaxChunksValue.Text = _settings.MaxConcurrentChunks.ToString();

            TimeoutSlider.Value = _settings.TimeoutSeconds;
            TimeoutValue.Text = $"{_settings.TimeoutSeconds} seconds";

            // Set buffer size combo box
            foreach (var item in BufferSizeComboBox.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem comboItem && 
                    comboItem.Tag is int tagValue && tagValue == _settings.BufferSize)
                {
                    BufferSizeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SetupEventHandlers()
        {
            MaxChunksSlider.ValueChanged += (s, e) => 
                MaxChunksValue.Text = ((int)e.NewValue).ToString();
            
            TimeoutSlider.ValueChanged += (s, e) => 
                TimeoutValue.Text = $"{(int)e.NewValue} seconds";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.MaxConcurrentChunks = (int)MaxChunksSlider.Value;
                _settings.TimeoutSeconds = (int)TimeoutSlider.Value;

                if (BufferSizeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem &&
                    selectedItem.Tag is int bufferSize)
                {
                    _settings.BufferSize = bufferSize;
                }

                _settings.Save();
                DialogResult = true;
                Close();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}