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

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace WindowsIoT
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    
    public sealed partial class MainPage : Page
    {
        DispatcherTimer dispatcher1s = new DispatcherTimer();
        RS485Dispatcher s485Dispatcher = RS485Dispatcher.GetInstance();

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
            DateTime ntpTime = App.GetDateTime();
            homeDateTime.Content = (ntpTime.Year < 2018) ? "N/A" :
                homeDateTime.Content = ntpTime.ToString("dddd d MMM H:mm:ss", DateTimeFormatInfo.InvariantInfo);
            
            rsUtil.Text = string.Format("{0} bps", s485Dispatcher.BusSpeed);
            s485Dispatcher.EnqueueItem(App.serialComm[1]); //AirCondState
        }

        private async void Init()
        {
            tsc2046 = await Tsc2046.GetDefaultAsync();
            if (!tsc2046.IsCalibrated)
            {
                try
                {
                    await tsc2046.LoadCalibrationAsync(CalibrationFilename);
                }
                catch (FileNotFoundException)
                {
                    await CalibrateTouch(); //Initiate calibration if we don't have a calibration on file
                }
                catch (UnauthorizedAccessException)
                {
                    //No access to documents folder
                    await new Windows.UI.Popups.MessageDialog("Make sure the application manifest specifies access to the documents folder and declares the file type association for the calibration file.", "Configuration Error").ShowAsync();
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
            var calibration = await TouchPanels.UI.LcdCalibrationView.CalibrateScreenAsync(tsc2046);
            tsc2046.SetCalibration(calibration.A, calibration.B, calibration.C, calibration.D, calibration.E, calibration.F);
            try
            {
                await tsc2046.SaveCalibrationAsync(CalibrationFilename);
            }
            catch (Exception ex)
            {
                await new Windows.UI.Popups.MessageDialog(ex.Message, ex.Source).ShowAsync();
            }
        }
        
        private uint _pID = 0;
        InputInjector _injector;
        private void Processor_PointerDown(object sender, TouchPanels.PointerEventArgs e)
        {
            _injector = InputInjector.TryCreate();
            _injector.InitializeTouchInjection(InjectedInputVisualizationMode.Indirect);
            List<InjectedInputTouchInfo> PDown = new List<InjectedInputTouchInfo>()
            {
                new InjectedInputTouchInfo()
                {
                    Contact = new InjectedInputRectangle() { Bottom = 8,Top = 8,Left = 8, Right = 8},
                    PointerInfo = new InjectedInputPointerInfo()
                    {
                        PointerId = _pID,
                        PixelLocation = new InjectedInputPoint()
                            { PositionX = (int)e.Position.X, PositionY = (int)e.Position.Y },
                        PointerOptions = InjectedInputPointerOptions.InContact | InjectedInputPointerOptions.InRange |
                            InjectedInputPointerOptions.PointerDown,
                        TimeOffsetInMilliseconds = 0
                    },
                    Pressure = e.Pressure / 255f,
                    TouchParameters = InjectedInputTouchParameters.Pressure | InjectedInputTouchParameters.Contact
                }
            };
            _injector.InjectTouchInput(PDown);
        }
        private void Processor_PointerMoved(object sender, TouchPanels.PointerEventArgs e)
        {
            List<InjectedInputTouchInfo> PMove = new List<InjectedInputTouchInfo>()
            {
                new InjectedInputTouchInfo()
                {
                    Contact = new InjectedInputRectangle() { Bottom = 8,Top = 8,Left = 8, Right = 8},
                    PointerInfo = new InjectedInputPointerInfo()
                    {
                        PointerId = _pID,
                        PixelLocation = new InjectedInputPoint()
                            { PositionX = (int)e.Position.X, PositionY = (int)e.Position.Y },
                        PointerOptions = InjectedInputPointerOptions.InContact | InjectedInputPointerOptions.InRange |
                            InjectedInputPointerOptions.PointerDown,
                        TimeOffsetInMilliseconds = 0
                    },
                    Pressure = e.Pressure / 255f,
                    TouchParameters = InjectedInputTouchParameters.Pressure | InjectedInputTouchParameters.Contact
                }
            };
            _injector.InjectTouchInput(PMove);
        }
        private void Processor_PointerUp(object sender, TouchPanels.PointerEventArgs e)
        {
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
            blowerLvl.Content = (acstate.FanLevel > 0) ? acstate.FanLevel.ToString("P0") : "Off";
            blowerFrp.Text = acstate.RPMFront.ToString();
            blowerRrp.Text = acstate.RPMRear.ToString();
            blowerIload.Text = string.Format("{0:G4}A", acstate.CurrentDraw);
            showerT.Text = string.Format("{0:G3}°C", acstate.InsideT);
            showerRH.Text = string.Format("{0:P1}", acstate.InsideRH);
            kitchenT.Text = string.Format("{0:G3}°C", acstate.OutsideT);
            kitchenRH.Text = string.Format("{0:P1}", acstate.OutsideRH);
        }
        private void C1StateRdy(SerialComm sender)
        {
            ControllerState state = sender as ControllerState;
            kitchenLL.Content = !state.IsLinkValid(0) ? "N/C" :
                ((state.GetLinkLevel(0) > 0) ? state.GetLinkLevel(0).ToString("P0") : "Off");
            roomaLL.Content = !state.IsLinkValid(1) ? "N/C" :
                ((state.GetLinkLevel(1) > 0) ? state.GetLinkLevel(1).ToString("P0") : "Off");
            toiletLL.Content = !state.IsLinkValid(2) ? "N/C" :
                ((state.GetLinkLevel(2) > 0) ? state.GetLinkLevel(2).ToString("P0") : "Off");
            tableLL.Content = !state.IsLinkValid(3) ? "N/C" : ((state.GetLinkLevel(3) > 0) ? "On" : "Off");
        }
        private void C2StateRdy(SerialComm sender)
        {
            ControllerState state = sender as ControllerState;
            roombLL.Content = !state.IsLinkValid(0) ? "N/C" :
                ((state.GetLinkLevel(0) > 0) ? state.GetLinkLevel(0).ToString("P0") : "Off");
            roomcLL.Content = !state.IsLinkValid(1) ? "N/C" :
                ((state.GetLinkLevel(1) > 0) ? state.GetLinkLevel(1).ToString("P0") : "Off");
            corridorLL.Content = !state.IsLinkValid(2) ? "N/C" :
                ((state.GetLinkLevel(2) > 0) ? state.GetLinkLevel(2).ToString("P0") : "Off");
            showerLL.Content = !state.IsLinkValid(3) ? "N/C" : ((state.GetLinkLevel(3) > 0) ? "On" : "Off");
            msenState.Text = state.MSenState;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            dispatcher1s.Start();
            App.serialComm[1].DataReady += AirStateRdy;
            App.serialComm[3].DataReady += C1StateRdy; //These two should be enqueued from main
            App.serialComm[7].DataReady += C2StateRdy;
            base.OnNavigatedTo(e);
        }
        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            dispatcher1s.Stop();
            App.serialComm[1].DataReady -= AirStateRdy;
            App.serialComm[3].DataReady -= C1StateRdy;
            App.serialComm[7].DataReady -= C2StateRdy;
            base.OnNavigatingFrom(e);
        }

        private void BlowerConfigClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PageAC));
        }
        private void KitchenClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PageLC), "Kitchen");
        }
        private void TableClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PageLC), "Table");
        }
        private void ShowerClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PageLC), "Shower");
        }
        private void ToiletClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PageLC), "Toilet");
        }
        private void RoomAClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PageLC), "Room A");
        }
        private void HomeDateTime_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(TimeInfo));
        }
        private void CorridorClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PageLC), "Corridor");
        }
        private void RoomBClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PageLC), "Room B");
        }
        private void RoomCClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PageLC), "Room C");
        }
    }
}
