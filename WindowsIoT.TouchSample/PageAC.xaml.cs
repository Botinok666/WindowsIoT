using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;
using WindowsIoT.Communication;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace WindowsIoT
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PageAC : Page
    {
        DispatcherTimer timer = new DispatcherTimer();
        RS485Dispatcher s485Dispatcher = RS485Dispatcher.GetInstance();
        bool refreshReq;
        public PageAC()
        {
            InitializeComponent();
            timer.Interval = TimeSpan.FromMilliseconds(1000);
            timer.Tick += Timer_Tick;
        }
        private void Timer_Tick(object sender, object e)
        {
            s485Dispatcher.EnqueueItem(App.serialComm[0]);
            s485Dispatcher.EnqueueItem(App.serialComm[1]);
            rsLoad.Text = string.Format("RS485: {0} bps, {1}", 
                s485Dispatcher.BusSpeed, s485Dispatcher.Statistics);
        }
        
        private void AirConfigRdy(SerialComm sender)
        {
            AirCondConfig config = sender as AirCondConfig;
            minTT.Text = string.Format("{0:F1}°C", config.MinT);
            maxTT.Text = string.Format("{0:F1}°C", config.MaxT);
            minTrh.Text = string.Format("{0:F0}%", config.MinRH);
            maxTrh.Text = string.Format("{0:F0}%", config.MaxRH);
            if (!refreshReq)
            {
                config.MinT = (float)minT.Value;
                config.MaxT = (float)maxT.Value;
                config.MinRH = (float)minRH.Value;
                config.MaxRH = (float)maxRH.Value;
                config.FanLevel = (float)fanLvl.Value / 100;
            }
        }
        private void AirStateRdy(SerialComm sender)
        {
            AirCondConfig config = App.serialComm[0] as AirCondConfig;
            AirCondState state = sender as AirCondState;
            if (config.IsBufferValid)
            {
                fanTlvl.Text = config.FanLevel >= .99f ? "Auto" : state.FanLevel.ToString("P0");
                if (refreshReq)
                {
                    minT.Value = config.MinT;
                    maxT.Value = config.MaxT;
                    minRH.Value = config.MinRH;
                    maxRH.Value = config.MaxRH;
                    fanLvl.Value = config.FanLevel >= .99f ? 100 : state.FanLevel * 100;
                    refreshReq = false;
                }
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            App.serialComm[0].DataReady += AirConfigRdy;
            App.serialComm[1].DataReady += AirStateRdy;
            s485Dispatcher.EnqueueItem(App.serialComm[0]);
            refreshReq = true;
            timer.Start();
            base.OnNavigatedTo(e);
        }
        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            timer.Stop();
            App.serialComm[0].DataReady -= AirConfigRdy;
            App.serialComm[1].DataReady -= AirStateRdy;
            base.OnNavigatingFrom(e);
        }
        private void Snm_BackRequested(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        private void MinT_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (maxT == null)
                return;
            if (maxT.Value - e.NewValue < 2)
                maxT.Value = e.NewValue + 2;
        }

        private void MaxT_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (minT == null)
                return;
            if (e.NewValue - minT.Value < 2)
                minT.Value = e.NewValue - 2;
        }

        private void MinRH_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (maxRH == null)
                return;
            if (maxRH.Value - e.NewValue < 10)
                maxRH.Value = e.NewValue + 10;
        }

        private void MaxRH_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (minRH == null)
                return;
            if (e.NewValue - minRH.Value < 10)
                minRH.Value = e.NewValue - 10;
        }

        private void FanLvl_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
        }
    }
}
