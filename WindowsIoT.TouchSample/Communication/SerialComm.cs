using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace WindowsIoT.Communication
{
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
        public float MinRH
        {
            get => BitConverter.ToInt16(buffer, 1) / 10f;
            set
            {
                lock (lockObj)
                    BitConverter.GetBytes((short)(value * 10)).CopyTo(buffer, 1);
            }
        }
        public float MaxRH
        {
            get => (BitConverter.ToInt16(buffer, 3) + BitConverter.ToInt16(buffer, 1)) / 10f;
            set
            {
                lock (lockObj)
                    BitConverter.GetBytes((short)(value * 10 - BitConverter.ToInt16(buffer, 1))).CopyTo(buffer, 3);
            }
        }
        public float MinT
        {
            get => BitConverter.ToInt16(buffer, 5) / 10f;
            set
            {
                lock (lockObj)
                    BitConverter.GetBytes((short)(value * 10)).CopyTo(buffer, 5);
            }
        }
        public float MaxT
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
}
