using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Mapping;
using System;
using System.Diagnostics;

namespace GEtools
{
    internal class OpenInStreetContext : Button
    {
        protected override void OnClick()
        {
            try
            {
                // Get the current map view
                var mapView = MapView.Active;

                if (mapView == null)
                {
                    Console.WriteLine("No active map view found.");
                    return;
                }

                // Get the extent of the map view
                var extent = mapView.Extent;

                // Get the center of the extent (which represents the point where the context menu was opened)
                var mapPoint = extent.Center;

                // Print the map coordinates
                Console.WriteLine($"Map Coordinates: X={mapPoint.X}, Y={mapPoint.Y}");

                // Open Google Street View in the browser
                OpenGoogleStreetViewInBrowser(mapPoint.Y, mapPoint.X);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private void OpenGoogleStreetViewInBrowser(double latitude, double longitude)
        {
            try
            {
                // Generate the Google Street View URL with the provided coordinates
                string googleStreetViewUrl = $"https://www.google.com/maps/@?api=1&map_action=pano&viewpoint={latitude},{longitude}";

                // Open the URL in the default web browser
                Process.Start(new ProcessStartInfo(googleStreetViewUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // Handle error
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }
    }
}
