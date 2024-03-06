using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Mapping;
using System;
using System.Diagnostics;
using System.IO;

namespace GEtools
{
    internal class OpenInMapsContext : Button
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

                // Open Google Maps in the browser
                OpenGoogleMapsInBrowser(mapPoint.Y, mapPoint.X);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private void OpenGoogleMapsInBrowser(double latitude, double longitude)
        {
            try
            {
                // Generate the Google Maps URL with the provided coordinates
                string googleMapsUrl = $"https://www.google.com/maps?q={latitude},{longitude}";

                // Open the URL in the default web browser
                Process.Start(new ProcessStartInfo(googleMapsUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // Handle error
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }
    }
}
