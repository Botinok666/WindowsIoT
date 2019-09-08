using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.I2c;

namespace WindowsIoT.Util
{
    public class BrightnessControl
    {
        private static BrightnessControl _instance = null;
        private uint _timeout = 0;
        private I2cDevice max44009 = null, pca9685 = null;
        private float _minLevel, _maxLux, _currentLvl;
        private readonly byte[] pinData, result;
        public enum ControlMode
        { Auto, Fixed }

        private BrightnessControl()
        {
            _minLevel = .1f;
            _maxLux = 2000f;
            _currentLvl = .66f;
            Mode = ControlMode.Auto;
            result = new byte[1];
            pinData = new byte[5] { 0x06, 0, 0, 0xF0, 0x0F }; //Ton = 0, Toff = 4080
            GetI2Clist();
        }
        /// <summary>
        /// Singleton patterb
        /// </summary>
        /// <returns>Instance of this class
        /// </returns>
        public static BrightnessControl GetInstance()
        {
            return _instance ?? (_instance = new BrightnessControl());
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
                    _currentLvl = value;
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
        private void SetDutyCycle()
        {
            BitConverter.GetBytes((ushort)((1 - Level) * 4080)).CopyTo(pinData, 1);
            pca9685.Write(pinData);
        }
        private async void GetI2Clist()
        {
            ConfigTrace = "I2C devices: ";
            var i2c = await DeviceInformation.FindAllAsync(I2cDevice.GetDeviceSelector());
            max44009 = await I2cDevice.FromIdAsync(i2c[0].Id, new I2cConnectionSettings(0b1001010));
            ConfigTrace += max44009 == null ? "none" : max44009.DeviceId + Environment.NewLine;
            pca9685 = await I2cDevice.FromIdAsync(i2c[0].Id, new I2cConnectionSettings(0b1000000));
            ConfigTrace += pca9685 == null ? "none" : pca9685.DeviceId + Environment.NewLine;
            pca9685.Write(new byte[] { 0x00, 33 }); //Auto increment, clear sleep
            await Task.Delay(1);
            pca9685.Write(new byte[] { 0xFE, 41 }); //~145Hz
            pca9685.Write(new byte[] { 0x00, 161 }); //Restart
            pca9685.Write(new byte[] { 0xFA, 0, 0, 0, 0 });
            _currentLvl = .1f;
            SetDutyCycle();
        }
        public void ResetTimeout()
        {
            _timeout = 0;
        }
        /// <summary>
        /// Retrieves current lux value from MAX44009. Performs brightness correction (in auto mode)
        /// Also increases timeout counter
        /// </summary>
        public void ReadLux()
        {
            if (!HWStatus)
                return;
            if (_timeout++ > 30)
            {
                _currentLvl = 0;
                SetDutyCycle();
                return;
            }
            try
            {
                max44009.WriteRead(new byte[] { 0x03 }, result);
                byte msb = result[0];
                max44009.WriteRead(new byte[] { 0x04 }, result);
                Lux = (((byte)(msb << 4) | result[0]) << (msb >> 4)) * .045f;
                if (Mode == ControlMode.Auto)
                {
                    _currentLvl = MinLevel + (Lux > 1 ?
                        (float)(Math.Log(Lux) * (1 - MinLevel) / Math.Log(MaxLux)) : 0);
                    SetDutyCycle();
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
            get => (max44009 != null && pca9685 != null);
        }
    }
}
