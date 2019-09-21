using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using TouchPanels.Devices;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Input;
using Windows.UI.Input.Preview.Injection;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WindowsIoT.Communication;
using static WindowsIoT.Communication.Enums;

// The Blank Page item template is documented at 
//http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace WindowsIoT
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    
    public sealed partial class MainPage : Page
    {
        readonly DispatcherTimer dispatcher1s = new DispatcherTimer();
        readonly RS485Dispatcher s485Dispatcher = RS485Dispatcher.GetInstance();

        const string CalibrationFilename = "TSC2046";
        private static Tsc2046 tsc2046;
        private static TouchPanels.TouchProcessor processor;

        public MainPage()
        {
            InitializeComponent();
            Init();
            dispatcher1s.Tick += Dispatcher1s_Tick;
            dispatcher1s.Interval = TimeSpan.FromSeconds(1);
            //CalibrateTouch();
        }
        private void Dispatcher1s_Tick(object sender, object e)
        {
            /*
            DateTime ntpTime = App.GetDateTime();
            homeDateTime.Content = (ntpTime.Year < 2018) ? "N/A" :
                ntpTime.ToString("dddd d MMM H:mm:ss", DateTimeFormatInfo.InvariantInfo);
            */
            s485Dispatcher.EnqueueItem(App.SerialDevs[SerialEndpoint.LC1State]);
            s485Dispatcher.EnqueueItem(App.SerialDevs[SerialEndpoint.LC2State]);
            s485Dispatcher.EnqueueItem(App.SerialDevs[SerialEndpoint.ShowerState]);
        }
        private async void Init()
        {
            tsc2046 = await Tsc2046.GetDefaultAsync().ConfigureAwait(true);
            if (!tsc2046.IsCalibrated)
            {
                try
                {
                    await tsc2046.LoadCalibrationAsync(CalibrationFilename).ConfigureAwait(true);
                }
                catch (FileNotFoundException)
                {
                    await CalibrateTouch().ConfigureAwait(true); //Initiate calibration
                }
                catch (UnauthorizedAccessException)
                {
                    //No access to documents folder
                    await new Windows.UI.Popups.MessageDialog("Make sure the application " +
                        "manifest specifies access to the documents folder and declares the " +
                        "file type association for the calibration file.", 
                        "Configuration Error").ShowAsync();
                    throw;
                }
            }
            //Load up the touch processor and listen for touch events
            if (processor == null)
            {
                processor = new TouchPanels.TouchProcessor(tsc2046);
                processor.PointerDown += Processor_PointerDown;
                processor.PointerMoved += Processor_PointerMoved;
                processor.PointerUp += Processor_PointerUp;
            }
        }
        private async Task CalibrateTouch()
        {
            var calibration = await TouchPanels.UI
                .LcdCalibrationView.CalibrateScreenAsync(tsc2046).ConfigureAwait(true);
            tsc2046.SetCalibration(calibration.A, calibration.B, calibration.C, 
                calibration.D, calibration.E, calibration.F);
            try
            {
                await tsc2046.SaveCalibrationAsync(CalibrationFilename).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await new Windows.UI.Popups.MessageDialog(ex.Message, ex.Source)
                    .ShowAsync();
            }
        }
        
        private uint _pID = 0;
        InputInjector _injector;
        InjectedInputRectangle inputRectangle = new InjectedInputRectangle()
        { Bottom = 24, Top = 24, Left = 24, Right = 24 };
        private void Processor_PointerDown(object sender, TouchPanels.PointerEventArgs e)
        {
            //Reset timeout for backlight
            Util.BrightnessControl.GetInstance().ResetTimeout();
            if (Util.BrightnessControl.GetInstance().Level == 0)
                return;
            _injector = InputInjector.TryCreate();
            _injector.InitializeTouchInjection(InjectedInputVisualizationMode.Indirect);
            List<InjectedInputTouchInfo> PDown = new List<InjectedInputTouchInfo>()
            {
                new InjectedInputTouchInfo()
                {
                    Contact = inputRectangle,
                    PointerInfo = new InjectedInputPointerInfo()
                    {
                        PointerId = _pID,
                        PixelLocation = new InjectedInputPoint()
                            { PositionX = (int)e.Position.X, PositionY = (int)e.Position.Y },
                        PointerOptions = InjectedInputPointerOptions.InContact | 
                            InjectedInputPointerOptions.InRange |
                            InjectedInputPointerOptions.PointerDown,
                        TimeOffsetInMilliseconds = 0
                    },
                    Pressure = e.Pressure / 255f,
                    TouchParameters = InjectedInputTouchParameters.Pressure | 
                        InjectedInputTouchParameters.Contact
                }
            };
            _injector.InjectTouchInput(PDown);
        }
        private void Processor_PointerMoved(object sender, TouchPanels.PointerEventArgs e)
        {
            if (Util.BrightnessControl.GetInstance().Level == 0)
                return;
            List<InjectedInputTouchInfo> PMove = new List<InjectedInputTouchInfo>()
            {
                new InjectedInputTouchInfo()
                {
                    Contact = inputRectangle,
                    PointerInfo = new InjectedInputPointerInfo()
                    {
                        PointerId = _pID,
                        PixelLocation = new InjectedInputPoint()
                            { PositionX = (int)e.Position.X, PositionY = (int)e.Position.Y },
                        PointerOptions = InjectedInputPointerOptions.InContact | 
                            InjectedInputPointerOptions.InRange |
                            InjectedInputPointerOptions.PointerDown,
                        TimeOffsetInMilliseconds = 0
                    },
                    Pressure = e.Pressure / 255f,
                    TouchParameters = InjectedInputTouchParameters.Pressure | 
                        InjectedInputTouchParameters.Contact
                }
            };
            _injector.InjectTouchInput(PMove);
        }
        private void Processor_PointerUp(object sender, TouchPanels.PointerEventArgs e)
        {
            if (Util.BrightnessControl.GetInstance().Level == 0)
                return;
            List<InjectedInputTouchInfo> PUp = new List<InjectedInputTouchInfo>()
            {
                new InjectedInputTouchInfo()
                {
                    PointerInfo = new InjectedInputPointerInfo()
                    {
                        PointerId = _pID++,
                        PointerOptions = InjectedInputPointerOptions.PointerUp
                    }
                }
            };
            _injector.InjectTouchInput(PUp);
            _injector.UninitializeTouchInjection();
        }
        private void AirStateRdy(SerialComm sender)
        {
            AirCondState acstate = sender as AirCondState;
            blowerLvl.Content = (acstate.FanLevel > 0) ? 
                acstate.FanLevel.ToString("P0", CultureInfo.InvariantCulture) : "Off";
            blowerFrp.Text = acstate.RPMFront.ToString(CultureInfo.InvariantCulture);
            blowerRrp.Text = acstate.RPMRear.ToString(CultureInfo.InvariantCulture);
            blowerIload.Text = string.Format(CultureInfo.InvariantCulture, 
                "{0:G4}A", acstate.CurrentDraw);
            showerT.Text = string.Format(CultureInfo.InvariantCulture, 
                "{0:G3}°C", acstate.InsideT);
            showerRH.Text = string.Format(CultureInfo.InvariantCulture, 
                "{0:P1}", acstate.InsideRH);
        }
        private void C1StateRdy(SerialComm sender)
        {
            ControllerState state = sender as ControllerState;
            kitchenLL.Content = !state.IsLinkValid(0) ? "N/C" :
                ((state.GetLinkLevel(0) > 0) ? 
                state.GetLinkLevel(0).ToString("P0", CultureInfo.InvariantCulture) : "Off");
            roomaLL.Content = !state.IsLinkValid(1) ? "N/C" :
                ((state.GetLinkLevel(1) > 0) ?
                state.GetLinkLevel(1).ToString("P0", CultureInfo.InvariantCulture) : "Off");
            toiletLL.Content = !state.IsLinkValid(2) ? "N/C" :
                ((state.GetLinkLevel(2) > 0) ? 
                state.GetLinkLevel(2).ToString("P0", CultureInfo.InvariantCulture) : "Off");
            tableLL.Content = !state.IsLinkValid(3) ? "N/C" : 
                ((state.GetLinkLevel(3) > 0) ? "On" : "Off");
        }
        private void C2StateRdy(SerialComm sender)
        {
            ControllerState state = sender as ControllerState;
            roombLL.Content = !state.IsLinkValid(0) ? "N/C" :
                ((state.GetLinkLevel(0) > 0) ? 
                state.GetLinkLevel(0).ToString("P0", CultureInfo.InvariantCulture) : "Off");
            roomcLL.Content = !state.IsLinkValid(1) ? "N/C" :
                ((state.GetLinkLevel(1) > 0) ? 
                state.GetLinkLevel(1).ToString("P0", CultureInfo.InvariantCulture) : "Off");
            corridorLL.Content = !state.IsLinkValid(2) ? "N/C" :
                ((state.GetLinkLevel(2) > 0) ? 
                state.GetLinkLevel(2).ToString("P0", CultureInfo.InvariantCulture) : "Off");
            showerLL.Content = !state.IsLinkValid(3) ? "N/C" : 
                ((state.GetLinkLevel(3) > 0) ? "On" : "Off");
            msenState.Text = state.MSenState;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            dispatcher1s.Start();
            App.SerialDevs[SerialEndpoint.ShowerState].DataReady += AirStateRdy;
            App.SerialDevs[SerialEndpoint.LC1State].DataReady += C1StateRdy; //These two should be enqueued from main
            App.SerialDevs[SerialEndpoint.LC2State].DataReady += C2StateRdy;
            base.OnNavigatedTo(e);
        }
        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            dispatcher1s.Stop();
            App.SerialDevs[SerialEndpoint.ShowerState].DataReady -= AirStateRdy;
            App.SerialDevs[SerialEndpoint.LC1State].DataReady -= C1StateRdy;
            App.SerialDevs[SerialEndpoint.LC2State].DataReady -= C2StateRdy;
            base.OnNavigatingFrom(e);
        }

        private void BlowerConfigClick(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(PageAC));
        }
        private void KitchenClick(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(PageLC), "Kitchen");
        }
        private void TableClick(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(PageLC), "Table");
        }
        private void ShowerClick(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(PageLC), "Shower");
        }
        private void ToiletClick(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(PageLC), "Toilet");
        }
        private void RoomAClick(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(PageLC), "Room A");
        }
        private void HomeDateTime_Click(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(TimeInfo));
        }
        private void CorridorClick(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(PageLC), "Corridor");
        }
        private void RoomBClick(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(PageLC), "Room B");
        }
        private void RoomCClick(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(PageLC), "Room C");
        }
    }
}
