using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Framework;

namespace GEtools
{
    internal class OpenInGE : MapTool
    {

        public OpenInGE()
        {
            OverlayControlID = "MapToolWithOverlayControl_EmbeddableControl";
            OverlayControlCanResize = true;
            OverlayControlPositionRatio = new System.Windows.Point(0, 0.95); // bottom left
        }

        protected override void OnToolMouseDown(MapViewMouseButtonEventArgs e)

        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                e.Handled = true;
        }

        protected override async Task HandleMouseDownAsync(MapViewMouseButtonEventArgs e)
        {
            var mapView = ActiveMapView;
            var coords = await QueuedTask.Run(() => EarthWebActions.ToWgs84(mapView.ClientToMap(e.ClientPoint)));
            if (coords == null) return;

            if (EarthSettings.Mode == EarthMode.Web)
            {
                // Google Earth Web, marked with a red X
                await EarthWebActions.OpenPointAsync(coords.Y, coords.X);
            }
            else
            {
                // Google Earth Pro: save the coordinates as a KML file and open it
                SaveCoordinatesAsKml(coords.Y, coords.X);
            }

            // Activate the Explore tool
            await FrameworkApplication.SetCurrentToolAsync("esri_mapping_exploreTool");
        }

        public void SaveCoordinatesAsKml(double latitude, double longitude)
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

                // Delete all previous KML files in the output directory
                string[] kmlFiles = Directory.GetFiles(outputDir, "*.kml");
                foreach (string file in kmlFiles)
                {
                    File.Delete(file);
                }

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
                    System.Windows.MessageBox.Show($"Failed to create KML file: {kmlFilePath}", "Error");
                }
            }
            catch (Exception e)
            {
                // Handle error
                System.Windows.MessageBox.Show($"An error occurred: {e.Message}", "Error");
            }
        }


    }

}