using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System.Threading;

namespace WindowsIoT.Communication
{
    public sealed class RS485Dispatcher : IDisposable
    {
        private static RS485Dispatcher _instance = null;
        private SerialDevice serialPort;
        private DataWriter dataWriteObject;
        private readonly AutoResetEvent _ARE;
        private readonly Queue<SerialComm> serialComm;
        private IAsyncAction asyncAction;
        private long _ticksElapsed;
        private int _timeout, _fails;
        private bool _isRunning;
        private readonly object lockObj;

        struct Stats
        {
            public int packets, badCRC, rxLost, txLost;
            public static Stats operator +(Stats s1, Stats s2)
            {
                Stats stats;
                stats.badCRC = s1.badCRC + s2.badCRC;
                stats.packets = s1.packets + s2.packets;
                stats.rxLost = s1.rxLost + s2.rxLost;
                stats.txLost = s1.txLost + s2.txLost;
                return stats;
            }
            public static Stats operator -(Stats s1, Stats s2)
            {
                Stats stats;
                stats.badCRC = s1.badCRC - s2.badCRC;
                stats.packets = s1.packets - s2.packets;
                stats.rxLost = s1.rxLost - s2.rxLost;
                stats.txLost = s1.txLost - s2.txLost;
                return stats;
            }
        }
        private Stats _stats;
        private readonly Queue<Stats> _queue;
        public delegate void config();
        private event config BusReady;
        public event config Ready
        {
            add
            {
                if (BusReady == null)
                {
                    BusReady += value;
                    UARTconnect();
                }
                else
                    BusReady += value;
            }
            remove
            {
                BusReady -= value;
                if (BusReady == null)
                    asyncAction?.Cancel();
            }
        }

        private RS485Dispatcher()
        {
            serialComm = new Queue<SerialComm>(10);
            _ARE = new AutoResetEvent(false);
            lockObj = new object();
            _ticksElapsed = 0;
            _timeout = _fails = 0;
            _queue = new Queue<Stats>(8);
            _isRunning = false;
        }
        /// <summary>
        /// Singleton pattern
        /// </summary>
        /// <returns>Instance of RS485 dispatcher</returns>
        public static RS485Dispatcher GetInstance()
        {
            if (_instance == null)
                _instance = new RS485Dispatcher();
            return _instance;
        }
        private async void UARTconnect()
        {
            try
            {
                var dis = await DeviceInformation.FindAllAsync(
                    SerialDevice.GetDeviceSelector("UART0"));
                serialPort = await SerialDevice.FromIdAsync(dis[0].Id);
                if (serialPort == null)
                    throw new IOException();
                // Configure serial settings
                serialPort.WriteTimeout = TimeSpan.FromMilliseconds(13);
                serialPort.ReadTimeout = TimeSpan.FromMilliseconds(13);
                serialPort.BaudRate = 76800;
                serialPort.Parity = SerialParity.None;
                serialPort.StopBits = SerialStopBitCount.One;
                serialPort.DataBits = 8;
                serialPort.Handshake = SerialHandshake.None;

                // Create the DataWriter object and attach to OutputStream
                dataWriteObject = new DataWriter(serialPort.OutputStream);
                BusReady?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
        /// <summary>
        /// Starts dispatcher, if it's not running and serial port is configured
        /// </summary>
        public void Start()
        {
            if (serialPort == null || _isRunning)
                return;
            _stats = new Stats() { packets = 0, badCRC = 0, rxLost = 0, txLost = 0 };
            asyncAction = Windows.System.Threading.ThreadPool.RunAsync(
                BusDispatcher, WorkItemPriority.High, WorkItemOptions.TimeSliced);
        }
        /// <summary>
        /// Push an item into a dispatcher queue
        /// </summary>
        /// <param name="item">One of SerialComm instances</param>
        public void EnqueueItem(SerialComm item)
        {
            lock (lockObj)
            {
                if (!serialComm.Contains<SerialComm>(item))
                    serialComm.Enqueue(item);
                if (serialComm.Count > 0)
                    _ARE.Set();
            }
            if (_timeout++ > 9) //Restart dispatcher if he's hanging
            {
                _timeout = 0;
                _fails++;
                _stats = new Stats() { packets = 0, badCRC = 0, rxLost = 0, txLost = 0 };
                _queue.Clear();
                asyncAction?.Cancel();
                _isRunning = false;
                Start();
            }
        }
        /// <summary>
        /// Statistics averaged over last 8 requests
        /// </summary>
        public string Statistics
        {
            get
            {
                if (_queue.Count > 7)
                    _queue.Dequeue();
                _queue.Enqueue(_stats);
                _stats = new Stats() { packets = 0, badCRC = 0, rxLost = 0, txLost = 0 };
                foreach (var j in _queue)
                    _stats += j;
                string result = _stats.packets > 0 ? string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} fails, {1:P0} bad CRC, {2:P0} TX lost, {3:P0} RX lost", 
                    _fails,
                    _stats.badCRC / (float)_stats.packets, 
                    _stats.txLost / (float)_stats.packets,
                    _stats.rxLost / (float)_stats.packets) : "N/A";
                _stats = new Stats() { packets = 0, badCRC = 0, rxLost = 0, txLost = 0 };
                return result;
            }
        }
        private async void BusDispatcher(IAsyncAction action)
        {
            byte[] arr;
            ushort crc;
            uint bt;
            SerialComm device;
            IBuffer buffer;
            Stopwatch busyCounter = new Stopwatch();
            _isRunning = true;

            while (!action.Status.Equals(AsyncStatus.Canceled))
            {
                if (serialComm.Count == 0)
                    _ARE.WaitOne();
                device = null;
                lock (lockObj)
                {
                    _timeout = 0;
                    if (serialComm.Count > 0)
                        device = serialComm.Dequeue();
                }
                if (device == null) continue;
                busyCounter.Restart();
                arr = device.GetBuffer();
                bt = 0;
                if (device.TxAddress != 0 && device.IsBufferValid)
                {
                    //Configuration transfer request - data must be written first
                    dataWriteObject.WriteByte(device.TxAddress);
                    bt += await serialPort.OutputStream.WriteAsync(
                        dataWriteObject.DetachBuffer());
                    dataWriteObject.WriteBytes(arr);
                    await Task.Delay(TimeSpan.FromSeconds(1.0 / 6400)).ConfigureAwait(true);
                    //Launch an async task to complete the write operation
                    bt += await serialPort.OutputStream.WriteAsync(
                        dataWriteObject.DetachBuffer());
                    await Task.Delay(TimeSpan.FromMilliseconds(3)).ConfigureAwait(true);
                }
                crc = BitConverter.ToUInt16(arr, arr.Length - 2);
                if (device.RxAddress != 0)
                {
                    //Read data
                    dataWriteObject.WriteByte(device.RxAddress);
                    await Task.Delay(TimeSpan.FromSeconds(1.0 / 6400)).ConfigureAwait(true);
                    bt += await serialPort.OutputStream.WriteAsync(
                        dataWriteObject.DetachBuffer());
                    try
                    {
                        var timeoutSource = new CancellationTokenSource(14);
                        buffer = new Windows.Storage.Streams.Buffer((uint)arr.Length);
                        arr = (await serialPort.InputStream.ReadAsync(
                            buffer, buffer.Capacity, InputStreamOptions.None)
                            .AsTask(timeoutSource.Token).ConfigureAwait(true)).ToArray();
                        bt += (uint)arr.Length;
                        timeoutSource.Dispose();
                    }
                    catch (TaskCanceledException)
                    {
                        device.IsBufferValid = false;
                        arr = null;
                    }
                }
                if (device.TxAddress != 0 && device.RxAddress != 0 && 
                    device.IsBufferValid && arr.Length > 1 && 
                    crc != BitConverter.ToUInt16(arr, arr.Length - 2))
                {
                    lock (lockObj)
                        serialComm.Enqueue(device); //Repeat data transmission
                    Interlocked.Increment(ref _stats.rxLost);
                }
                else
                {
                    byte result = await device.SetBuffer(arr).ConfigureAwait(true);
                    if (result.Equals(1)) //CRC error
                    {
                        lock (lockObj)
                            serialComm.Enqueue(device); //Repeat data transmission 
                        Interlocked.Increment(ref _stats.badCRC);
                    }
                    else if (result.Equals(2))
                        Interlocked.Increment(ref _stats.txLost);
                }
                busyCounter.Stop();
                Interlocked.Increment(ref _stats.packets);
                Interlocked.Add(ref _ticksElapsed, bt);
            }
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                    dataWriteObject.Dispose();
                    _ARE.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
