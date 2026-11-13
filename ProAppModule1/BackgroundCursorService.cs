using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Collections.Generic;
using System.Windows.Threading;

namespace GEtools
{
    // Tracks the map pointer without changing the active editing tool.
    internal static class BackgroundCursorService
    {
        private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AP2GE");
        private static readonly string Marker = Path.Combine(Folder, "AP2GE_Cursor.kml");
        private static readonly string Link = Path.Combine(Folder, "AP2GE_CursorNetworkLink.kml");
        private static readonly string CursorIcon = Path.Combine(Folder, "AP2GE_CursorIcon.png");
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly SemaphoreSlim Writer = new SemaphoreSlim(1, 1);
        private static DispatcherTimer _timer;
        private static bool _running;
        private static long _sequence;
        private static DateTime _lastSample = DateTime.MinValue;
        private const int MinimumMs = 50;
        private static DateTime _lastRectScan = DateTime.MinValue;
        private static Rect? _cachedMapRect;

        [StructLayout(LayoutKind.Sequential)]
        private struct WinPoint { public int X; public int Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out WinPoint point);
        [StructLayout(LayoutKind.Sequential)]
        private struct WinRect { public int Left, Top, Right, Bottom; }
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out WinRect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref WinPoint point);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);

        public static bool IsRunning => _running;
        public static void Toggle() { if (_running) Stop(); else Start(); }
        public static void Stop()
        {
            if (!_running) return;
            _running = false;
            Interlocked.Increment(ref _sequence);
            if (_timer != null) { _timer.Stop(); _timer.Tick -= OnTick; _timer = null; }
        }

        private static void Start()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                WriteCursorIcon();
                WriteLink();
                File.WriteAllText(Marker, EmptyKml, Utf8);
                _running = true;
                _lastSample = DateTime.MinValue;
                _cachedMapRect = null;
                _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
                _timer.Tick += OnTick;
                _timer.Start();
                Process.Start(new ProcessStartInfo(Link) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Stop();
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show("Cannot start Sync Cursor: " + ex.Message);
            }
        }

        private static void OnTick(object sender, EventArgs e)
        {
            if (!_running || (DateTime.UtcNow - _lastSample).TotalMilliseconds < MinimumMs) return;
            _lastSample = DateTime.UtcNow;
            try
            {
                var view = MapView.Active;
                if (view == null) { return; }
                if (!GetCursorPos(out var screen)) { return; }
                var size = GetMapViewSize(view);
                if (size == null) { return; }
                if ((DateTime.UtcNow - _lastRectScan).TotalSeconds > 2 || _cachedMapRect == null)
                {
                    _cachedMapRect = FindMapRectangle(size.Value, screen);
                    _lastRectScan = DateTime.UtcNow;
                }
                var rect = _cachedMapRect;
                if (rect == null) { return; }
                if (!rect.Value.Contains(new Point(screen.X, screen.Y)))
                {
                    // Rescan when the pointer moves out of the previously detected view.
                    _cachedMapRect = null;

                    return;
                }
                // ClientToMap expects map-view DIPs, not physical Windows pixels.
                var position = new Point(
                    (screen.X - rect.Value.Left) * size.Value.Width / rect.Value.Width,
                    (screen.Y - rect.Value.Top) * size.Value.Height / rect.Value.Height);

                long seq = Interlocked.Increment(ref _sequence);
                _ = UpdateAsync(view, position, seq);
            }
            catch (Exception ex) { Debug.WriteLine("Sync Cursor: " + ex); }
        }

        private static Size? GetMapViewSize(MapView view)
        {
            // Reflection keeps compatibility with minor SDK API differences.
            var method = view.GetType().GetMethod("GetViewSize", Type.EmptyTypes);
            if (method == null) return null;
            var result = method.Invoke(view, null);
            if (result is Size s) return s;
            if (result == null) return null;
            var t = result.GetType();
            var w = t.GetProperty("Width")?.GetValue(result);
            var h = t.GetProperty("Height")?.GetValue(result);
            if (w == null || h == null) return null;
            return new Size(Convert.ToDouble(w), Convert.ToDouble(h));
        }

        // Locate a screen rectangle corresponding to the *active* map view.
        // We never rely on Mouse.DirectlyOver: ArcGIS Pro renders the map using a native surface.
        // Reject ambiguous candidates rather than publishing incorrect geographic coordinates.
        private static Rect? FindMapRectangle(Size expected, WinPoint pointer)
        {
            var candidates = new List<Rect>();
            var main = Process.GetCurrentProcess().MainWindowHandle;
            if (main != IntPtr.Zero)
            {
                EnumChildWindows(main, (hwnd, _) =>
                {
                    if (!IsWindowVisible(hwnd) || !GetClientRect(hwnd, out var r)) return true;
                    var origin = new WinPoint();
                    if (!ClientToScreen(hwnd, ref origin)) return true;
                    double w = r.Right - r.Left, h = r.Bottom - r.Top;
                    if (w < 200 || h < 200) return true;
                    // Account for display scaling: compare aspect ratios and approximate dimensions.
                    double ratio = w / h, wanted = expected.Width / expected.Height;
                    if (Math.Abs(ratio - wanted) < 0.035 &&
                        w / expected.Width > 0.75 && w / expected.Width < 2.6)
                        candidates.Add(new Rect(origin.X, origin.Y, w, h));
                    return true;
                }, IntPtr.Zero);
            }
            // Native map surface may be windowless. Search the *entire* WPF visual tree,
            // not just elements under the mouse pointer, for a same-sized map viewport.
            foreach (Window window in Application.Current.Windows)
            {
                if (!window.IsVisible) continue;
                ScanVisual(window, expected, candidates);
            }
            // Deduplicate nested WPF containers that share the same screen bounds.
            var unique = new List<Rect>();
            foreach (var rect in candidates)
            {
                if (!unique.Exists(x => Math.Abs(x.Left - rect.Left) < 8 &&
                                        Math.Abs(x.Top - rect.Top) < 8 &&
                                        Math.Abs(x.Width - rect.Width) < 8 &&
                                        Math.Abs(x.Height - rect.Height) < 8)) unique.Add(rect);
            }
            var underPointer = unique.FindAll(x => x.Contains(new Point(pointer.X, pointer.Y)));
            if (underPointer.Count == 1)
            {
                return underPointer[0];
            }
            return null;
        }

        private static void ScanVisual(DependencyObject root, Size expected, List<Rect> candidates)
        {
            if (root is FrameworkElement fe && fe.IsVisible && fe.ActualWidth > 200 && fe.ActualHeight > 200)
            {
                double error = Math.Abs(fe.ActualWidth - expected.Width) + Math.Abs(fe.ActualHeight - expected.Height);
                if (error < 8)
                {
                    try
                    {
                        var p = fe.PointToScreen(new Point(0, 0));
                        var q = fe.PointToScreen(new Point(fe.ActualWidth, fe.ActualHeight));
                        candidates.Add(new Rect(p, q));
                    }
                    catch { }
                }
            }
            int count;
            try { count = VisualTreeHelper.GetChildrenCount(root); }
            catch { return; }
            for (int i = 0; i < count; i++)
            {
                try { ScanVisual(VisualTreeHelper.GetChild(root, i), expected, candidates); }
                catch { }
            }
        }
        private static async Task UpdateAsync(MapView view, Point pt, long seq)
        {
            try
            {
                var point = await QueuedTask.Run(() =>
                {
                    var mapPoint = view.ClientToMap(pt);
                    return mapPoint == null ? null : GeometryEngine.Instance.Project(mapPoint, SpatialReferences.WGS84) as MapPoint;
                });
                if (point == null || !_running || seq != Interlocked.Read(ref _sequence)) return;
                string kml = string.Format(CultureInfo.InvariantCulture,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document><Placemark><Style><IconStyle><scale>1.0</scale><hotSpot x=\"0.5\" y=\"0.5\" xunits=\"fraction\" yunits=\"fraction\"/><Icon><href>AP2GE_CursorIcon.png</href></Icon></IconStyle><LabelStyle><scale>0</scale></LabelStyle></Style><Point><coordinates>{0:R},{1:R},0</coordinates></Point></Placemark></Document></kml>", point.X, point.Y);
                await Writer.WaitAsync();
                try { if (_running && seq == Interlocked.Read(ref _sequence)) await File.WriteAllTextAsync(Marker, kml, Utf8); }
                finally { Writer.Release(); }
            }
            catch (Exception ex) { Debug.WriteLine("Sync Cursor: " + ex); }
        }
        private static void WriteCursorIcon()
        {
            using var stream = typeof(BackgroundCursorService).Assembly.GetManifestResourceStream("GEtools.Images.cursor-marker.png")
                ?? throw new InvalidOperationException("Embedded cursor icon is missing.");
            using var file = File.Create(CursorIcon);
            stream.CopyTo(file);
        }

        private const string EmptyKml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document/></kml>";
        private static void WriteLink()
        {
            const string kml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document><name>ArcGIS Pro Cursor</name><NetworkLink><name>Cursor position</name><flyToView>0</flyToView><Link><href>AP2GE_Cursor.kml</href><refreshMode>onInterval</refreshMode><refreshInterval>0.1</refreshInterval></Link></NetworkLink></Document></kml>";
            File.WriteAllText(Link, kml, Utf8);
        }
    }
}
