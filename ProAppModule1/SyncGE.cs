using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Core.Internal.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GEtools
{
    internal class SyncGE : Button
    {
        // Location of save file and file names
        static private String _saveDirectory;
        static private String _currentViewFileName = "AP2GE_CurrentView.kml";
        static private String _networkLinkFileName = "AP2GE_NetworkLink.kml";

        // Default Location Is Batman Building In Japan
        private String _latitude = "26.357896";
        private String _longitude = "127.783809";
        private String _altitude = "100";
        private String _heading = "0";
        private String _tilt = "0";

        #region Button 

        // Which Google Earth this sync session drives (fixed when it starts)
        private EarthMode _activeMode;

        // Google Earth Web: wait for the map to settle before following it
        private CancellationTokenSource _webDebounce;

        private SyncGE()
        {
            _saveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"AP2GE\");

            // Switching between Google Earth Web and Pro ends the current sync.
            EarthSettings.ModeChanged += mode => EarthWebActions.RunOnUi(() =>
            {
                if (this.IsChecked && mode != _activeMode)
                    Deactivate();
            });
        }

        protected override void OnClick()
        {
            if (this.IsChecked)
            {
                Deactivate();
            }
            else
            {
                Activate();
            }
        }

        #endregion

        #region Methods

        private void Activate()
        {
            _activeMode = EarthSettings.Mode;
            MapViewCameraChangedEvent.Subscribe(MapViewCameraCanged, false);
            this.IsChecked = true;
            this.Caption = "Deactivate";

            if (_activeMode == EarthMode.Web)
            {
                _ = StartWebSyncAsync();
            }
            else
            {
                WriteCurrentView();
                WriteNetworkLink();
            }
        }

        private void Deactivate()
        {
            MapViewCameraChangedEvent.Unsubscribe(MapViewCameraCanged);
            _webDebounce?.Cancel();
            this.IsChecked = false;
            this.Caption = "Activate";

            if (_activeMode == EarthMode.Web)
            {
                // The Google Earth Web window stays open; it just stops following.
                return;
            }

            try
            {
                string currentViewFile = Path.Combine(_saveDirectory, _currentViewFileName);

                if (File.Exists(currentViewFile))
                {
                    File.Delete(currentViewFile);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Deactivation Failed...\n" + ex.Message);
            }
        }

        private void MapViewCameraCanged(MapViewCameraChangedEventArgs args)
        {
            MapView mapView = args.MapView;

            if (mapView != null)
            {
                if (_activeMode == EarthMode.Web)
                    QueueWebSync(mapView);
                else
                    SyncViews(mapView);
            }
        }

        #region Google Earth Web

        private async Task StartWebSyncAsync()
        {
            var mapView = MapView.Active;
            if (mapView == null)
            {
                MessageBox.Show("Open a map view first.", "SyncToGE");
                Deactivate();
                return;
            }
            await SyncWebAsync(mapView, launch: true);
        }

        // The camera changes many times a second while panning; only move
        // Google Earth once the map has been still for the sync delay.
        private void QueueWebSync(MapView mapView)
        {
            _webDebounce?.Cancel();
            var cts = _webDebounce = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(EarthSettings.SyncDelayMs, cts.Token);
                }
                catch (TaskCanceledException)
                {
                    return; // the map moved again
                }
                await SyncWebAsync(mapView, launch: false);
            });
        }

        private async Task SyncWebAsync(MapView mapView, bool launch)
        {
            EarthWeb.EarthWebResult result;
            try
            {
                result = await EarthWebActions.SyncViewAsync(mapView, launch);
            }
            catch (Exception ex)
            {
                result = EarthWeb.EarthWebResult.Failed;
                EarthWebActions.Notify("SyncToGE", ex.Message);
            }
            if (result == EarthWeb.EarthWebResult.Ok)
                return;

            EarthWebActions.RunOnUi(() =>
            {
                if (!this.IsChecked || _activeMode != EarthMode.Web)
                    return;
                Deactivate();
                if (result == EarthWeb.EarthWebResult.Closed)
                    EarthWebActions.Notify("SyncToGE", "SyncToGE turned off: the Google Earth window was closed.");
                else if (result == EarthWeb.EarthWebResult.Failed)
                    EarthWebActions.Notify("SyncToGE", "SyncToGE turned off: " + EarthWeb.EarthWebWindow.Instance.LastError);
            });
        }

        #endregion

        private async void SyncViews(MapView mapView)
        {
            // Get Altitude *Google Earth Range
            MapPoint lowerLeftPoint = null;
            MapPoint upperRightPoint = null;

            await QueuedTask.Run(() =>
            {
                lowerLeftPoint = MapPointBuilder.CreateMapPoint(mapView.Extent.XMin, mapView.Extent.YMin, mapView.Extent.SpatialReference);
                upperRightPoint = MapPointBuilder.CreateMapPoint(mapView.Extent.XMax, mapView.Extent.YMax, mapView.Extent.SpatialReference);
            });

            double latXmin, latXmax, longYmin, longYmax, diagonal;

            Tuple<double, double> tupleResult = null;

            tupleResult = await PointToLatLong(lowerLeftPoint);
            longYmin = tupleResult.Item1;
            latXmin = tupleResult.Item2;

            tupleResult = await PointToLatLong(upperRightPoint);
            longYmax = tupleResult.Item1;
            latXmax = tupleResult.Item2;

            diagonal = Math.Round(DistanceBetweenPoints(latXmin, longYmin, latXmax, longYmax, 'K') * 1000, 2); // 1km * 1000m

            double altitude = diagonal * Math.Sqrt(3) * 0.5;

            _altitude = altitude.ToString();



            // Longitude, Latitude
            MapPoint point = null;

            await QueuedTask.Run(() =>
            {
                Coordinate2D coordinate = new Coordinate2D(mapView.Camera.X, mapView.Camera.Y);
                //Coordinate coordinate = new Coordinate(((map.Extent.XMax + map.Extent.XMin) / 2), ((map.Extent.YMax + map.Extent.YMin) / 2));
                MapPointBuilder pointBuilder = new MapPointBuilder(coordinate, mapView.Extent.SpatialReference);
                point = pointBuilder.ToGeometry();
            });

            double latitude;
            double longitude;

            var result = await PointToLatLong(point);
            longitude = result.Item1;
            latitude = result.Item2;

            _latitude = Convert.ToString(Math.Round(latitude, 5));
            _longitude = Convert.ToString(Math.Round(longitude, 5));

            // Altitude
            // Arcmap Heading 0, 90, 180, -90:       Point of Reference is the Camera
            // Google Earth Heading 0, 90, 180, 270: Point of Reference is the Bearing
            double heading = mapView.Camera.Heading;
            heading = 360 - (heading < 0 ? (heading + 360) : heading);  // Flip Point of Reference - Flip to 360 
            _heading = heading.ToString();


            // Tilt
            double tilt = (mapView.Camera.Pitch + 90); // Convert To Google Earth Formatting
            _tilt = tilt.ToString();


            WriteCurrentView();
        }

        private void WriteNetworkLink()
        {
            if (!System.IO.Directory.Exists(_saveDirectory))
            {
                System.IO.Directory.CreateDirectory(_saveDirectory);
            }

            string networklinkfile = Path.Combine(_saveDirectory, _networkLinkFileName);

            using (TextWriter tw = new StreamWriter(networklinkfile))
            {
                tw.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                tw.WriteLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
                tw.WriteLine("<Folder>");
                tw.WriteLine("<open>1</open>");
                tw.WriteLine("<name>ArcPro to Google Earth Sync</name>");
                tw.WriteLine("<NetworkLink>");
                tw.WriteLine("<name>" + "ArcGIS Pro Map" + "</name>");
                tw.WriteLine("<flyToView>1</flyToView>");
                tw.WriteLine("<Link>");
                tw.WriteLine("<href>" + _currentViewFileName + "</href>");
                tw.WriteLine("<refreshMode>onInterval</refreshMode>");
                tw.WriteLine("<refreshInterval>0.300000</refreshInterval>");
                tw.WriteLine("</Link>");
                tw.WriteLine("</NetworkLink>");
                tw.WriteLine("</Folder>");
                tw.WriteLine("</kml>");
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = networklinkfile;
            psi.UseShellExecute = true;
            Process.Start(psi);

        }

        private void WriteCurrentView()
        {
            if (!System.IO.Directory.Exists(_saveDirectory))
            {
                System.IO.Directory.CreateDirectory(_saveDirectory);
            }

            string currentviewfile = Path.Combine(_saveDirectory, _currentViewFileName);

            using (TextWriter tw = new StreamWriter(currentviewfile))
            {

                tw.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                tw.WriteLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
                tw.WriteLine("<NetworkLinkControl>");
                tw.WriteLine("<LookAt>");
                tw.WriteLine("<longitude>" + _longitude + "</longitude>");
                tw.WriteLine("<latitude>" + _latitude + "</latitude>");
                tw.WriteLine("<altitudeMode>relativeToGround</altitudeMode>");
                // tw.WriteLine("<altitude> + " + _gpsAltitude + "</altitude>");
                tw.WriteLine("<heading>" + _heading + "</heading>");
                tw.WriteLine("<tilt>" + _tilt + "</tilt>");
                tw.WriteLine("<range>" + _altitude + "</range>");
                tw.WriteLine("</LookAt>");
                tw.WriteLine("</NetworkLinkControl>");
                tw.WriteLine("</kml>");
            }
        }

        private async Task<Tuple<double, double>> PointToLatLong(MapPoint point)
        {

            try
            {
                SpatialReference spatialReference = null;
                await QueuedTask.Run(() =>
                {
                    SpatialReferenceBuilder spatialReferenceBuilder = new SpatialReferenceBuilder(SpatialReferences.WGS84);

                    spatialReferenceBuilder.FalseX = -180;
                    spatialReferenceBuilder.FalseY = -90;
                    spatialReferenceBuilder.XYScale = 1000000;

                    spatialReference = spatialReferenceBuilder.ToSpatialReference();
                });

                Geometry geometry = (Geometry)point;

                MapPoint newPoint = null;

                await QueuedTask.Run(() =>
                {
                    newPoint = GeometryEngine.Instance.Project(geometry, spatialReference) as MapPoint;
                });

                return new Tuple<double, double>(newPoint.X, newPoint.Y);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return null;
        }

        private double DistanceBetweenPoints(double lat1, double lon1, double lat2, double lon2, char unit)
        {
            //'M' is statute miles
            //'K' is kilometers (default)
            //'N' is nautical miles  
            double theta = lon1 - lon2;
            double dist = Math.Sin(ConvertDegrees2Radians(lat1)) * Math.Sin(ConvertDegrees2Radians(lat2)) + Math.Cos(ConvertDegrees2Radians(lat1)) * Math.Cos(ConvertDegrees2Radians(lat2)) * Math.Cos(ConvertDegrees2Radians(theta));
            dist = Math.Acos(dist);
            dist = ConvertRadians2Degrees(dist);
            dist = dist * 60 * 1.1515;
            if (unit == 'K')
            {
                dist = dist * 1.609344;
            }
            else if (unit == 'N')
            {
                dist = dist * 0.8684;
            }
            return (dist);
        }

        private double ConvertDegrees2Radians(Double deg)
        {
            return (deg * Math.PI / 180.0);
        }

        private double ConvertRadians2Degrees(Double rad)
        {
            return (rad / Math.PI * 180.0);
        }

        #endregion
    }
}