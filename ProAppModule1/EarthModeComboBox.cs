using ArcGIS.Desktop.Framework.Contracts;
using System.Linq;

namespace GEtools
{
    /// <summary>
    /// Ribbon drop-down choosing which Google Earth the GEtools buttons use:
    /// Google Earth Web (in a Chrome/Edge window) or Google Earth Pro (desktop).
    /// The choice is remembered between ArcGIS Pro sessions.
    /// </summary>
    internal class EarthModeComboBox : ComboBox
    {
        private const string WebText = "Google Earth Web";
        private const string DesktopText = "Google Earth Pro";

        private bool _updating;

        public EarthModeComboBox()
        {
            Add(new ComboBoxItem(WebText));
            Add(new ComboBoxItem(DesktopText));
            Select(EarthSettings.Mode);
            Enabled = true;
        }

        private void Select(EarthMode mode)
        {
            string text = mode == EarthMode.Web ? WebText : DesktopText;
            _updating = true;
            try
            {
                SelectedItem = ItemCollection.OfType<ComboBoxItem>().FirstOrDefault(i => i.Text == text);
            }
            finally
            {
                _updating = false;
            }
        }

        protected override void OnSelectionChange(ComboBoxItem item)
        {
            if (_updating || item == null)
                return;
            EarthSettings.Mode = item.Text == WebText ? EarthMode.Web : EarthMode.Desktop;
        }
    }
}
