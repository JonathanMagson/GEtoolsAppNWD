using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using GEtools.EarthWeb;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MessageBox = ArcGIS.Desktop.Framework.Dialogs.MessageBox;

namespace GEtools
{
    /// <summary>A Google Earth camera: where it looks, from how far, and its heading/tilt.</summary>
    internal sealed class EarthCamera
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double RangeM { get; set; }
        public double Heading { get; set; }
        public double Tilt { get; set; }

        public string Url => GoogleUrls.EarthViewUrl(Lat, Lon, RangeM, Heading, Tilt);
    }

    /// <summary>
    /// What the GEtools buttons do in Google Earth Web mode. The Google Earth
    /// Pro (desktop) behaviour stays in the buttons themselves.
    /// </summary>
    internal static class EarthWebActions
    {
        private static EarthWebWindow Window => EarthWebWindow.Instance;

        // ---- geometry (call these inside QueuedTask.Run) ----------------------

        /// <summary>A point projected to WGS84, or null if it can't be.</summary>
        public static MapPoint ToWgs84(MapPoint point)
        {
            if (point == null)
                return null;
            var p = GeometryEngine.Instance.Project(point, SpatialReferences.WGS84) as MapPoint;
            if (p == null || p.IsEmpty || double.IsNaN(p.X) || double.IsNaN(p.Y) ||
                p.Y < -90 || p.Y > 90 || p.X < -180 || p.X > 180)
                return null;
            return p;
        }

        /// <summary>
        /// Google Earth camera matching a map view: centred on the view, far
        /// enough back that the view's height fills Google Earth, same rotation.
        /// Null when the view can't be converted to latitude/longitude.
        /// </summary>
        public static EarthCamera CameraFor(MapView mapView)
        {
            var extent = mapView?.Extent;
            if (extent == null || extent.IsEmpty)
                return null;
            var sr = extent.SpatialReference;
            var centre = ToWgs84(MapPointBuilderEx.CreateMapPoint(extent.Center.X, extent.Center.Y, sr));
            if (centre == null)
                return null;

            var top = ToWgs84(MapPointBuilderEx.CreateMapPoint(extent.Center.X, extent.YMax, sr));
            var bottom = ToWgs84(MapPointBuilderEx.CreateMapPoint(extent.Center.X, extent.YMin, sr));
            double groundHeight = top != null && bottom != null
                ? GoogleUrls.GroundDistanceM(top.Y, top.X, bottom.Y, bottom.X)
                : 20_000_000.0; // zoomed out past the poles: show the globe

            // ArcGIS Pro's heading points the other way round from Google Earth's
            // (same conversion the Google Earth Pro sync has always used).
            var camera = mapView.Camera;
            double heading = camera.Heading < 0 ? camera.Heading + 360 : camera.Heading;
            heading = (360 - heading) % 360;

            // 2D maps look straight down; in a 3D scene, Pro's pitch is -90 when
            // looking down and Google Earth's tilt is 0.
            double tilt = mapView.ViewingMode == MapViewingMode.Map
                ? 0.0
                : Math.Max(0.0, Math.Min(89.0, camera.Pitch + 90.0));

            return new EarthCamera
            {
                Lat = centre.Y,
                Lon = centre.X,
                RangeM = GoogleUrls.RangeForGroundHeight(Math.Max(groundHeight, 1.0)),
                Heading = heading,
                Tilt = tilt,
            };
        }

        /// <summary>Camera framing an envelope (padded 20%, never closer than 500 m across).</summary>
        public static EarthCamera CameraForEnvelope(Envelope envelope)
        {
            if (envelope == null || envelope.IsEmpty)
                return null;
            var sr = envelope.SpatialReference;
            var c = envelope.Center;
            var centre = ToWgs84(MapPointBuilderEx.CreateMapPoint(c.X, c.Y, sr));
            if (centre == null)
                return null;

            double Dist(double x1, double y1, double x2, double y2)
            {
                var a = ToWgs84(MapPointBuilderEx.CreateMapPoint(x1, y1, sr));
                var b = ToWgs84(MapPointBuilderEx.CreateMapPoint(x2, y2, sr));
                return a == null || b == null ? 0.0 : GoogleUrls.GroundDistanceM(a.Y, a.X, b.Y, b.X);
            }

            double span = Math.Max(Math.Max(Dist(c.X, envelope.YMin, c.X, envelope.YMax),
                                            Dist(envelope.XMin, c.Y, envelope.XMax, c.Y)), 500.0) * 1.2;
            return new EarthCamera { Lat = centre.Y, Lon = centre.X, RangeM = GoogleUrls.RangeForGroundHeight(span) };
        }

        // ---- helpers -----------------------------------------------------------

        public static void OpenInDefaultBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                RunOnUi(() => MessageBox.Show($"Couldn't open a web browser:\n{ex.Message}", "GEtools"));
            }
        }

        public static void Notify(string title, string message)
        {
            RunOnUi(() => FrameworkApplication.AddNotification(new Notification { Title = title, Message = message }));
        }

        public static void RunOnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        private const string NoBrowserMessage =
            "Google Earth Web works best in Google Chrome or Microsoft Edge, and neither was found, " +
            "so it opened in your default browser instead.";

        // ---- OpenInGE ----------------------------------------------------------

        /// <summary>
        /// OpenInGE in web mode: show the spot in Google Earth Web, marked with
        /// a red X. Uses the add-in's Chrome/Edge window, because only a page the
        /// add-in controls can carry the X.
        /// </summary>
        public static async Task OpenPointAsync(double lat, double lon)
        {
            await Window.SetMarkersAsync(EarthPageScripts.MarkerJs(new[] { (lat, lon) }));
            double range = EarthSettings.PointRangeM;
            var result = await Window.NavigateAsync(GoogleUrls.EarthViewUrl(lat, lon, range), EarthSettings.RaiseEarthWindow);
            switch (result)
            {
                case EarthWebResult.Ok:
                    return;
                case EarthWebResult.NoBrowser:
                    OpenInDefaultBrowser(GoogleUrls.EarthPointUrl(lat, lon, range));
                    Notify("GEtools", NoBrowserMessage);
                    return;
                default:
                    OpenInDefaultBrowser(GoogleUrls.EarthPointUrl(lat, lon, range));
                    Notify("GEtools", $"Couldn't use the Google Earth window ({Window.LastError}). " +
                                      "Opened your default browser instead.");
                    return;
            }
        }

        // ---- SyncToGE ----------------------------------------------------------

        /// <summary>
        /// Point Google Earth Web at the map view. With launch, starts the
        /// window if needed (turning sync on); without, reports Closed if the
        /// user has closed it (sync then turns off).
        /// </summary>
        public static async Task<EarthWebResult> SyncViewAsync(MapView mapView, bool launch)
        {
            var camera = await QueuedTask.Run(() => CameraFor(mapView));
            if (camera == null)
                return EarthWebResult.Ok; // e.g. a view with no projection: nothing to show
            var result = await Window.NavigateAsync(camera.Url, bringToFront: launch && EarthSettings.RaiseEarthWindow,
                                                    launchIfNeeded: launch);
            if (result == EarthWebResult.NoBrowser)
            {
                OpenInDefaultBrowser(camera.Url);
                Notify("GEtools", "Live sync with Google Earth Web needs Google Chrome or Microsoft Edge. " +
                                  "Opened the current view in your default browser instead.");
            }
            return result;
        }

        // ---- LayerToGE ---------------------------------------------------------

        public static string KmlFolder()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = Path.Combine(docs, "OpenInGE_kml_temp");
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>
        /// LayerToGE in web mode: export the layer selected in Contents (or its
        /// selected features) to KMZ, then load it into Google Earth Web by
        /// dropping it on the add-in's window, as if dragged there from Explorer.
        /// If that isn't possible, the file path is copied for importing by hand.
        /// </summary>
        public static async Task SendLayerAsync()
        {
            var mapView = MapView.Active;
            var layer = mapView?.GetSelectedLayers().OfType<FeatureLayer>().FirstOrDefault();
            if (layer == null)
            {
                MessageBox.Show("Select a feature layer in the Contents pane first.", "LayerToGE");
                return;
            }

            var (selectedOnly, camera) = await QueuedTask.Run(() =>
            {
                bool sel;
                using (var selection = layer.GetSelection())
                    sel = selection.GetCount() > 0;
                return (sel, CameraForEnvelope(layer.QueryExtent(sel)));
            });
            string what = (selectedOnly ? "selected features of " : "") + $"'{layer.Name}'";

            string folder = KmlFolder();
            foreach (string old in Directory.GetFiles(folder, "*.km?"))
            {
                try { File.Delete(old); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { /* still open somewhere */ }
            }
            string safeName = Regex.Replace(layer.Name, @"[^\w\-]+", "_").Trim('_');
            string path = Path.Combine(folder, $"{(safeName.Length > 0 ? safeName : "layer")}_{DateTime.Now:yyyyMMdd_HHmmss}.kmz");

            // Layer To KML honours the layer's selection, like the desktop tool.
            var args = Geoprocessing.MakeValueArray(layer, path, 0, "NO_COMPOSITE", "", "", "", "CLAMPED_TO_GROUND");
            var gp = await Geoprocessing.ExecuteToolAsync("conversion.LayerToKML", args, null, null, null,
                                                          GPExecuteToolFlags.None);
            if (gp.IsFailed || !File.Exists(path))
            {
                string errors = string.Join("\n", gp.ErrorMessages.Select(m => m.Text));
                MessageBox.Show($"Couldn't export {what} to KML.\n{errors}", "LayerToGE");
                return;
            }

            string url = camera?.Url ?? GoogleUrls.EarthWeb;
            var result = await Window.NavigateAsync(url, EarthSettings.RaiseEarthWindow);
            if (result != EarthWebResult.Ok)
            {
                OpenInDefaultBrowser(url);
                ManualImport(path, $"Exported {what}.");
                return;
            }

            Notify("LayerToGE", $"Sending {what} to Google Earth…");
            if (await Window.DropWhenReadyAsync(path, TimeSpan.FromSeconds(40)))
                Notify("LayerToGE", $"Sent {what} to Google Earth. If it doesn't appear, import it by hand: " +
                                    ImportHint(path));
            else
                ManualImport(path, $"Google Earth didn't finish loading, so {what} wasn't added automatically.");
        }

        private static string ImportHint(string path) =>
            "in Google Earth Web choose Projects > New project > Import KML file from computer, " +
            $"and pick {path} (the path is on the clipboard).";

        private static void ManualImport(string path, string lead)
        {
            RunOnUi(() =>
            {
                try { System.Windows.Clipboard.SetText(path); }
                catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy */ }
                MessageBox.Show(lead + " To add it, " + ImportHint(path), "LayerToGE");
            });
        }
    }
}
