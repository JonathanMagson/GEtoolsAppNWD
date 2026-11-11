using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Markup;
using System.IO;

namespace GEtools
{
    internal class LayerToGE : Button
    {
        protected override async void OnClick()
        {
            if (EarthSettings.Mode == EarthMode.Web)
            {
                // Google Earth Web: export the selected layer and drop it on the Earth window
                try
                {
                    await EarthWebActions.SendLayerAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"LayerToGE failed:\n{ex.Message}", "LayerToGE");
                }
                return;
            }

            // Google Earth Pro: run the toolbox tool, which opens the KML in Google Earth Pro
            string installPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string toolboxPath = Path.Combine(installPath, "FCtoKML.pyt\\LayerToKMLTool");
            
            Geoprocessing.OpenToolDialog(toolboxPath, null);

        }
    }
}
