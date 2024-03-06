using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Mapping;
using System;
using System.Diagnostics;
using System.IO;

namespace GEtools
{
    internal class OpenInGEcontext : Button
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


                // Create a KML file for the point
                SaveCoordinatesAsKml(mapPoint.Y, mapPoint.X);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private void SaveCoordinatesAsKml(double latitude, double longitude)
        {
            try
            {
                // Get the user's documents folder
                string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                // Define the relative path to the KML folder
                string kmlFolderName = "OpenInGE_kml_temp";

                // Combine the user's documents folder with the KML folder path
                string outputDir = Path.Combine(documentsFolder, kmlFolderName);

                // Check if the directory exists, if not, create it
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                // Define the KML file path
                string kmlFileName = $"Target.kml";
                string kmlFilePath = Path.Combine(outputDir, kmlFileName);

                // Write KML content to the file
                string kmlContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
                <kml xmlns=""http://www.opengis.net/kml/2.2"">
                  <Placemark>
                    <name>target</name>
                    <Point>
                      <coordinates>{longitude},{latitude}</coordinates>
                    </Point>
                  </Placemark>
                </kml>";

                File.WriteAllText(kmlFilePath, kmlContent);

                // Check if the file exists before attempting to open it
                if (File.Exists(kmlFilePath))
                {
                    // Use ProcessStartInfo to specify the default program associated with KML files
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = kmlFilePath;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                else
                {
                    // Handle error: File not found
                    Console.WriteLine($"Failed to create KML file: {kmlFilePath}");
                }
            }
            catch (Exception e)
            {
                // Handle error
                Console.WriteLine($"An error occurred: {e.Message}");
            }
        }
    }
}
