using System;

namespace WindowsIoT.Util
{
    public class SolarTimeNOAA
    {
        private TimeZoneInfo _timeZone;
        private double _latitude, _longitude;
        private static SolarTimeNOAA _instance = null;
        /// <summary>
        /// Configuration
        /// </summary>
        /// <param name="tz">Timezone information</param>
        /// <param name="latitude">Geographical latitude</param>
        /// <param name="longitude">Geographical longitude</param>
        public void Configure(TimeZoneInfo tz, double latitude, double longitude)
        {
            _timeZone = tz;
            _latitude = latitude * Math.PI / 180;
            _longitude = longitude;
        }
        /// <summary>
        /// Singleton pattern
        /// </summary>
        /// <returns>Instance of this class</returns>
        public static SolarTimeNOAA GetInstance()
        {
            if (_instance == null)
                _instance = new SolarTimeNOAA();
            return _instance;
        }
        private SolarTimeNOAA()
        {
            Sunrise = new DateTime(2018, 5, 22);
            Sunset = new DateTime(2018, 5, 30);
        }
        /// <summary>
        /// Calculates sunrise and sunset times from given date
        /// </summary>
        public DateTime CurrentDate
        {
            set
            {
                double jc = (value.Date.Ticks / TimeSpan.TicksPerDay - 7.301185e+5 -
                    _timeZone.GetUtcOffset(value).Hours / 24.0) / 3.6525e+4;
                double gmls = 4.895063 + jc * (628.331967 + jc * 5.29184e-6);
                double gmas = 6.24006 + jc * (628.301955 - jc * 2.68257e-6);
                double eeo = 1.6708634e-2 - jc * (4.2037e-5 + 1.267e-7 * jc);
                double seoc = Math.Sin(gmas) * (3.341611e-2 - jc * (8.40725e-5 + 2.443461e-7 * jc)) +
                    Math.Sin(2 * gmas) * (3.489437e-4 + 1.762783e-6 * jc) + Math.Sin(3 * gmas) * 5.044e-6;
                double oc = 4.090928e-1 - jc * (2.269655e-4 + jc * (2.8604e-9 - jc * 8.789672e-9)) +
                    4.468043e-5 * Math.Cos(2.18236 - 33.757041 * jc);
                double sd = Math.Asin(Math.Sin(oc) *
                    Math.Sin(gmls + seoc - 9.930923e-5 - 8.342674e-5 * Math.Sin(2.18236 - 33.757041 * jc)));
                double y = Math.Pow(Math.Tan(oc / 2), 2);
                double has = Math.Acos(-1.453808e-2 /
                    (Math.Cos(_latitude) * Math.Cos(sd)) - Math.Tan(_latitude) * Math.Tan(sd));
                double sn = .5 - _longitude / 360 - (229.183118 * (y * Math.Sin(2 * gmls) -
                    2 * eeo * Math.Sin(gmas) + 4 * eeo * y * Math.Sin(gmas) * Math.Cos(2 * gmls) -
                    .5 * y * y * Math.Sin(4 * gmls) - 1.25 * eeo * eeo * Math.Sin(2 * gmas))) / 1440 +
                    _timeZone.GetUtcOffset(value).Hours / 24.0;
                Sunrise = value.Date.AddDays(sn - has * 1.591549e-1);
                Sunset = value.Date.AddDays(sn + has * 1.591549e-1);
            }
        }
        public DateTime Sunrise
        {
            get; private set;
        }
        public DateTime Sunset
        {
            get; private set;
        }
    }
}
