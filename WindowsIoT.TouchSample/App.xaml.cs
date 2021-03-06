﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Devices.I2c;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage;
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
using WindowsIoT.Communication;
using static WindowsIoT.Communication.Enums;
using Windows.ApplicationModel.Resources;

namespace WindowsIoT
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public sealed partial class App : Application, IDisposable
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        
        //private static DateTime ntpTime = new DateTime(1900, 1, 1);
        //private static TimeZoneInfo timeZoneInfo = null;
        //private static readonly Stopwatch stopwatch = new Stopwatch();
        //private static Timer syncTimer = null;
        private Timer dispatcher = null;
        private readonly ManualResetEvent timerMRE = new ManualResetEvent(false);
        //private float _msenRelA, _msenRelB;

        //private readonly Util.SolarTimeNOAA SolarTime = null;

        //private Semaphore dispatcherS = null;
        private readonly RS485Dispatcher s485Dispatcher = null;
        //All external RS485 devices are here
        static public Dictionary<SerialEndpoint, SerialComm> SerialDevs { get; private set; }
        //Brightness control maintained here
        private readonly Util.BrightnessControl brightness = null;

        /*
        struct TimeCorrection
        {
            public bool saveRequest;
            public ulong tick;
            public DateTime time;
            public int ppm;
        }
        private TimeCorrection _tcA = new TimeCorrection() { saveRequest = false, tick = 0, ppm = 0 },
            _tcB = new TimeCorrection() { saveRequest = false, tick = 0, ppm = 0 };
        */

        public App()
        {
            InitializeComponent();
            
            SerialDevs = new Dictionary<SerialEndpoint, SerialComm> {
                { SerialEndpoint.ShowerConfig, new AirCondConfig(0x12, 0x13) },
                { SerialEndpoint.ShowerState, new AirCondState(0x11, 0) },
                { SerialEndpoint.LC1Config, new ControllerConfig(0x22, 0x23) },
                { SerialEndpoint.LC1State, new ControllerState(0x21, 0) },
                { SerialEndpoint.LC1GetOnTime, new ControllerChOnTime(0x24, 0) },
                { SerialEndpoint.LC1SetOnTime, new ControllerOTSet(0, 0x23) },
                { SerialEndpoint.LC2Config, new ControllerConfig(0x32, 0x33) },
                { SerialEndpoint.LC2State, new ControllerState(0x31, 0) },
                { SerialEndpoint.LC2GetOnTime, new ControllerChOnTime(0x34, 0) },
                { SerialEndpoint.LC2SetOnTime, new ControllerOTSet(0, 0x33) } };
            /*
            SerialDevs[SerialEndpoint.LC1State].DataReady += Controller1State;
            SerialDevs[SerialEndpoint.LC2State].DataReady += Controller2State;
            SerialDevs[SerialEndpoint.LC1Config].DataReady += Controller1Config;
            SerialDevs[SerialEndpoint.LC2Config].DataReady += Controller2Config;
            */
            s485Dispatcher = RS485Dispatcher.GetInstance();
            s485Dispatcher.Ready += UART_Configured;
            /*
            syncTimer = new Timer(SyncTimerCallback, timerMRE, TimeSpan.FromMilliseconds(-1), TimeSpan.FromDays(2));
            
            timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
            SolarTime = Util.SolarTimeNOAA.GetInstance();
            SolarTime.Configure(timeZoneInfo, 47.215, 38.925);
            */
            brightness = Util.BrightnessControl.GetInstance();
            Suspending += OnSuspending;
        }

        private void Dispatcher_Tick(object sender)
        {
            brightness.ReadLux();
        }

        private void UART_Configured()
        {
            dispatcher = new Timer(Dispatcher_Tick, null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1));
            s485Dispatcher.Start();
        }
        /*
        uint SwapEndianness(ulong x)
        {
            return (uint)(((x & 0x000000ff) << 24) |
                           ((x & 0x0000ff00) << 8) |
                           ((x & 0x00ff0000) >> 8) |
                           ((x & 0xff000000) >> 24));
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
                var milliseconds = (intPart * 1000) + (fractPart * 1000 / 0x100000000L);
                //**UTC** time
                ntpTime = new DateTime(1900, 1, 1).AddMilliseconds(milliseconds);
                stopwatch.Restart();
                SolarTime.CurrentDate = GetDateTime();
                timerMRE.Set();
                _tcA.saveRequest = _tcB.saveRequest = true;
            }
            sender.Dispose();
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
        public static void UpdateNTP()
        {
            ApplicationDataContainer applicationData = ApplicationData.Current.LocalSettings;
            if (!(applicationData.Values["IsNTPenabled"] is bool))
                applicationData.Values["IsNTPenabled"] = false;
            if ((bool)applicationData.Values["IsNTPenabled"] == true)
                syncTimer.Change(TimeSpan.FromMilliseconds(-1), TimeSpan.FromDays(2));
            else
                syncTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromDays(2));
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
                ApplicationDataContainer applicationData = ApplicationData.Current.LocalSettings;
                if (!(applicationData.Values["IsNTPenabled"] is bool))
                    applicationData.Values["IsNTPenabled"] = false;
                if ((bool)applicationData.Values["IsNTPenabled"] == true)
                    config.MSenEnable = true;
                else
                {
                    SolarTime.CurrentDate = ntpTime; //Corridor motion sensor
                    config.MSenEnable = SolarTime.Sunrise > ntpTime || ntpTime > SolarTime.Sunset;
                }
                float maxCurLvl = MathF.Max(_msenRelA, _msenRelB);
                float minChLvl = MathF.Max(config.MinLvlGet(0), config.MinLvlGet(1));
                config.MsenOnLvl = (byte)(MathF.Max(maxCurLvl, minChLvl) * 255);
            }
        }
        */
        
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
            if (e is null)
                throw new ArgumentNullException(nameof(e),ResourceLoader.GetForCurrentView().GetString("NullArgExc"));

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (!(Window.Current.Content is Frame rootFrame))
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

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                    dispatcher.Dispose();
                    timerMRE.Dispose();
                    s485Dispatcher.Dispose();
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
