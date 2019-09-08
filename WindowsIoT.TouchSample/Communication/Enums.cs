using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsIoT.Communication
{
    public static class Enums
    {
        public enum SerialEndpoint
        {   ShowerConfig, ShowerState, 
            LC1Config, LC1State, LC1GetOnTime, LC1SetOnTime,
            LC2Config, LC2State, LC2GetOnTime, LC2SetOnTime }
    }
}
