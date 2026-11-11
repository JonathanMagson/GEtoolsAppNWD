using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GEtools.EarthWeb
{
    /// <summary>
    /// URL builders for Google Earth Web, plus the camera maths that goes with
    /// them. No ArcGIS dependency, so it can be unit tested on its own.
    /// </summary>
    internal static class GoogleUrls
    {
        public const string EarthWeb = "https://earth.google.com/web";

        /// <summary>Google Earth Web's default vertical field of view, in degrees.</summary>
        public const double DefaultFov = 35.0;

        /// <summary>Earth Web refuses a closer camera; the whole globe shows from the upper value.</summary>
        public const double MinRangeM = 50.0;
        public const double MaxRangeM = 30_000_000.0;

        /// <summary>Camera distance for OpenInGE: close enough to see the spot clearly.</summary>
        public const double PointRangeM = 1000.0;

        private const double EarthRadiusM = 6371008.8;

        /// <summary>Format a number without trailing zeros ("1.50000" -> "1.5"), culture-independent.</summary>
        internal static string Fmt(double value, int digits)
        {
            string text = value.ToString("F" + digits, CultureInfo.InvariantCulture);
            if (text.Contains('.'))
                text = text.TrimEnd('0').TrimEnd('.');
            return text == "" || text == "-0" ? "0" : text;
        }

        private static void CheckLatLon(double lat, double lon)
        {
            if (double.IsNaN(lat) || lat < -90.0 || lat > 90.0)
                throw new ArgumentOutOfRangeException(nameof(lat), $"latitude {lat} is outside -90..90");
            if (double.IsNaN(lon) || lon < -180.0 || lon > 180.0)
                throw new ArgumentOutOfRangeException(nameof(lon), $"longitude {lon} is outside -180..180");
        }

        /// <summary>
        /// Google Earth Web camera looking at lat, lon from rangeM metres:
        /// <c>/web/@lat,lon,&lt;alt&gt;a,&lt;range&gt;d,&lt;fov&gt;y,&lt;heading&gt;h,&lt;tilt&gt;t,0r</c>.
        /// </summary>
        public static string EarthViewUrl(double lat, double lon, double rangeM, double heading = 0.0,
                                          double tilt = 0.0, double fov = DefaultFov, double altitude = 0.0)
        {
            CheckLatLon(lat, lon);
            rangeM = Math.Min(Math.Max(rangeM, MinRangeM), MaxRangeM);
            heading = ((heading % 360.0) + 360.0) % 360.0;
            return $"{EarthWeb}/@{Fmt(lat, 7)},{Fmt(lon, 7)},{Fmt(altitude, 2)}a,{Fmt(rangeM, 2)}d," +
                   $"{Fmt(fov, 2)}y,{Fmt(heading, 2)}h,{Fmt(tilt, 2)}t,0r";
        }

        /// <summary>
        /// Google Earth Web looking straight down on a coordinate, for browsers
        /// the add-in can't draw its X in. Earth Web shows a pin for the search,
        /// and the camera part stops it opening wherever it was last left.
        /// </summary>
        public static string EarthPointUrl(double lat, double lon, double rangeM = PointRangeM)
        {
            CheckLatLon(lat, lon);
            string camera = EarthViewUrl(lat, lon, rangeM).Substring(EarthWeb.Length + 1);
            return $"{EarthWeb}/search/{Fmt(lat, 7)},{Fmt(lon, 7)}/{camera}";
        }

        /// <summary>Camera distance at which groundHeightM fills the vertical view.</summary>
        public static double RangeForGroundHeight(double groundHeightM, double fov = DefaultFov)
        {
            double half = fov * Math.PI / 180.0 / 2.0;
            return groundHeightM / (2.0 * Math.Tan(half));
        }

        /// <summary>Great-circle distance in metres.</summary>
        public static double GroundDistanceM(double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = lat1 * Math.PI / 180.0, p2 = lat2 * Math.PI / 180.0;
            double dp = p2 - p1, dl = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Pow(Math.Sin(dp / 2), 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Pow(Math.Sin(dl / 2), 2);
            return 2 * EarthRadiusM * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
        }

        private static readonly Regex CameraRe = new Regex(@"/@(-?[\d.]+),(-?[\d.]+),[^/?#]*?(-?[\d.]+)d", RegexOptions.Compiled);

        /// <summary>True when an Earth Web address carries a camera (it does once the globe has loaded).</summary>
        public static bool HasCamera(string url)
        {
            var m = CameraRe.Match(url ?? "");
            return m.Success &&
                   double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) &&
                   d > 0;
        }

        // Moves the Google Earth camera without reloading the app: Earth Web is a
        // single-page app that follows its own history, so push the new "@..." URL
        // and announce it the way the browser's back/forward buttons would. Falls
        // back to a normal page load when Earth Web isn't the page that's showing.
        private const string SoftNavTemplate = @"(function (url) {
  if (location.host !== 'earth.google.com' || location.pathname.indexOf('/web') !== 0) {
    location.href = url; return;
  }
  history.pushState(history.state, '', url);
  window.dispatchEvent(new PopStateEvent('popstate', {state: history.state}));
})(__URL__);";

        /// <summary>JavaScript that points an already-open Earth Web page at url.</summary>
        public static string SoftNavJs(string url) => SoftNavTemplate.Replace("__URL__", JsonSerializer.Serialize(url));
    }
}
