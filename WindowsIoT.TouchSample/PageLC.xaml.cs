using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using TouchPanels.Devices;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
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

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace WindowsIoT
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PageLC : Page
    {
        private byte linkNum, chANum, chBNum, chCNum;
        private byte[] sendConf, groupConf, aConf, bConf, cConf;
        private bool changed, refState, refConfig;
        ControllerConfig config = null;
        ControllerState state = null;
        ControllerChOnTime chOnTime = null;
        readonly DispatcherTimer dispatcher = new DispatcherTimer();
        readonly ObservableCollection<string> opmode = new ObservableCollection<string>();
        readonly RS485Dispatcher s485Dispatcher = RS485Dispatcher.GetInstance();

        public PageLC()
        {
            InitializeComponent();
            dispatcher.Interval = TimeSpan.FromMilliseconds(6666);
            dispatcher.Tick += Dispatcher_Tick;
            LayoutRoot.Children.Remove(lampB);
            LayoutRoot.Children.Remove(lampC);
            LayoutRoot.Children.Remove(msen);
            opmode.Add("Always off");
            opmode.Add("Always on");
            opmode.Add("Auto");
        }
        private void GroupSLlvl_ValueChanged(object _1, RangeBaseValueChangedEventArgs e)
        {
            if (lampaSLlvl == null)
                return;
            sendConf = groupConf;
            lampaSLlvl.Value = lampbSLlvl.Value = lampcSLlvl.Value = e.NewValue;
            changed = true;
        }
        private void LampaSLlvl_ValueChanged(object _1, RangeBaseValueChangedEventArgs _2)
        {
            if (sendConf == groupConf)
                return;
            sendConf = aConf;
            changed = true;
        }
        private void LampbSLlvl_ValueChanged(object _1, RangeBaseValueChangedEventArgs _2)
        {
            if (sendConf == groupConf)
                return;
            sendConf = bConf;
            changed = true;
        }
        private void LampcSLlvl_ValueChanged(object _1, RangeBaseValueChangedEventArgs _2)
        {
            if (sendConf == groupConf)
                return;
            sendConf = cConf;
            changed = true;
        }
        private void GroupSLminlvl_ValueChanged(object _1, RangeBaseValueChangedEventArgs e)
        {
            if (lampaSLminlvl == null)
                return;
            lampcSLminlvl.Value = lampbSLminlvl.Value = lampaSLminlvl.Value = e.NewValue;
        }
        
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e == null)
                throw new ArgumentNullException(nameof(e), "Argument shouldn't be null");
            pivot.Title = e.Parameter as string;
            switch (e.Parameter as string)
            {
                case "Kitchen":
                case "Room A":
                case "Toilet":
                case "Table":
                    config = App.SerialDevs[Enums.SerialEndpoint.LC1Config] as ControllerConfig;
                    state = App.SerialDevs[Enums.SerialEndpoint.LC1State] as ControllerState;
                    chOnTime = App.SerialDevs[Enums.SerialEndpoint.LC1GetOnTime] as ControllerChOnTime;
                    break;
                case "Room B":
                case "Room C":
                case "Corridor":
                case "Shower":
                    config = App.SerialDevs[Enums.SerialEndpoint.LC2Config] as ControllerConfig;
                    state = App.SerialDevs[Enums.SerialEndpoint.LC2State] as ControllerState;
                    chOnTime = App.SerialDevs[Enums.SerialEndpoint.LC2GetOnTime] as ControllerChOnTime;
                    break;
            }
            switch (e.Parameter as string)
            {
                case "Kitchen":
                    linkNum = 0;
                    pivot.Items.Add(lampB);
                    pivot.Items.Add(lampC);
                    lampB.Visibility = lampC.Visibility = Visibility.Visible;
                    chANum = 7; chBNum = 6; chCNum = 5;
                    break;
                case "Room A":
                    linkNum = 1;
                    pivot.Items.Add(lampB);
                    pivot.Items.Add(lampC);
                    lampB.Visibility = lampC.Visibility = Visibility.Visible;
                    chANum = 4; chBNum = 3; chCNum = 2;
                    break;
                case "Toilet":
                    linkNum = 2;
                    groupSLdelay.IsEnabled = false;
                    chANum = 1; chBNum = 9; chCNum = 9;
                    break;
                case "Table":
                    linkNum = 3;
                    lampaSLminlvl.IsEnabled = groupSLminlvl.IsEnabled = false;
                    groupSLfr.IsEnabled = groupSLdelay.IsEnabled = false;
                    chANum = 8; chBNum = 9; chCNum = 9;
                    break;
                case "Room B":
                    linkNum = 0;
                    pivot.Items.Add(lampB);
                    pivot.Items.Add(lampC);
                    lampB.Visibility = lampC.Visibility = Visibility.Visible;
                    chANum = 7; chBNum = 6; chCNum = 5;
                    break;
                case "Room C":
                    linkNum = 1;
                    pivot.Items.Add(lampB);
                    pivot.Items.Add(lampC);
                    lampB.Visibility = lampC.Visibility = Visibility.Visible;
                    chANum = 4; chBNum = 3; chCNum = 2;
                    break;
                case "Corridor":
                    linkNum = 2;
                    pivot.Items.Add(lampB);
                    pivot.Items.Add(msen);
                    lampB.Visibility = msen.Visibility = Visibility.Visible;
                    chANum = 1; chBNum = 0; chCNum = 9;
                    break;
                case "Shower":
                    linkNum = 3;
                    lampaSLminlvl.IsEnabled = groupSLminlvl.IsEnabled = false;
                    groupSLfr.IsEnabled = groupSLdelay.IsEnabled = false;
                    chANum = 8; chBNum = 9; chCNum = 9;
                    break;
            }
            groupConf = new byte[3] { chANum, chBNum, chCNum };
            aConf = new byte[1] { chANum };
            bConf = new byte[1] { chBNum };
            cConf = new byte[1] { chCNum };

            state.DataReady += StateRdy;
            config.DataReady += ConfigRdy;
            chOnTime.DataReady += OnTimeRdy;
            s485Dispatcher.EnqueueItem(chOnTime);
            
            refConfig = refState = true;
            changed = false;

            dispatcher.Start();
            base.OnNavigatedTo(e);
        }
        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            dispatcher.Stop();
            state.DataReady -= StateRdy;
            config.DataReady -= ConfigRdy;
            chOnTime.DataReady -= OnTimeRdy;
            base.OnNavigatingFrom(e);
        }

        private void StateRdy(SerialComm sender)
        {
            float al = state.GetChLevel(chANum), bl = state.GetChLevel(chBNum), cl = state.GetChLevel(chCNum);
            lampaTlvl.Text = (chANum != 8) ? 
                al.ToString("P0", CultureInfo.InvariantCulture) : (al > 0 ? "On" : "Off");
            lampbTlvl.Text = bl.ToString("P0", CultureInfo.InvariantCulture);
            lampcTlvl.Text = cl.ToString("P0", CultureInfo.InvariantCulture);
            if (chBNum == 9 && 9 == chCNum) //Only one active channel
                groupTlvl.Text = lampaTlvl.Text;
            else if (chCNum == 9) //Two active channels
            {
                float x = .5f * (al + bl);
                groupTlvl.Text = x.ToString("P0", CultureInfo.InvariantCulture);
                if (Math.Abs(x - al) >= .01 || Math.Abs(x - bl) >= .01)
                    groupTlvl.Text += "⚠";
            }
            else
            {
                float x = (al + bl + cl) / 3;
                groupTlvl.Text = x.ToString("P0", CultureInfo.InvariantCulture);
                if (Math.Abs(x - al) >= .01 || Math.Abs(x - bl) >= .01 || Math.Abs(x - cl) >= .01)
                    groupTlvl.Text += "⚠";
            }
            if (refState)
            {
                groupSLlvl.Value = state.GetChLevel(chANum) * 100;
                lampaSLlvl.Value = state.GetChLevel(chANum) * 100;
                lampbSLlvl.Value = state.GetChLevel(chBNum) * 100;
                lampcSLlvl.Value = state.GetChLevel(chCNum) * 100;
                refState = false;
            }
            if (pivot.Items.Contains(msen))
                opmodeT.Text = state.MSenState;
            rsLoad.Text = "RS485: " + s485Dispatcher.Statistics;
        }
        private void ConfigRdy(SerialComm sender)
        {
            float am = config.MinLvlGet(chANum), bm = config.MinLvlGet(chBNum), cm = config.MinLvlGet(chCNum);
            lampaTminLvl.Text = am.ToString("P0", CultureInfo.InvariantCulture);
            lampbTminLvl.Text = bm.ToString("P0", CultureInfo.InvariantCulture);
            lampcTminLvl.Text = cm.ToString("P0", CultureInfo.InvariantCulture);
            if (chBNum == 9 && 9 == chCNum) //Only one active channel
                groupTminLvl.Text = lampaTminLvl.Text;
            else if (chCNum == 9) //Two active channels
            {
                float y = .5f * (am + bm);
                groupTminLvl.Text = y.ToString("P0", CultureInfo.InvariantCulture);
                if (Math.Abs(y - am) >= .01 || Math.Abs(y - bm) >= .01)
                    groupTminLvl.Text += "⚠";
            }
            else
            {
                float y = (am + bm + cm) / 3;
                groupTminLvl.Text = y.ToString("P0", CultureInfo.InvariantCulture);
                if (Math.Abs(y - am) >= .01 || Math.Abs(y - bm) >= .01 || Math.Abs(y - cm) >= .01)
                    groupTminLvl.Text += "⚠";
            }
            groupTdelay.Text = string.Format(CultureInfo.InvariantCulture, 
                "{0:F1}s", config.LinkDelayGet(linkNum));
            groupTfr.Text = string.Format(CultureInfo.InvariantCulture, 
                "{0:F0}%/s", config.FadeRateGet(linkNum));
            if (pivot.Items.Contains(msen))
            {
                msenLL.Text = (config.MSenLowLvl / 255f).ToString("P0", CultureInfo.InvariantCulture);
                msenLT.Text = config.MSenLowTime.ToString(CultureInfo.InvariantCulture) + 's';
                msenOT.Text = config.MSenOnTime.ToString(CultureInfo.InvariantCulture) + 's';
            }
            if (refConfig)
            {
                groupSLminlvl.Value = config.MinLvlGet(chANum) * 100;
                lampaSLminlvl.Value = config.MinLvlGet(chANum) * 100;
                lampbSLminlvl.Value = config.MinLvlGet(chBNum) * 100;
                lampcSLminlvl.Value = config.MinLvlGet(chCNum) * 100;
                groupSLdelay.Value = config.LinkDelayGet(linkNum);
                groupSLfr.Value = config.FadeRateGet(linkNum);
                if (pivot.Items.Contains(msen))
                {
                    msenLowLvl.Value = config.MSenLowLvl / 2.55;
                    msenOnTime.Value = config.MSenOnTime;
                    msenLowTime.Value = config.MSenLowTime;
                    opmodecb.SelectedIndex = config.MSenAuto ? 2 : (config.MSenEnable ? 1 : 0);
                }
                refConfig = false;
            }
            config.MinLvlSet(chANum, (byte)(lampaSLminlvl.Value * 2.55));
            config.MinLvlSet(chBNum, (byte)(lampbSLminlvl.Value * 2.55));
            config.MinLvlSet(chCNum, (byte)(lampcSLminlvl.Value * 2.55));
            config.LinkDelaySet(linkNum, (byte)(32 * groupSLdelay.Value));
            config.FadeRateSet(linkNum, (byte)(8 * groupSLfr.Value / 3));
            if (!changed)
            {
                sendConf = null;
                config.OverrideLvlSet(null, 0);
            }
            else
            {
                if (groupConf.Equals(sendConf))
                    config.OverrideLvlSet(sendConf, (byte)(groupSLlvl.Value * 2.55));
                else if (aConf.Equals(sendConf))
                    config.OverrideLvlSet(sendConf, (byte)(lampaSLlvl.Value * 2.55));
                else if (bConf.Equals(sendConf))
                    config.OverrideLvlSet(sendConf, (byte)(lampbSLlvl.Value * 2.55));
                else if (cConf.Equals(sendConf))
                    config.OverrideLvlSet(sendConf, (byte)(lampcSLlvl.Value * 2.55));
            }
            if (pivot.Items.Contains(msen))
            {
                config.MSenOnTime = (byte)msenOnTime.Value;
                config.MSenLowTime = (byte)msenLowTime.Value;
                config.MSenLowLvl = (byte)(msenLowLvl.Value * 2.55);
                if (!opmodecb.SelectedIndex.Equals(2))
                    config.MSenEnable = opmodecb.SelectedIndex.Equals(1);
                config.MSenAuto = opmodecb.SelectedIndex.Equals(2);
            }
            changed = false;
        }
        private void OnTimeRdy(SerialComm sender)
        {
            var timeSpan = TimeSpan.FromSeconds(chOnTime.GetOnTime(chANum));
            lampaOT.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}:{1:D2}", (int)timeSpan.TotalHours, timeSpan.Minutes);
            lampaSC.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} cycles", chOnTime.GetSwCount(chANum));
            timeSpan = TimeSpan.FromSeconds(chOnTime.GetOnTime(chBNum));
            lampbOT.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}:{1:D2}", (int)timeSpan.TotalHours, timeSpan.Minutes);
            lampbSC.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} cycles", chOnTime.GetSwCount(chBNum));
            timeSpan = TimeSpan.FromSeconds(chOnTime.GetOnTime(chCNum));
            lampcOT.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}:{1:D2}", (int)timeSpan.TotalHours, timeSpan.Minutes);
            lampcSC.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} cycles", chOnTime.GetSwCount(chCNum));
        }

        private void Dispatcher_Tick(object sender, object e)
        {
            s485Dispatcher.EnqueueItem(chOnTime);
        }

        private void Snm_BackRequested(object _1, RoutedEventArgs _2)
        {
            Frame.Navigate(typeof(MainPage));
        }
    }
}
