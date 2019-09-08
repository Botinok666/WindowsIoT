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
using WindowsIoT.Communication;
using WindowsIoT.Util;

using static WindowsIoT.Communication.Enums;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace WindowsIoT
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class TimeInfo : Page
    {
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly RS485Dispatcher s485Dispatcher = RS485Dispatcher.GetInstance();
        readonly BrightnessControl brightnessControl = BrightnessControl.GetInstance();
        readonly SolarTimeNOAA SolarTime = SolarTimeNOAA.GetInstance();

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
            App.SerialDevs[SerialEndpoint.LC1State].DataReady += C1StateRdy;
            App.SerialDevs[SerialEndpoint.LC2State].DataReady += C2StateRdy;
            RegModeSw.IsOn = brightnessControl.Mode == BrightnessControl.ControlMode.Auto;
            BlLevel.Value = brightnessControl.Level * 100;
            BlMinLvl.Value = brightnessControl.MinLevel * 100;
            AmbLuxMax.Value = 101 - Math.Exp((2050 - brightnessControl.MaxLux) * Math.Log(100) / 2000);
            timer.Start();

            Windows.Storage.ApplicationDataContainer applicationData = 
                Windows.Storage.ApplicationData.Current.LocalSettings;
            if (!(applicationData.Values["IsNTPenabled"] is bool))
                applicationData.Values["IsNTPenabled"] = false;
            ntpEn.IsOn = (bool)applicationData.Values["IsNTPenabled"];
            base.OnNavigatedTo(e);
        }
        private void Timer_Tick(object sender, object e)
        {
            TimeSpan timeSpan = App.SyncTimeSpan();
            ntpSync.Text = timeSpan.Equals(TimeSpan.FromDays(365)) ? "N/A" :
                string.Format(CultureInfo.InvariantCulture,
                    "{0}:{1:D2}:{2:D2}", 
                    (int)timeSpan.TotalHours, timeSpan.Minutes, timeSpan.Seconds);
            rsLoad.Text = "RS485: " + s485Dispatcher.Statistics;
            LuxTLevel.Text = string.Format(CultureInfo.InvariantCulture,
                "Ambient light level: {0:F1}lux", 
                brightnessControl.Lux);
            BlTLevel.Text = brightnessControl.Level.ToString("P0", CultureInfo.InvariantCulture);
            if (brightnessControl.Mode == BrightnessControl.ControlMode.Auto)
                BlLevel.Value = brightnessControl.Level * 100;
            BlTMinLvl.Text = brightnessControl.MinLevel.ToString("P0", CultureInfo.InvariantCulture);
        }
        private void C1StateRdy(SerialComm sender)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds((sender as ControllerState).Tick / 32.0);
            lc1Ot.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}:{1:D2}:{2:D2}", 
                (int)timeSpan.TotalHours, timeSpan.Minutes, timeSpan.Seconds);
            lc1ppm.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} ppm", 
                (App.SerialDevs[SerialEndpoint.LC1Config] as ControllerConfig).RTCCorrect);
        }
        private void C2StateRdy(SerialComm sender)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds((sender as ControllerState).Tick / 32.0);
            lc2Ot.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}:{1:D2}:{2:D2}", 
                (int)timeSpan.TotalHours, timeSpan.Minutes, timeSpan.Seconds);
            lc2ppm.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} ppm", 
                (App.SerialDevs[SerialEndpoint.LC2Config] as ControllerConfig).RTCCorrect);
        }
        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            timer.Stop();
            App.SerialDevs[SerialEndpoint.LC1State].DataReady -= C1StateRdy;
            App.SerialDevs[SerialEndpoint.LC2State].DataReady -= C2StateRdy;
            base.OnNavigatingFrom(e);
        }
        private void Snm_BackRequested(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(MainPage));
        }

        private void BlLevel_ValueChanged(object _1, RangeBaseValueChangedEventArgs e)
        {
            brightnessControl.Level = (float)e.NewValue / 100f;
        }

        private void BlMinLvl_ValueChanged(object _1, RangeBaseValueChangedEventArgs e)
        {
            if (BlTMinLvl == null)
                return;
            brightnessControl.MinLevel = (float)e.NewValue / 100f;
        }

        private void AmbLuxMax_ValueChanged(object _1, RangeBaseValueChangedEventArgs e)
        {
            if (AmbTLux == null) return;
            brightnessControl.MaxLux = 2050f - (float)(Math.Log(101 - e.NewValue) * 2000 / Math.Log(AmbLuxMax.Maximum));
            AmbTLux.Text = brightnessControl.MaxLux.ToString("F0", CultureInfo.InvariantCulture) + "lux";
        }

        private async void LuxTLevel_Tapped(object _1, TappedRoutedEventArgs _2)
        {
            await new Windows.UI.Popups.MessageDialog(brightnessControl.ConfigTrace, 
                "HW state: " + brightnessControl.HWStatus.ToString(CultureInfo.InvariantCulture)).ShowAsync();
        }

        private void RegModeSw_Toggled(object _1, RoutedEventArgs _2)
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

        private void NtpEn_Toggled(object _1, RoutedEventArgs _2)
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["IsNTPenabled"] = ntpEn.IsOn;
            App.UpdateNTP();
        }
    }
}
