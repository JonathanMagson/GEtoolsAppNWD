using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GEtools
{
    /// <summary>Which Google Earth the GEtools buttons use.</summary>
    internal enum EarthMode
    {
        /// <summary>Google Earth Pro, the desktop app (KML files).</summary>
        Desktop,
        /// <summary>Google Earth Web, in a Chrome or Edge window the add-in drives.</summary>
        Web,
    }

    /// <summary>
    /// GEtools options, saved per Windows user in
    /// %LocalAppData%\GEtools\settings.json so they apply to every project.
    /// </summary>
    internal static class EarthSettings
    {
        private sealed class Data
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public EarthMode Mode { get; set; } = EarthMode.Desktop;

            /// <summary>"auto" (Chrome, else Edge), "chrome", "edge" or "custom".</summary>
            public string Browser { get; set; } = "auto";

            /// <summary>Browser to use when Browser is "custom".</summary>
            public string BrowserPath { get; set; } = "";

            /// <summary>Bring the Google Earth window to the front for OpenInGE and LayerToGE.</summary>
            public bool RaiseEarthWindow { get; set; } = true;

            /// <summary>Move Google Earth Web without reloading it while syncing.</summary>
            public bool FastSync { get; set; } = true;

            /// <summary>How long the map must stay still before Google Earth Web follows.</summary>
            public int SyncDelayMs { get; set; } = 400;

            /// <summary>OpenInGE camera distance in Google Earth Web.</summary>
            public double PointRangeM { get; set; } = 1000.0;
        }

        public static string Folder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GEtools");

        private static string FilePath => Path.Combine(Folder, "settings.json");

        private static readonly object Sync = new object();
        private static Data _data = Load();

        /// <summary>Raised (on the caller's thread) after the mode changes.</summary>
        public static event Action<EarthMode> ModeChanged;

        public static EarthMode Mode
        {
            get { lock (Sync) return _data.Mode; }
            set
            {
                lock (Sync)
                {
                    if (_data.Mode == value)
                        return;
                    _data.Mode = value;
                    Save();
                }
                ModeChanged?.Invoke(value);
            }
        }

        public static string Browser { get { lock (Sync) return _data.Browser; } }
        public static string BrowserPath { get { lock (Sync) return _data.BrowserPath; } }
        public static bool RaiseEarthWindow { get { lock (Sync) return _data.RaiseEarthWindow; } }
        public static bool FastSync { get { lock (Sync) return _data.FastSync; } }
        public static int SyncDelayMs { get { lock (Sync) return Math.Max(0, _data.SyncDelayMs); } }
        public static double PointRangeM { get { lock (Sync) return _data.PointRangeM; } }

        private static Data Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                // Unreadable settings: fall back to the defaults.
            }
            return new Data();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Not fatal: the choice still applies until ArcGIS Pro closes.
            }
        }
    }
}
