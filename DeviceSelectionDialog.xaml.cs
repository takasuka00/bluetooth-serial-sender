using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace BluetoothSerialSender
{
    public partial class DeviceSelectionDialog : Window
    {
        private ObservableCollection<PortItem> _portItems = new ObservableCollection<PortItem>();

        public string[] SelectedPorts 
        { 
            get 
            { 
                return _portItems.Where(p => p.IsSelected).Select(p => p.PortName).ToArray(); 
            } 
        }

        public DeviceSelectionDialog()
        {
            InitializeComponent();
            PortListBox.ItemsSource = _portItems;
            RefreshPorts();
        }

        private void RefreshPorts()
        {
            _portItems.Clear();
            string[] ports = SerialPort.GetPortNames();
            
            foreach (string port in ports.OrderBy(p => p))
            {
                _portItems.Add(new PortItem { PortName = port, IsSelected = false });
            }

            UpdateSelectionCount();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshPorts();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _portItems)
            {
                item.IsSelected = true;
            }
            UpdateSelectionCount();
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _portItems)
            {
                item.IsSelected = false;
            }
            UpdateSelectionCount();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPorts.Length == 0)
            {
                MessageBox.Show("少なくとも1つのポートを選択してください。", "警告", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UpdateSelectionCount()
        {
            int count = _portItems.Count(p => p.IsSelected);
            SelectionCountText.Text = $"{count}個選択";
        }

        // ポートアイテムクラス
        public class PortItem : INotifyPropertyChanged
        {
            private bool _isSelected;

            public string PortName { get; set; } = "";
            
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    // 親ウィンドウの選択数を更新
                    if (Application.Current.Windows.OfType<DeviceSelectionDialog>().FirstOrDefault() 
                        is DeviceSelectionDialog dialog)
                    {
                        dialog.UpdateSelectionCount();
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
