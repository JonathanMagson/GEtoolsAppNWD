# GEtools

**Download ArcPro 3.5 + version:** https://github.com/JonathanMagson/GEtoolsAppNWD/blob/master/GEtoolsArcProV3_5.esriAddinX

**Download ArcPro 3.3 + version:** [https://github.com/JonathanMagson/GEtoolsAppNWDversion/blob/0f8ee671d33b9c1f6b470c47a7af3817dde26e8f/GEtools.esriAddinX
](https://github.com/JonathanMagson/GEtoolsAppNWD/blob/master/GEtoolsArcProV3_3.esriAddinX)

**Download ArcPro 3.2 version:** https://github.com/JonathanMagson/GEtoolsAppNWD/blob/master/GEtoolsArcProV3_1.esriAddinX

![image](https://github.com/JonathanMagson/GEtoolsAppNWDversion/assets/162064664/cf05c4e2-8419-4e62-aa3a-cc8bc3627de0)


**minimum ArcGIS Pro version required:** 3.1.41833

The Add-in has only been tested on Windows.

In newer versions of ArcGIS PRo (3.3+), you may need to update Microsoft .NET Desktop Runtime to a minimum of version 8.0.x (x64)

## Google Earth Web or Google Earth Pro

Google Earth Pro (the desktop app) is being retired, so GEtools can also use **Google Earth Web** (earth.google.com/web). Choose which one in the **Google Earth** drop-down on the GEtools ribbon. OpenInGE, SyncToGE and LayerToGE (and the right-click OpenInGE) use whichever is selected. The choice is remembered between sessions. The default is Google Earth Pro.

With **Google Earth Web**:

* Nothing needs installing. GEtools opens Google Earth Web in its own **Google Chrome** or **Microsoft Edge** window (Chrome if installed, otherwise Edge, which comes with Windows) and steers it from ArcGIS Pro.
  * The window uses its own browser profile (`%LocalAppData%\GEtools\EarthWebBrowser`), separate from your everyday browser.
  * ArcGIS Pro talks to it over the browser's DevTools connection, which only listens on this computer (127.0.0.1).
* **OpenInGE** flies Google Earth to the clicked spot and marks it with a red **X**. Google Earth Web can't take a placemark from outside, so GEtools draws the X over the globe itself. It hides while you drag the globe and comes back when the view settles.
* **SyncToGE** moves Google Earth each time the map settles after a pan, zoom or rotation. Closing the Google Earth window turns SyncToGE off. The Fly-To Speed tip below doesn't apply.
* **LayerToGE** exports the layer selected in Contents (only its selected features, if any are selected) to a KMZ in `Documents\OpenInGE_kml_temp`. It then frames the layer in Google Earth and drops the file onto it, the same as dragging it there from Explorer. If that doesn't work, the file path is copied to the clipboard: in Google Earth Web choose **Projects > New project > Import KML file from computer**.
* Without Chrome or Edge, the tools open Google Earth Web in your default browser instead (no live sync, no X, no automatic layer loading).

Advanced options are in `%LocalAppData%\GEtools\settings.json`, created the first time you change the drop-down:

| Setting | Default | |
|---|---|---|
| `Browser` | `auto` | `auto`, `chrome`, `edge`, or `custom` (then set `BrowserPath`) |
| `RaiseEarthWindow` | `true` | Bring the Google Earth window to the front for OpenInGE and LayerToGE |
| `FastSync` | `true` | Move Google Earth without reloading it. Set to `false` if it stops following the map |
| `SyncDelayMs` | `400` | How long the map must stay still before Google Earth follows |
| `PointRangeM` | `1000` | OpenInGE viewing distance, in metres |

There is also a [QGIS version](https://github.com/JonathanMagson/GEtoolsQGIS) built around Google Earth Web.

## Tools

**OpenInGE:** This tool allows you to view the location of a mouse click in Google Earth Pro. Simply click on the map at the desired location, and the tool will generate a KML file with the coordinates and open it in Google Earth Pro automatically. It creates a kml file in ‘User\Documents\OpenInGE_kml_temp’ then opens it with Google Earth Pro, as long as it is the default program for opening KML files. SyncToGE must be deactivated for this tool to function correctly.

**SyncToGE:** SyncToGE synchronizes the view of ArcGIS Pro with Google Earth Pro. This tool ensures that the extent and orientation of your map in ArcGIS Pro match those in Google Earth Pro, when ever you pan/zoom in ArcGIS PRo, the changes are reflected in Google Earth Pro. This tool was originall created by Chris Stayte, I have updated it to work with ArcGIS PRo 3.1. original add-in: https://github.com/chrisstayte/ArcPro_To_GoogleEarth

For this feature to work correctly you need to change GE settings: Go to Tools > Options > Navigation > Change the Fly-To Speed to 5

**LayerToGE**: With LayerToGE, you can open a feature class from ArcGIS Pro directly in Google Earth Pro. Select the desired feature class from the Contents pane, then click the LayerToGE button to export and open it in Google Earth Pro. It creates a kml file in ‘User\Documents\OpenInGE_kml_temp’ then opens it with Google Earth Pro, as long as it is the default program for opening KML files

**OpenInMaps:** OpenInMaps tool enables you to view the location of a mouse click in a web browser using Google Maps. Simply click on the map at the desired location, and the tool will open Google Maps in your default browser with the clicked location.

**OpenInStreet:** OpenInStreet provides a convenient way to view the location of a mouse click in a web browser using Google Street View. Click on the map at the desired location, and the tool will open Google Street View in your default browser with the clicked location.

**Usage**
1. download 'GEtools.esriAddinX'
2. run the .esriAddinX file and install addon
3. Restart ArcGis Pro.
4. Access tools in the Add-in ribbon or by right-clicking on the map
5. Configure Google Earth. Go to Tools > Options > Navigation > Change the Fly-To Speed to 5


Accessing the tools via add-in ribbon

![image](https://github.com/JonathanMagson/GEtoolsAppNWDversion/assets/162064664/3a3242b3-7d09-478e-afc0-d3d5c56b2e58)

Accessing the tools via right-click on map context menu using explorer tool or selection tool


![image](https://github.com/JonathanMagson/GEtoolsAppNWDversion/assets/162064664/7ef51fb7-6a2c-45ad-9908-22d11659aa8d)


See LICENCE for licences and acknowledgements for this plugin, including the MIT licence for Chris Stayte's original add-in.


