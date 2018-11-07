using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IoT.Lightning.Providers;
using Microsoft.IoT.Devices.Pwm;
using Microsoft.IoT.DeviceCore.Pwm;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Devices.SerialCommunication;
using Windows.Devices.I2c;
using Windows.Devices.Pwm;
using Windows.Devices.Gpio;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace WindowsIoT
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public sealed partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        
        private static DateTime ntpTime = new DateTime(1900, 1, 1);
        private static TimeZoneInfo timeZoneInfo = null;
        private static Stopwatch stopwatch = new Stopwatch();
        private Timer syncTimer = null;
        private Timer dispatcher = null;
        private ManualResetEvent timerMRE = new ManualResetEvent(false);
        private float _msenRelA, _msenRelB;

        private SolarTimeNOAA SolarTime = null;

        //private Semaphore dispatcherS = null;
        private RS485Dispatcher s485Dispatcher = null;
        //All external RS485 devices are here
        static public SerialComm[] serialComm = null;
        //Brightness control maintained here
        private BrightnessControl brightness = null;

        struct TimeCorrection
        {
            public bool saveRequest;
            public ulong tick;
            public DateTime time;
            public int ppm;
        }
        private TimeCorrection _tcA = new TimeCorrection() { saveRequest = false, tick = 0, ppm = 0 },
            _tcB = new TimeCorrection() { saveRequest = false, tick = 0, ppm = 0 };

        public App()
        {
            InitializeComponent();
            
            serialComm = new SerialComm[] {
                new AirCondConfig(0x12, 0x13), new AirCondState(0x11, 0),
                new ControllerConfig(0x22, 0x23), new ControllerState(0x21, 0),
                new ControllerChOnTime(0x24, 0), new ControllerOTSet(0, 0x23),
                new ControllerConfig(0x32, 0x33), new ControllerState(0x31, 0),
                new ControllerChOnTime(0x34, 0), new ControllerOTSet(0, 0x33) };
            serialComm[3].DataReady += Controller1State;
            serialComm[7].DataReady += Controller2State;
            serialComm[2].DataReady += Controller1Config;
            serialComm[6].DataReady += Controller2Config;
            s485Dispatcher = RS485Dispatcher.GetInstance();
            s485Dispatcher.Ready += UART_Configured;
            //syncTimer = new Timer(SyncTimerCallback, timerMRE, TimeSpan.FromSeconds(10), TimeSpan.FromDays(2));
            timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
            SolarTime = SolarTimeNOAA.GetInstance();
            SolarTime.Configure(timeZoneInfo, 47.215, 38.925);
            brightness = BrightnessControl.GetInstance();

            if (LightningProvider.IsLightningEnabled)
            {
                Windows.Devices.LowLevelDevicesController.DefaultProvider = 
                    LightningProvider.GetAggregateProvider();
            }
            Suspending += OnSuspending;
        }

        private void Dispatcher_Tick(object sender)
        {
            s485Dispatcher.EnqueueItem(serialComm[3]);
            s485Dispatcher.EnqueueItem(serialComm[7]);
            s485Dispatcher.EnqueueItem(serialComm[2]);
            s485Dispatcher.EnqueueItem(serialComm[6]);
            brightness.ReadLux();
        }

        uint SwapEndianness(ulong x)
        {
            return (uint)(((x & 0x000000ff) << 24) |
                           ((x & 0x0000ff00) << 8) |
                           ((x & 0x00ff0000) >> 8) |
                           ((x & 0xff000000) >> 24));
        }
        private void UART_Configured()
        {
            dispatcher = new Timer(Dispatcher_Tick, null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));
            s485Dispatcher.Start();
        }
        //Network time protocol handling
        private void Socket_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            using (DataReader reader = args.GetDataReader())
            {
                byte[] b = new byte[48];
                reader.ReadBytes(b);
                //Offset to get to the "Transmit Timestamp" field (time at which the reply 
                //departed the server for the client, in 64-bit timestamp format."
                const byte serverReplyTime = 40;
                //Get the seconds part
                ulong intPart = SwapEndianness(BitConverter.ToUInt32(b, serverReplyTime));
                //Get the seconds fraction
                ulong fractPart = SwapEndianness(BitConverter.ToUInt32(b, serverReplyTime + 4));
                var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);
                //**UTC** time
                ntpTime = (new DateTime(1900, 1, 1)).AddMilliseconds(milliseconds);
                stopwatch.Restart();
                SolarTime.CurrentDate = GetDateTime();
                timerMRE.Set();
                _tcA.saveRequest = _tcB.saveRequest = true;
            }
        }
        private async void SetTimeFromNetwork(string hostName)
        {
            DatagramSocket socket = new DatagramSocket();
            socket.MessageReceived += Socket_MessageReceived;
            try
            {
                await socket.ConnectAsync(new Windows.Networking.HostName(hostName), "123");
            }
            catch (Exception exc)
            {
                await new Windows.UI.Popups.MessageDialog(exc.Message, exc.Source).ShowAsync();
            }
            using (DataWriter writer = new DataWriter(socket.OutputStream))
            {
                byte[] container = new byte[48]; //NTP message size - 16 bytes of the digest (RFC 2030)
                //Setting the Leap Indicator, Version Number and Mode values
                container[0] = 0x1B; //LI = 0 (no warning), VN = 3 (IPv4 only), Mode = 3 (Client Mode)
                writer.WriteBytes(container);
                await writer.StoreAsync();
            }
        }
        private void SyncTimerCallback(object sender)
        {
            if ((sender as ManualResetEvent).WaitOne(13)) //Message was received from NTP
            {
                syncTimer.Change(TimeSpan.FromDays(3), TimeSpan.FromDays(3));
                (sender as ManualResetEvent).Reset();
            }
            else
            {
                syncTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
                SetTimeFromNetwork("time.windows.com");
            }
        }
        public static DateTime GetDateTime()
        {
            return TimeZoneInfo.ConvertTime(new DateTime(ntpTime.Ticks + stopwatch.Elapsed.Ticks),
                TimeZoneInfo.Utc, timeZoneInfo);
        }
        public static TimeSpan SyncTimeSpan()
        {
            return stopwatch.IsRunning ? stopwatch.Elapsed : TimeSpan.FromDays(365);
        }
        private void Controller1State(SerialComm sender)
        {
            ControllerState state = sender as ControllerState;
            _msenRelA = 0; //Motion sensor night level
            for (byte j = 5; j <= 7; j++) //Kitchen
                if (state.GetLinkLevel(j) > _msenRelA)
                    _msenRelA = state.GetLinkLevel(j);
            if (_tcA.saveRequest) //Correction for RTC in ATxmega
            {
                _tcA.saveRequest = false;
                if (_tcA.tick > 0)
                {
                    double actual = (state.Tick - _tcA.tick) / 32.0;
                    double reference = GetDateTime().Subtract(_tcA.time).TotalSeconds;
                    _tcA.ppm += (int)((actual - reference) * 1e+6 / reference);
                }
                _tcA.tick = state.Tick;
                _tcA.time = GetDateTime();
            }
        }
        private void Controller1Config(SerialComm sender)
        {
            ControllerConfig config = sender as ControllerConfig;
            config.RTCCorrect = _tcA.ppm;
        }
        private void Controller2State(SerialComm sender)
        {
            ControllerState state = sender as ControllerState;
            _msenRelB = 0; //Motion sensor night mode
            for (byte j = 2; j <= 7; j++) //Room B and C
                if (state.GetLinkLevel(j) > _msenRelB)
                    _msenRelB = state.GetLinkLevel(j);
            if (_tcB.saveRequest) //Correction for RTC in ATxmega
            {
                _tcB.saveRequest = false;
                if (_tcB.tick > 0)
                {
                    double actual = (state.Tick - _tcB.tick) / 32.0;
                    double reference = GetDateTime().Subtract(_tcB.time).TotalSeconds;
                    _tcB.ppm += (int)((actual - reference) * 1e+6 / reference);
                }
                _tcB.tick = state.Tick;
                _tcB.time = GetDateTime();
            }
        }
        private void Controller2Config(SerialComm sender)
        {
            ControllerConfig config = sender as ControllerConfig;
            config.RTCCorrect = _tcB.ppm;
            if (config.MSenAuto)
            {
                SolarTime.CurrentDate = ntpTime; //Corridor motion sensor
                config.MSenEnable = SolarTime.Sunrise > ntpTime || ntpTime > SolarTime.Sunset;
                float maxCurLvl = _msenRelA > _msenRelB ? _msenRelA : _msenRelB;
                float minChLvl = config.MinLvlGet(0) > config.MinLvlGet(1) ? config.MinLvlGet(0) : config.MinLvlGet(1);
                config.MsenOnLvl = (byte)((maxCurLvl > minChLvl ? maxCurLvl : minChLvl) * 255);
            }
        }
        
        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {

#if DEBUG
            if (Debugger.IsAttached)
            {
                DebugSettings.EnableFrameRateCounter = true;
            }
#endif

            Frame rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                // When the navigation stack isn't restored navigate to the first page,
                // configuring the new page by passing required information as a navigation
                // parameter
                rootFrame.Navigate(typeof(MainPage), e.Arguments);
            }
            // Ensure the current window is active
            Window.Current.Activate();
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Save application state and stop any background activity
            deferral.Complete();
        }
    }
}

public class BrightnessControl
{
    private static BrightnessControl _instance = null;
    private I2cDevice max44009 = null;
    private PwmController pwmController = null;
    private PwmPin pwmPin = null;
    //private IReadOnlyList<DeviceInformation> i2cDevices, pwmDevices;
    private float _minLevel, _maxLux, _currentLvl, _tempLvl, _delta;
    private byte[] command, result;
    private Timer _timer;
    public enum ControlMode
    { Auto, Fixed }

    private BrightnessControl()
    {
        _minLevel = .1f;
        _maxLux = 2000f;
        _currentLvl = .66f;
        _tempLvl = .67f;
        _delta = .01f;
        Mode = ControlMode.Auto;
        _timer = new Timer(TimerTick, null, TimeSpan.FromMilliseconds(-1), TimeSpan.FromSeconds(1 / 30f));
        command = new byte[1] { 0x03 };
        result = new byte[2];
        GetI2Clist();
    }
    /// <summary>
    /// Singleton patterb
    /// </summary>
    /// <returns>Instance of this class
    /// </returns>
    public static BrightnessControl GetInstance()
    {
        if (_instance == null)
            _instance = new BrightnessControl();
        return _instance;
    }
    private void TimerTick(object sender)
    {
        if (!HWStatus)
            return;
        if (Math.Abs(_currentLvl - _tempLvl) < _delta)
        {
            _currentLvl = _tempLvl;
            _timer.Change(TimeSpan.FromMilliseconds(-1), TimeSpan.FromSeconds(1 / 30f));
        }
        else
            _currentLvl -= _delta * Math.Sign(_currentLvl - _tempLvl);
        pwmPin?.SetActiveDutyCyclePercentage(_currentLvl);
    }
    public ControlMode Mode { get; set; }
    /// <summary>
    /// Minimum brightness level in auto mode. Acceptable range [.05;.95]
    /// </summary>
    public float MinLevel
    {
        get => _minLevel;
        set
        {
            if (value >= .05f && .95f >= value)
                _minLevel = value;
        }
    }
    /// <summary>
    /// Current brightness level, can be set in fixed mode. Acceptable range [.05;1]
    /// </summary>
    public float Level
    {
        get => _currentLvl;
        set
        {
            if (Mode == ControlMode.Fixed && HWStatus && value >= .05f && value <= 1f)
            {
                _currentLvl = value;
                pwmPin?.SetActiveDutyCyclePercentage(value);
            }
        }
    }
    /// <summary>
    /// Lux level at which maximum brightess is achieved in auto mode. Must be greater than 50
    /// </summary>
    public float MaxLux
    {
        get => _maxLux;
        set
        {
            _maxLux = (value < 50 ? 50 : value);
        }
    }
    public string ConfigTrace { get; private set; }
    private async void GetI2Clist()
    {
        ConfigTrace = "I2C devices: ";
        if (LightningProvider.IsLightningEnabled)
        {
            var i2c = await I2cController.GetControllersAsync(LightningI2cProvider.GetI2cProvider());
            max44009 = i2c[0].GetDevice(new I2cConnectionSettings(0b1001010));
            ConfigTrace += max44009 == null ? "none" : max44009.DeviceId + Environment.NewLine;
            PwmProviderManager pwmProvider = new PwmProviderManager();
            var pca = new PCA9685() { Address = 0x40 };
            pwmProvider.Providers.Add(pca);
            var pwm = await pwmProvider.GetControllersAsync();
            ConfigTrace += "PWM controllers: " + pwm.Count + Environment.NewLine;
            pwmController = pwm[0];
            foreach (var dev in pwm)
                ConfigTrace += "Pins:" + dev.PinCount + Environment.NewLine;
            pwmPin = pwmController.OpenPin(1);
            pwmController.SetDesiredFrequency(144);
            pwmPin.SetActiveDutyCyclePercentage(.1);
            pwmPin.Start();
            ConfigTrace += pwmPin.IsStarted ? string.Format("PWM running at {0:F1}Hz with duty cycle {1:P0}\n",
                pwmPin.Controller.ActualFrequency, pwmPin.GetActiveDutyCyclePercentage()) : "Failed to start\n";
        }
        else
            ConfigTrace += "lightning not configured" + Environment.NewLine;
        //else
        //{
        //    i2cDevices = await DeviceInformation.FindAllAsync(I2cDevice.GetDeviceSelector());
        //    ConfigTrace += i2cDevices.Count.ToString() + Environment.NewLine;
        //    pwmDevices = await DeviceInformation.FindAllAsync(PwmController.GetDeviceSelector());
        //    ConfigTrace += "PWM devices: " + pwmDevices.Count.ToString() + Environment.NewLine;
        //    var i2cSettings = new I2cConnectionSettings(0b1001010); //A0 = 0
        //    try
        //    {
        //        foreach (var dev in i2cDevices)
        //        {
        //            ConfigTrace += "MAX44009 ID is ";
        //            max44009 = await I2cDevice.FromIdAsync(dev.Id, i2cSettings);
        //            ConfigTrace += max44009.DeviceId + Environment.NewLine;
        //        }
        //        foreach (var dev in pwmDevices)
        //        {
        //            pwmController = await PwmController.FromIdAsync(dev.Id);
        //            ConfigTrace += "Default PWM: pin count " + pwmController.PinCount +
        //                ", min frequency " + pwmController.MinFrequency + Environment.NewLine;
        //            pwmController.SetDesiredFrequency(400);
        //            pwmPin = pwmController.OpenPin(18);
        //            pwmPin.Start();
        //            ConfigTrace += "PWM pin 18 started: " + pwmPin.IsStarted.ToString();
        //        }
        //    }
        //    catch (Exception exc)
        //    {
        //        Debug.WriteLine(exc.Message);
        //    }
        //}

    }
    /// <summary>
    /// Retrieves current lux value from MAX44009. Performs brightness correction (in auto mode)
    /// </summary>
    public void ReadLux()
    {
        if (!HWStatus)
            return;
        try
        {
            max44009.WriteRead(command, result);
            ushort lux = BitConverter.ToUInt16(result, 0);
            Lux = (((lux >> 4) & 0xFF) << (lux >> 12)) * .045f;
            if (Mode == ControlMode.Auto)
            {
                _tempLvl = _minLevel + Lux > 1 ? 
                    (float)(Math.Log(MaxLux) * Math.Log(Lux)) / (1 - _minLevel) : 0;
                if (_tempLvl > 1)
                    _tempLvl = 1;
                if (_tempLvl != _currentLvl)
                    _timer.Change(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1 / 30f));
            }
        }
        catch (Exception exc)
        {
            Debug.WriteLine(exc.Message);
        }
    }
    /// <summary>
    /// Current lux level (read only)
    /// </summary>
    public float Lux
    { get; private set; }
    /// <summary>
    /// Returns true if both MAX44009 and PWM are configured
    /// </summary>
    public bool HWStatus
    {
        get => (max44009 != null && pwmPin != null);
    }
}

public class RS485Dispatcher
{
    private static RS485Dispatcher _instance = null;
    private SerialDevice serialPort;
    private DataWriter dataWriteObject;
    private DataReader dataReaderObject;
    private AutoResetEvent _ARE;
    private Queue<SerialComm> serialComm;
    private Stopwatch stopwatch;
    private IAsyncAction asyncAction;
    private long _ticksElapsed;
    private int _timeout, _fails;
    private bool _isRunning;
    private object lockObj;

    struct Stats
    {
        public int packets, badCRC, rxLost, txLost;
        public static Stats operator + (Stats s1, Stats s2)
        {
            Stats stats;
            stats.badCRC = s1.badCRC + s2.badCRC;
            stats.packets = s1.packets + s2.packets;
            stats.rxLost = s1.rxLost + s2.rxLost;
            stats.txLost = s1.txLost + s2.txLost;
            return stats;
        }
        public static Stats operator - (Stats s1, Stats s2)
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
    private Queue<Stats> _queue;
    public delegate void config();
    private event config _ready;
    public event config Ready
    {
        add
        {
            if (_ready == null)
                UARTconnect();
            _ready += value;
        }
        remove
        {
            _ready -= value;
            if (_ready == null)
                asyncAction?.Cancel();
        }
    }

    private RS485Dispatcher()
    {
        serialComm = new Queue<SerialComm>(10);
        _ARE = new AutoResetEvent(false);
        stopwatch = new Stopwatch();
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
            var dis = await DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector("UART0"));
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
            dataReaderObject = new DataReader(serialPort.InputStream);
            _ready?.Invoke();
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
        stopwatch.Restart();
        asyncAction = Windows.System.Threading.ThreadPool.RunAsync(BusDispatcher, WorkItemPriority.High, WorkItemOptions.TimeSliced);
    }
    /// <summary>
    /// Current bus loading in bytes per second
    /// </summary>
    public long BusSpeed
    {
        get
        {
            long x = Interlocked.Exchange(ref _ticksElapsed, 0) * 1000 / stopwatch.ElapsedMilliseconds;
            stopwatch.Restart();
            return x;
        }
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
            string result = _stats.packets > 0 ? 
                string.Format("{0} fails, {1:P0} bad CRC, {2:P0} TX lost, {3:P0} RX lost", _fails,
                _stats.badCRC / (float)_stats.packets, _stats.txLost / (float)_stats.packets,
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
                bt += await serialPort.OutputStream.WriteAsync(dataWriteObject.DetachBuffer());
                dataWriteObject.WriteBytes(arr);
                await Task.Delay(TimeSpan.FromSeconds(1.0 / 6400));
                //Launch an async task to complete the write operation
                bt += await serialPort.OutputStream.WriteAsync(dataWriteObject.DetachBuffer());
                await Task.Delay(TimeSpan.FromMilliseconds(3));
            }
            crc = BitConverter.ToUInt16(arr, arr.Length - 2);
            if (device.RxAddress != 0)
            {
                //Read data
                dataWriteObject.WriteByte(device.RxAddress);
                await Task.Delay(TimeSpan.FromSeconds(1.0 / 6400));
                bt += await serialPort.OutputStream.WriteAsync(dataWriteObject.DetachBuffer());
                try
                {
                    var timeoutSource = new CancellationTokenSource(14);
                    buffer = new Windows.Storage.Streams.Buffer((uint)arr.Length);
                    arr = (await serialPort.InputStream.ReadAsync(buffer, buffer.Capacity, 
                        InputStreamOptions.None).AsTask(timeoutSource.Token)).ToArray();
                    bt += (uint)arr.Length;
                }
                catch (TaskCanceledException)
                {
                    device.IsBufferValid = false;
                    arr = null;
                }
            }
            if (device.TxAddress != 0 && device.RxAddress != 0 && device.IsBufferValid &&
                arr.Length > 1 && crc != BitConverter.ToUInt16(arr, arr.Length - 2))
            {
                lock (lockObj)
                    serialComm.Enqueue(device); //Repeat data transmission
                Interlocked.Increment(ref _stats.rxLost);
            }
            else
            {
                byte result = await device.SetBuffer(arr); //CRC check will be performed inside class
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
}
public class SolarTimeNOAA
{
    private TimeZoneInfo _timeZone;
    private double _latitude, _longitude;
    private static SolarTimeNOAA _instance = null;
    /// <summary>
    /// Configuration
    /// </summary>
    /// <param name="tz">Timezone information</param>
    /// <param name="latitude">Geographical latitude</param>
    /// <param name="longitude">Geographical longitude</param>
    public void Configure(TimeZoneInfo tz, double latitude, double longitude)
    {
        _timeZone = tz;
        _latitude = latitude * Math.PI / 180;
        _longitude = longitude;
    }
    /// <summary>
    /// Singleton pattern
    /// </summary>
    /// <returns>Instance of this class</returns>
    public static SolarTimeNOAA GetInstance()
    {
        if (_instance == null)
            _instance = new SolarTimeNOAA();
        return _instance;
    }
    private SolarTimeNOAA()
    {
        Sunrise = new DateTime(2018, 5, 22);
        Sunset = new DateTime(2018, 5, 30);
    }
    /// <summary>
    /// Calculates sunrise and sunset times from given date
    /// </summary>
    public DateTime CurrentDate
    {
        set
        {
            double jc = (value.Date.Ticks / TimeSpan.TicksPerDay - 7.301185e+5 -
                _timeZone.GetUtcOffset(value).Hours / 24.0) / 3.6525e+4;
            double gmls = 4.895063 + jc * (628.331967 + jc * 5.29184e-6);
            double gmas = 6.24006 + jc * (628.301955 - jc * 2.68257e-6);
            double eeo = 1.6708634e-2 - jc * (4.2037e-5 + 1.267e-7 * jc);
            double seoc = Math.Sin(gmas) * (3.341611e-2 - jc * (8.40725e-5 + 2.443461e-7 * jc)) +
                Math.Sin(2 * gmas) * (3.489437e-4 + 1.762783e-6 * jc) + Math.Sin(3 * gmas) * 5.044e-6;
            double oc = 4.090928e-1 - jc * (2.269655e-4 + jc * (2.8604e-9 - jc * 8.789672e-9)) +
                4.468043e-5 * Math.Cos(2.18236 - 33.757041 * jc);
            double sd = Math.Asin(Math.Sin(oc) *
                Math.Sin(gmls + seoc - 9.930923e-5 - 8.342674e-5 * Math.Sin(2.18236 - 33.757041 * jc)));
            double y = Math.Pow(Math.Tan(oc / 2), 2);
            double has = Math.Acos(-1.453808e-2 /
                (Math.Cos(_latitude) * Math.Cos(sd)) - Math.Tan(_latitude) * Math.Tan(sd));
            double sn = .5 - _longitude / 360 - (229.183118 * (y * Math.Sin(2 * gmls) -
                2 * eeo * Math.Sin(gmas) + 4 * eeo * y * Math.Sin(gmas) * Math.Cos(2 * gmls) -
                .5 * y * y * Math.Sin(4 * gmls) - 1.25 * eeo * eeo * Math.Sin(2 * gmas))) / 1440 +
                _timeZone.GetUtcOffset(value).Hours / 24.0;
            Sunrise = value.Date.AddDays(sn - has * 1.591549e-1);
            Sunset = value.Date.AddDays(sn + has * 1.591549e-1);
        }
    }
    public DateTime Sunrise
    {
        get; private set;
    }
    public DateTime Sunset
    {
        get; private set;
    }
}
#region SerialDevices
public abstract class SerialComm : DependencyObject
{
    protected byte[] buffer;
    private bool _bv;
    protected object lockObj;
    public delegate void DRDY(SerialComm sender);
    public event DRDY DataReady;
    /// <summary>
    /// Address of device's buffer for reading
    /// </summary>
    public byte RxAddress
    {
        get; protected set;
    }
    /// <summary>
    /// Address of device's buffer for writing
    /// </summary>
    public byte TxAddress
    {
        get; protected set;
    }
    /// <summary>
    /// This is the CRC16-CCITT. HEX: 0x1021; initial value: 0xFFFF
    /// </summary>
    /// <param name="arr">Byte array to calculate CRC from</param>
    /// <returns>CRC16 of array (except two last bytes)</returns>
    private ushort CRC16(byte[] arr)
    {
        int crc = 0xFFFF;
        for (int o = 0; o < arr.Length - 2; o++)
        {
            crc = crc ^ (arr[o] << 8);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (crc << 1) ^ 0x1021;
                else
                    crc <<= 1;
            }
        }
        return (ushort)crc;
    }
    /// <summary>
    /// Indicates that buffer contains actual data from device
    /// </summary>
    public bool IsBufferValid
    {
        get => _bv;
        set
        {
            if (!value)
                lock (lockObj)
                    _bv = value;
        }
    }
    /// <summary>
    /// Provides a copy of all internal fields combined in one array
    /// </summary>
    /// <returns>Byte array with generated CRC16</returns>
    public byte[] GetBuffer()
    {
        byte[] result = new byte[buffer.Length];
        lock (lockObj)
        {
            Array.Copy(buffer, result, buffer.Length - 2);
            BitConverter.GetBytes(CRC16(result)).CopyTo(result, result.Length - 2);
        }
        return result;
    }
    /// <summary>
    /// Sets internal fields from byte array
    /// </summary>
    /// <param name="value">Byte array from device</param>
    /// <returns>Data integrity checking result (0 - OK, 1 - bad CRC, 2 - bad size)</returns>
    public async Task<byte> SetBuffer(byte[] value)
    {
        byte result = 0;
        lock (lockObj)
        {
            _bv = false;
            if (value != null && value.Length == buffer.Length)
            {
                if (CRC16(value) == BitConverter.ToUInt16(value, value.Length - 2))
                {
                    if (!_bv || TxAddress == 0)
                        Array.Copy(value, buffer, buffer.Length);
                    _bv = true;
                }
                else
                    result = 1;
            }
            else
                result = 2;
        }
        if (result == 0 && DataReady != null)
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () => DataReady?.Invoke(this));
        return result;
    }
    public override bool Equals(object obj)
    {
        if (obj is SerialComm)
            return ((obj as SerialComm).RxAddress == RxAddress) & ((obj as SerialComm).TxAddress == TxAddress);
        return false;
    }
    public override int GetHashCode()
    {
        return base.GetHashCode();
    }
}
//Controller
public class ControllerConfig : SerialComm //24+2 bytes, get: 0x22, 0x32; set: 0x23, 0x33
{
    private byte _mSenOt;
    public ControllerConfig(byte rxAddr, byte txAddr)
    {
        buffer = new byte[26];
        lockObj = new object();
        RxAddress = rxAddr;
        TxAddress = txAddr;
        MSenAuto = true;
    }
    /// <summary>
    /// Get minimum level for brightness regulation
    /// </summary>
    /// <param name="channel">Channel number, range [0;7]</param>
    /// <returns>Brightness level, range [0;1]</returns>
    public float MinLvlGet(byte channel)
    {
        if (channel > 7)
            return 0;
        return buffer[channel] / 255f;
    }
    /// <summary>
    /// Set minimum level for brightness regulation
    /// </summary>
    /// <param name="channel">Channel number, range [0;7]</param>
    /// <param name="value">Brightness level, range [0;255]</param>
    public void MinLvlSet(byte channel, byte value)
    {
        if (channel > 7)
            return;
        lock (lockObj)
            buffer[channel] = value;
    }
    /// <summary>
    /// Set brightness level directly
    /// </summary>
    /// <param name="channels">Array of channel numbers in range [0;8]</param>
    /// <param name="val">Brightness level, range [0;255]</param>
    public void OverrideLvlSet(byte[] channels, byte val)
    {
        lock (lockObj)
        {
            buffer[8] = val;
            buffer[9] = 0; //Override mask
            buffer[22] &= 0xef; //Clear 4th bit in config
            if (channels != null)
            {
                foreach (var x in channels)
                    if (x < 8)
                        buffer[9] |= (byte)(1 << x);
                    else if (x == 8)
                        buffer[22] |= 1 << 4;
            }
        }
    }
    /// <summary>
    /// Get fade rate of link
    /// </summary>
    /// <param name="link">Link number, range [0;3]</param>
    /// <returns>Fade rate</returns>
    public float FadeRateGet(byte link)
    {
        if (link > 3)
            return 0;
        return (3 * buffer[10 + link]) / 8f;
    }
    public void FadeRateSet(byte link, byte value)
    {
        if (link > 3)
            return;
        lock (lockObj)
            buffer[10 + link] = value;
    }
    public float LinkDelayGet(byte link)
    {
        if (link > 3)
            return 0;
        return buffer[14 + link] / 32f;
    }
    public void LinkDelaySet(byte link, byte value)
    {
        if (link > 3)
            return;
        lock (lockObj)
            buffer[14 + link] = value;
    }
    public bool MSenAuto { get; set; }
    public bool MSenEnable
    {
        set
        {
            lock (lockObj)
            {
                if (value)
                    buffer[18] = _mSenOt;
                else
                    buffer[18] = 0;
            }
        }
        get => buffer[18] == _mSenOt;
    }
    public byte MSenOnTime
    {
        get => _mSenOt;
        set
        {
            lock (lockObj)
            {
                if (buffer[18].Equals(_mSenOt))
                    buffer[18] = value;
                _mSenOt = value;
            }
        }
    }
    public byte MsenOnLvl
    {
        get => buffer[19];
        set
        {
            lock (lockObj)
                buffer[19] = value;
        }
    }
    public byte MSenLowTime
    {
        get => buffer[20];
        set
        {
            lock (lockObj)
                buffer[20] = value;
        }
    }
    public byte MSenLowLvl
    {
        get => buffer[21];
        set
        {
            lock (lockObj)
                buffer[21] = value;
        }
    }
    public void SetConfig(bool saveToNvm)
    {
        lock (lockObj)
            buffer[22] = (byte)(saveToNvm ? (1 << 3) : 0);
    }
    public int RTCCorrect //Value in ppm, sign-and-magnitude representation
    {
        get => buffer[23] > 0x7f ? -(buffer[23] & 0x7f) : buffer[23];
        set
        {
            if (value < -127)
                value = -127;
            else if (value > 127)
                value = 127;
            lock (lockObj)
                buffer[23] = value < 0 ? (byte)((-value) | 0x80) : (byte)value;
        }
    }
}
public class ControllerOTSet : SerialComm //24+2 bytes, set: 0x23, 0x33
{
    public ControllerOTSet(byte rxAddr, byte txAddr)
    {
        buffer = new byte[26];
        lockObj = new object();
        RxAddress = rxAddr;
        TxAddress = txAddr;
    }
    public void ReplaceChOnTime(byte channel, uint onTime, ushort swCnt)
    {
        if (channel > 8)
            return;
        lock (lockObj)
        {
            BitConverter.GetBytes(onTime).CopyTo(buffer, 0);
            BitConverter.GetBytes(swCnt).CopyTo(buffer, 10);
            buffer[9] = (byte)(channel | 0x80);
            buffer[22] = 0x80;
        }
    }
}
public class ControllerState : SerialComm //23+2 bytes, get: 0x21, 0x31
{
    public ControllerState(byte rxAddr, byte txAddr)
    {
        buffer = new byte[25];
        lockObj = new object();
        RxAddress = rxAddr;
        TxAddress = txAddr;
    }
    public ulong Tick
    {
        get => BitConverter.ToUInt64(buffer, 0);
    }
    public float GetChLevel(byte channel)
    {
        if (channel > 8)
            return 0;
        return buffer[8 + channel] / 255f;
    }
    public float GetLinkLevel(byte link)
    {
        if (link > 3)
            return 0;
        return buffer[17 + link] / 255f;
    }
    public bool IsLinkValid(byte link)
    {
        if (link > 3)
            return false;
        return (buffer[21] & (1 << link)) != 0;
    }
    public string MSenState
    {
        get
        {
            if (buffer[22] < 20)
                return "N/C";
            if (buffer[22] < 75)
                return "Off";
            if (buffer[22] < 100)
                return "A";
            if (buffer[22] < 150)
                return "B";
            return "A+B";
        }
    }
}
public class ControllerChOnTime : SerialComm //54+2 bytes, get: 0x24, 0x34
{
    public ControllerChOnTime(byte rxAddr, byte txAddr)
    {
        buffer = new byte[56];
        lockObj = new object();
        RxAddress = rxAddr;
        TxAddress = txAddr;
    }
    public uint GetOnTime(byte channel)
    {
        if (channel > 8)
            return 0;
        return BitConverter.ToUInt32(buffer, channel * 4);
    }
    public ushort GetSwCount(byte channel)
    {
        if (channel > 8)
            return 0;
        return BitConverter.ToUInt16(buffer, 36 + channel * 2);
    }
}
//Air conditioning
public class AirCondConfig : SerialComm //9+2 bytes, get: 0x12, set: 0x13
{
    public AirCondConfig(byte rxAddr, byte txAddr)
    {
        buffer = new byte[11];
        lockObj = new object();
        RxAddress = rxAddr;
        TxAddress = txAddr;
    }
    public float FanLevel
    {
        get => buffer[0] / 147f;
        set
        {
            lock (lockObj)
            {
                if (value < 0)
                    buffer[0] = 0;
                else if (value > .99f)
                    buffer[0] = 0xff;
                else
                    buffer[0] = (byte)(value * 147f);
            }
        }
    }
    public float MinDeltaRH
    {
        get => BitConverter.ToInt16(buffer, 1) / 10f;
        set
        {
            lock (lockObj)
                BitConverter.GetBytes((short)(value * 10)).CopyTo(buffer, 1);
        }
    }
    public float MaxDeltaRH
    {
        get => (BitConverter.ToInt16(buffer, 3) + BitConverter.ToInt16(buffer, 1)) / 10f;
        set
        {
            lock (lockObj)
                BitConverter.GetBytes((short)(value * 10 - BitConverter.ToInt16(buffer, 1))).CopyTo(buffer, 3);
        }
    }
    public float MinDeltaT
    {
        get => BitConverter.ToInt16(buffer, 5) / 10f;
        set
        {
            lock (lockObj)
                BitConverter.GetBytes((short)(value * 10)).CopyTo(buffer, 5);
        }
    }
    public float MaxDeltaT
    {
        get => (BitConverter.ToInt16(buffer, 7) + BitConverter.ToInt16(buffer, 5)) / 10f;
        set
        {
            lock (lockObj)
                BitConverter.GetBytes((short)(value * 10 - BitConverter.ToInt16(buffer, 5))).CopyTo(buffer, 7);
        }
    }
}
public class AirCondState : SerialComm //15+2 bytes, get: 0x11
{
    public AirCondState(byte rxAddr, byte txAddr)
    {
        buffer = new byte[17];
        lockObj = new object();
        RxAddress = rxAddr;
        TxAddress = txAddr;
    }
    public float FanLevel
    {
        get => buffer[0] / 147f;
    }
    public ushort RPMFront
    {
        get => BitConverter.ToUInt16(buffer, 1);
    }
    public ushort RPMRear
    {
        get => BitConverter.ToUInt16(buffer, 3);
    }
    public float CurrentDraw
    {
        get => BitConverter.ToUInt16(buffer, 5) / 1000f;
    }
    public float InsideRH
    {
        get => BitConverter.ToInt16(buffer, 7) / 1000f;
    }
    public float OutsideRH
    {
        get => BitConverter.ToInt16(buffer, 9) / 1000f;
    }
    public float InsideT
    {
        get => BitConverter.ToInt16(buffer, 11) / 10f;
    }
    public float OutsideT
    {
        get => BitConverter.ToInt16(buffer, 13) / 10f;
    }
}
#endregion SerialDevices