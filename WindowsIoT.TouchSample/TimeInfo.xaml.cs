using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking.Connectivity;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace WindowsIoT
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class TimeInfo : Page
    {
        DispatcherTimer timer = new DispatcherTimer();
        RS485Dispatcher s485Dispatcher = RS485Dispatcher.GetInstance();
        BrightnessControl brightnessControl = BrightnessControl.GetInstance();
        SolarTimeNOAA SolarTime = SolarTimeNOAA.GetInstance();

        public TimeInfo()
        {
            InitializeComponent();
            timer.Interval = TimeSpan.FromMilliseconds(1000);
            timer.Tick += Timer_Tick;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            DateTime dateTime = App.GetDateTime();
            SolarTime.CurrentDate = dateTime;
            if (dateTime <= SolarTime.Sunrise)
            {
                SunEvent.Text = "Sunrise:";
                sEvVal.Text = SolarTime.Sunrise.ToString("H:mm:ss", DateTimeFormatInfo.InvariantInfo);
            }
            else if (SolarTime.Sunrise < dateTime && dateTime <= SolarTime.Sunset)
            {
                SunEvent.Text = "Sunset:";
                sEvVal.Text = SolarTime.Sunset.ToString("H:mm:ss", DateTimeFormatInfo.InvariantInfo);
            }
            else
            {
                SunEvent.Text = "Sunrise tomorrow:";
                SolarTime.CurrentDate = dateTime.AddDays(1);
                sEvVal.Text = SolarTime.Sunrise.ToString("H:mm:ss", DateTimeFormatInfo.InvariantInfo);
            }
            App.serialComm[3].DataReady += C1StateRdy;
            App.serialComm[7].DataReady += C2StateRdy;
            RegModeSw.IsOn = brightnessControl.Mode == BrightnessControl.ControlMode.Auto;
            BlLevel.Value = brightnessControl.Level * 100;
            BlMinLvl.Value = brightnessControl.MinLevel * 100;
            AmbLuxMax.Value = 101 - Math.Exp((2050 - brightnessControl.MaxLux) * Math.Log(100) / 2000);
            timer.Start();
            base.OnNavigatedTo(e);
        }
        private void Timer_Tick(object sender, object e)
        {
            TimeSpan timeSpan = App.SyncTimeSpan();
            ntpSync.Text = timeSpan.Equals(TimeSpan.FromDays(365)) ? "N/A" :
                string.Format("{0}:{1:D2}:{2:D2}", (int)timeSpan.TotalHours, timeSpan.Minutes, timeSpan.Seconds);
            rsLoad.Text = string.Format("RS485 load: {0} bps, {1}", 
                s485Dispatcher.BusSpeed, s485Dispatcher.Statistics);
            LuxTLevel.Text = string.Format("Ambient light level: {0:F1}lux", brightnessControl.Lux);
            BlTLevel.Text = brightnessControl.Level.ToString("P0");
            if (brightnessControl.Mode == BrightnessControl.ControlMode.Auto)
                BlLevel.Value = brightnessControl.Level * 100;
            BlTMinLvl.Text = brightnessControl.MinLevel.ToString("P0");
        }
        private void C1StateRdy(SerialComm sender)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds((sender as ControllerState).Tick / 32.0);
            lc1Ot.Text = string.Format("{0}:{1:D2}:{2:D2}", (int)timeSpan.TotalHours, timeSpan.Minutes, timeSpan.Seconds);
        }
        private void C2StateRdy(SerialComm sender)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds((sender as ControllerState).Tick / 32.0);
            lc2Ot.Text = string.Format("{0}:{1:D2}:{2:D2}", (int)timeSpan.TotalHours, timeSpan.Minutes, timeSpan.Seconds);
        }
        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            timer.Stop();
            App.serialComm[3].DataReady -= C1StateRdy;
            App.serialComm[7].DataReady -= C2StateRdy;
            base.OnNavigatingFrom(e);
        }
        private void Snm_BackRequested(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        private void BlLevel_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            brightnessControl.Level = (float)e.NewValue / 100f;
        }

        private void BlMinLvl_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (BlTMinLvl == null)
                return;
            brightnessControl.MinLevel = (float)e.NewValue / 100f;
        }

        private void AmbLuxMax_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (AmbTLux == null) return;
            brightnessControl.MaxLux = 2050f - (float)(Math.Log(101 - e.NewValue) * 2000 / Math.Log(AmbLuxMax.Maximum));
            AmbTLux.Text = brightnessControl.MaxLux.ToString("F0") + "lux";
        }

        private async void LuxTLevel_Tapped(object sender, TappedRoutedEventArgs e)
        {
            await new Windows.UI.Popups.MessageDialog(brightnessControl.ConfigTrace, 
                "HW state: " + brightnessControl.HWStatus.ToString()).ShowAsync();
        }

        private void RegModeSw_Toggled(object sender, RoutedEventArgs e)
        {
            if (BlMinLvl == null)
                return;
            if (RegModeSw.IsOn)
            {
                brightnessControl.Mode = BrightnessControl.ControlMode.Auto;
                BlMinLvl.IsEnabled = AmbLuxMax.IsEnabled = true;
                BlLevel.IsEnabled = false;
            }
            else
            {
                brightnessControl.Mode = BrightnessControl.ControlMode.Fixed;
                BlMinLvl.IsEnabled = AmbLuxMax.IsEnabled = false;
                BlLevel.IsEnabled = true;
            }
        }
    }
}
