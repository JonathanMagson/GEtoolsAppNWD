using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using System;
using System.Diagnostics;
using System.IO;


namespace GEtools
{
    internal class OpenInGEcontext : Button
    {
        protected override async void OnClick()
        {
            // Replace "YourNamespace_YourToolId" with the actual DAML ID of your tool
            await FrameworkApplication.SetCurrentToolAsync("GEtools_OpenInGE");
        }
    }
}