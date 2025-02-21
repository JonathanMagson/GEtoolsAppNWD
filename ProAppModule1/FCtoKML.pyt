import arcpy
import os
import datetime

class Toolbox(object):
    def __init__(self):
        """Define the toolbox (the name of the toolbox is the name of the .pyt file)."""
        self.label = "Custom Toolbox"
        self.alias = "customtoolbox"
        self.tools = [LayerToKMLTool]

class LayerToKMLTool(object):
    def __init__(self):
        """Define the tool (tool name is the name of the class)."""
        self.label = "Layer To KML Tool"
        self.description = "Converts a layer to KML format."
        self.canRunInBackground = False

    def getParameterInfo(self):
        """Define parameter definitions"""
        param0 = arcpy.Parameter(
            displayName="Input Layer",
            name="input_layer",
            datatype="GPFeatureLayer",
            parameterType="Required",
            direction="Input")

        params = [param0]
        return params

    def isLicensed(self):
        """Set whether tool is licensed to execute."""
        return True

    def updateParameters(self, parameters):
        """Modify the values and properties of parameters before internal validation is performed."""
        return

    def updateMessages(self, parameters):
        """Modify the messages created by internal validation for each tool parameter."""
        return

    def execute(self, parameters, messages):
        """The source code of the tool."""
        try:
            # Get input layer
            input_layer = parameters[0].valueAsText

            # Output directory
            username = os.getlogin()
            output_dir = os.path.join("C:\\Users", username, "Documents", "OpenInGE_kml_temp")
            if not os.path.exists(output_dir):
	            os.makedirs(output_dir)
            timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
            kml_out_file = os.path.join(output_dir, f"{input_layer}_{timestamp}.kml")

            #delete old kml files
            for file_name in os.listdir(output_dir):
                if file_name.endswith(".kml"):
                    file_path = os.path.join(output_dir, file_name)
                    os.remove(file_path)
                    print(f"Deleted existing KML file: {file_path}")


            # Run Layer To KML tool
            arcpy.conversion.LayerToKML(layer=input_layer, out_kmz_file=kml_out_file, layer_output_scale=0, is_composite="NO_COMPOSITE", ignore_zvalue="CLAMPED_TO_GROUND")

            arcpy.AddMessage("KML file created successfully.")

            os.system(f'start "" "{kml_out_file}"')
            
        except Exception as e:
            arcpy.AddError(f"An error occurred: {str(e)}")
