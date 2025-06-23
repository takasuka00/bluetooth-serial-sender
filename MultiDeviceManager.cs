using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;

namespace BluetoothSerialSender
{
    /// <summary>
    /// 複数のシリアルポートデバイスを管理するクラス
    /// </summary>
    public class MultiDeviceManager : IDisposable
    {
        private readonly Dictionary<string, SerialPort> _connectedDevices = new Dictionary<string, SerialPort>();
        private readonly object _lockObject = new object();

        /// <summary>
        /// 接続されているデバイスのポート名一覧を取得
        /// </summary>
        public IReadOnlyList<string> ConnectedPorts 
        { 
            get 
            { 
                lock (_lockObject)
                {
                    return _connectedDevices.Keys.ToList(); 
                }
            }
        }

        /// <summary>
        /// 接続されているデバイス数を取得
        /// </summary>
        public int ConnectedDeviceCount 
        { 
            get 
            { 
                lock (_lockObject)
                {
                    return _connectedDevices.Count; 
                }
            }
        }

        /// <summary>
        /// デバイスに接続
        /// </summary>
        public void ConnectDevice(string portName, int baudRate = 9600)
        {
            lock (_lockObject)
            {
                if (_connectedDevices.ContainsKey(portName))
                {
                    throw new InvalidOperationException($"ポート {portName} は既に接続されています。");
                }

                var serialPort = new SerialPort();
                serialPort.PortName = portName;
                serialPort.BaudRate = baudRate;
                serialPort.DataBits = 8;
                serialPort.Parity = Parity.None;
                serialPort.StopBits = StopBits.One;
                serialPort.Handshake = Handshake.None;
                serialPort.ReadTimeout = 500;
                serialPort.WriteTimeout = 500;
                serialPort.ReadBufferSize = 4096;
                serialPort.WriteBufferSize = 2048;

                serialPort.Open();
                _connectedDevices[portName] = serialPort;
            }
        }

        /// <summary>
        /// 特定のデバイスを切断
        /// </summary>
        public void DisconnectDevice(string portName)
        {
            lock (_lockObject)
            {
                if (_connectedDevices.TryGetValue(portName, out var serialPort))
                {
                    if (serialPort.IsOpen)
                    {
                        serialPort.Close();
                    }
                    serialPort.Dispose();
                    _connectedDevices.Remove(portName);
                }
            }
        }

        /// <summary>
        /// すべてのデバイスを切断
        /// </summary>
        public void DisconnectAllDevices()
        {
            lock (_lockObject)
            {
                foreach (var device in _connectedDevices.Values)
                {
                    if (device.IsOpen)
                    {
                        device.Close();
                    }
                    device.Dispose();
                }
                _connectedDevices.Clear();
            }
        }

        /// <summary>
        /// すべての接続されたデバイスにデータを送信
        /// </summary>
        public void WriteToAllDevices(byte[] data)
        {
            lock (_lockObject)
            {
                var failedPorts = new List<string>();

                foreach (var kvp in _connectedDevices)
                {
                    try
                    {
                        if (kvp.Value.IsOpen)
                        {
                            kvp.Value.Write(data, 0, data.Length);
                        }
                    }
                    catch (Exception)
                    {
                        failedPorts.Add(kvp.Key);
                    }
                }

                // 失敗したポートを切断
                foreach (var portName in failedPorts)
                {
                    DisconnectDevice(portName);
                }
            }
        }

        /// <summary>
        /// 特定のデバイスにデータを送信
        /// </summary>
        public void WriteToDevice(string portName, byte[] data)
        {
            lock (_lockObject)
            {
                if (_connectedDevices.TryGetValue(portName, out var serialPort))
                {
                    if (serialPort.IsOpen)
                    {
                        serialPort.Write(data, 0, data.Length);
                    }
                }
            }
        }

        /// <summary>
        /// デバイスが接続されているか確認
        /// </summary>
        public bool IsDeviceConnected(string portName)
        {
            lock (_lockObject)
            {
                return _connectedDevices.ContainsKey(portName) && 
                       _connectedDevices[portName].IsOpen;
            }
        }

        public void Dispose()
        {
            DisconnectAllDevices();
        }
    }
}
