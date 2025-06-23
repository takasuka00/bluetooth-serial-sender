using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.Win32;

namespace BluetoothSerialSender
{
    public partial class MainWindow : Window
    {
        private MultiDeviceManager _deviceManager = new MultiDeviceManager();
        private ObservableCollection<CsvDataItem> _csvData = new ObservableCollection<CsvDataItem>();
        private ObservableCollection<string> _connectedDevices = new ObservableCollection<string>();
        private DispatcherTimer _sendTimer;
        private Stopwatch _stopwatch = new Stopwatch();
        private int _currentIndex = 0;
        private bool _isRunning = false;
        private bool _isUpdatingTextBox = false;

        public MainWindow()
        {
            InitializeComponent();
            
            _sendTimer = new DispatcherTimer();
            _sendTimer.Tick += SendTimer_Tick;
            
            CsvDataGrid.ItemsSource = _csvData;
            ConnectedDevicesList.ItemsSource = _connectedDevices;
        }

        private void SelectDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DeviceSelectionDialog();
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true)
            {
                ConnectToDevices(dialog.SelectedPorts);
            }
        }

        private void ConnectToDevices(string[] portNames)
        {
            // ボーレートを取得
            int baudRate = 9600;
            if (BaudRateComboBox.SelectedItem != null)
            {
                string selectedBaudRate = ((ComboBoxItem)BaudRateComboBox.SelectedItem).Content.ToString()!;
                baudRate = int.Parse(selectedBaudRate);
            }

            int successCount = 0;
            int failCount = 0;
            
            foreach (var portName in portNames)
            {
                try
                {
                    _deviceManager.ConnectDevice(portName, baudRate);
                    _connectedDevices.Add(portName);
                    successCount++;
                    LogMessage($"接続しました: {portName}");
                }
                catch (Exception ex)
                {
                    failCount++;
                    LogMessage($"接続失敗: {portName} - {ex.Message}");
                }
            }

            UpdateConnectionStatus();
            
            if (failCount > 0)
            {
                MessageBox.Show($"{successCount}台接続成功、{failCount}台接続失敗", 
                    "接続結果", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (successCount > 0)
            {
                MessageBox.Show($"{successCount}台のデバイスに接続しました", 
                    "接続成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DisconnectAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                StopSending();
            }
            
            _deviceManager.DisconnectAllDevices();
            _connectedDevices.Clear();
            UpdateConnectionStatus();
            LogMessage("すべてのデバイスを切断しました");
        }

        private void DisconnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string portName)
            {
                _deviceManager.DisconnectDevice(portName);
                _connectedDevices.Remove(portName);
                UpdateConnectionStatus();
                LogMessage($"切断しました: {portName}");
            }
        }

        private void UpdateConnectionStatus()
        {
            int count = _deviceManager.ConnectedDeviceCount;
            
            if (count == 0)
            {
                ConnectionStatusText.Text = "未接続";
                ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
                DisconnectAllButton.IsEnabled = false;
                ManualSendButton.IsEnabled = false;
            }
            else
            {
                ConnectionStatusText.Text = $"{count}台接続中";
                ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Green;
                DisconnectAllButton.IsEnabled = true;
                ManualSendButton.IsEnabled = true;
            }
            
            UpdateControlButtons();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
                DefaultExt = ".csv"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                FilePathTextBox.Text = openFileDialog.FileName;
            }
        }

        private void LoadCsvButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(FilePathTextBox.Text))
            {
                MessageBox.Show("CSVファイルを選択してください。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(FilePathTextBox.Text))
            {
                MessageBox.Show("選択されたファイルが存在しません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                LoadCsvFile(FilePathTextBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"CSVファイルの読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage($"CSV読み込みエラー: {ex.Message}");
            }
        }

        private void LoadCsvFile(string filePath)
        {
            _csvData.Clear();
            
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = CsvHelper.Configuration.TrimOptions.Trim,
                HeaderValidated = null, // ヘッダー検証を無効化
                MissingFieldFound = null // 欠落フィールドの検証を無効化
            };

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, config))
            {
                try
                {
                    var records = csv.GetRecords<CsvRecord>().ToList();
                    
                    foreach (var record in records)
                    {
                        _csvData.Add(new CsvDataItem
                        {
                            Time = record.Time,
                            Data = record.Data,
                            HexData = $"0x{record.Data:X2}",
                            IsSent = false
                        });
                    }
                }
                catch (HeaderValidationException ex)
                {
                    // ヘッダーエラーの詳細を表示
                    MessageBox.Show($"CSVファイルのヘッダーが正しくありません。\n期待されるヘッダー: time, data\n\n詳細: {ex.Message}", 
                        "CSVフォーマットエラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogMessage($"CSVヘッダーエラー: {ex.Message}");
                    return;
                }
                catch (Exception ex)
                {
                    throw new Exception($"CSV読み込みエラー: {ex.Message}", ex);
                }
            }

            ProgressText.Text = $"0 / {_csvData.Count}";
            UpdateControlButtons();
            
            LogMessage($"CSVファイルを読み込みました: {_csvData.Count}件のデータ");
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartSending();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopSending();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetSending();
        }

        private void StartSending()
        {
            if (_csvData.Count == 0)
            {
                MessageBox.Show("送信するデータがありません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_deviceManager.ConnectedDeviceCount == 0)
            {
                MessageBox.Show("接続されているデバイスがありません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isRunning = true;
            _stopwatch.Start();
            _sendTimer.Interval = TimeSpan.FromMilliseconds(10); // 10ms間隔でチェック
            _sendTimer.Start();

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            ResetButton.IsEnabled = false;
            
            LogMessage($"送信を開始しました ({_deviceManager.ConnectedDeviceCount}台のデバイスへ)");
        }

        private void StopSending()
        {
            _isRunning = false;
            _stopwatch.Stop();
            _sendTimer.Stop();

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            ResetButton.IsEnabled = true;
            
            LogMessage("送信を停止しました");
        }

        private void ResetSending()
        {
            _currentIndex = 0;
            _stopwatch.Reset();
            
            foreach (var item in _csvData)
            {
                item.IsSent = false;
            }

            ElapsedTimeText.Text = "0.00 s";
            ProgressBar.Value = 0;
            ProgressText.Text = $"0 / {_csvData.Count}";
            
            UpdateControlButtons();
            
            LogMessage("リセットしました");
        }

        private void SendTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isRunning || _currentIndex >= _csvData.Count)
            {
                if (_currentIndex >= _csvData.Count)
                {
                    StopSending();
                    LogMessage("すべてのデータを送信しました");
                }
                return;
            }

            double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            ElapsedTimeText.Text = $"{elapsedSeconds:F2} s";

            // 現在の時間に該当するデータを送信
            while (_currentIndex < _csvData.Count && _csvData[_currentIndex].Time <= elapsedSeconds)
            {
                var dataItem = _csvData[_currentIndex];
                SendByte((byte)dataItem.Data);
                dataItem.IsSent = true;

                // 現在の行をスクロール表示
                CsvDataGrid.ScrollIntoView(dataItem);
                CsvDataGrid.SelectedItem = dataItem;

                _currentIndex++;
                
                // プログレスバーの更新
                ProgressBar.Value = (_currentIndex / (double)_csvData.Count) * 100;
                ProgressText.Text = $"{_currentIndex} / {_csvData.Count}";
            }
        }

        private void SendByte(byte data)
        {
            if (_deviceManager.ConnectedDeviceCount == 0)
            {
                LogMessage("エラー: 接続されているデバイスがありません");
                return;
            }

            try
            {
                byte[] dataArray = new byte[] { data };
                _deviceManager.WriteToAllDevices(dataArray);
                LogMessage($"送信: {data} (0x{data:X2}) → {_deviceManager.ConnectedDeviceCount}台");
            }
            catch (Exception ex)
            {
                LogMessage($"送信エラー: {ex.Message}");
                StopSending();
            }
        }

        private void ManualSendButton_Click(object sender, RoutedEventArgs e)
        {
            if (byte.TryParse(ManualDecimalTextBox.Text, out byte value))
            {
                SendByte(value);
            }
            else
            {
                MessageBox.Show("有効な値を入力してください (0-255)。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ManualDecimalTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingTextBox) return;
            
            _isUpdatingTextBox = true;
            
            if (byte.TryParse(ManualDecimalTextBox.Text, out byte value))
            {
                ManualHexTextBox.Text = $"{value:X2}";
            }
            else
            {
                ManualHexTextBox.Text = "";
            }
            
            _isUpdatingTextBox = false;
        }

        private void ManualHexTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingTextBox) return;
            
            _isUpdatingTextBox = true;
            
            string hexText = ManualHexTextBox.Text.Replace("0x", "").Replace("0X", "");
            
            if (byte.TryParse(hexText, NumberStyles.HexNumber, null, out byte value))
            {
                ManualDecimalTextBox.Text = value.ToString();
            }
            else
            {
                ManualDecimalTextBox.Text = "";
            }
            
            _isUpdatingTextBox = false;
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
        }

        private void LogMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] {message}\n";
            
            Dispatcher.BeginInvoke(() =>
            {
                LogTextBox.AppendText(logEntry);
                LogTextBox.ScrollToEnd();
            });
        }

        private void UpdateControlButtons()
        {
            bool hasData = _csvData.Count > 0;
            bool isConnected = _deviceManager.ConnectedDeviceCount > 0;
            
            StartButton.IsEnabled = hasData && isConnected && !_isRunning;
            StopButton.IsEnabled = _isRunning;
            ResetButton.IsEnabled = hasData && !_isRunning && (_currentIndex > 0 || _csvData.Any(d => d.IsSent));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_deviceManager.ConnectedDeviceCount > 0)
            {
                _deviceManager.DisconnectAllDevices();
            }
            
            _deviceManager.Dispose();
            base.OnClosing(e);
        }
    }

    // CSVデータのモデルクラス
    public class CsvRecord
    {
        [CsvHelper.Configuration.Attributes.Name("time")]
        public double Time { get; set; }
        
        [CsvHelper.Configuration.Attributes.Name("data")]
        public byte Data { get; set; }
    }

    // DataGrid用のデータアイテムクラス
    public class CsvDataItem : INotifyPropertyChanged
    {
        private bool _isSent;

        public double Time { get; set; }
        public byte Data { get; set; }
        public string HexData { get; set; } = "";
        
        public bool IsSent
        {
            get => _isSent;
            set
            {
                _isSent = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
