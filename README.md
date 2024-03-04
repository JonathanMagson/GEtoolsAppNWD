# GEtoolsNWDversion

**minimum ArcGIS Pro version required:** 3.1.41833

﻿Add-in has only been tested on windows. This is the NWD version with custom tools for non-woody-disturbance workflows.

**OpenInGE:** This tool allows you to view the location of a mouse click in Google Earth Pro. Simply click on the map at the desired location, and the tool will generate a KML file with the coordinates and open it in Google Earth Pro automatically. It creates a kml file in ‘User\Documents\OpenInGE_kml_temp’ then opens it with Google Earth Pro, as long as it is the default program for opening KML files. SyncToGE must be deactivated for this tool to function correctly.

**SyncToGE:** SyncToGE synchronizes the view of ArcGIS Pro with Google Earth Pro. This tool ensures that the extent and orientation of your map in ArcGIS Pro match those in Google Earth Pro, providing seamless integration between the two applications. This tool was originall created by Chris Stayte, I have updated it to work with ArcGIS PRo 3.1. 

**LayerToGE**: With LayerToGE, you can open a feature class from ArcGIS Pro directly in Google Earth Pro. Select the desired feature class from the Contents pane, then click the LayerToGE button to export and open it in Google Earth Pro. It creates a kml file in ‘User\Documents\OpenInGE_kml_temp’ then opens it with Google Earth Pro, as long as it is the default program for opening KML files

**OpenInMaps:** OpenInMaps tool enables you to view the location of a mouse click in a web browser using Google Maps. Simply click on the map at the desired location, and the tool will open Google Maps in your default browser with the clicked location.

**OpenInStreet:** OpenInStreet provides a convenient way to view the location of a mouse click in a web browser using Google Street View. Click on the map at the desired location, and the tool will open Google Street View in your default browser with the clicked location.

**Toggle NVDI:** Toggle NVDI allows you to quickly switch the display of the NVDI layer on and off. Ensure you have a layer named 'NVDI' in the map's layer panel to use this feature.

**Installation**
1. download 'GEtools.esriAddinX'
2. run the .esriAddinX file and install addon
3. Restart ArcGis Pro.
4. Access tools in the Add-in ribbon


See LICENCE for  licences and acknowledgement for this plugin, including the MIT licence for Chris Stayte's original add-in.
