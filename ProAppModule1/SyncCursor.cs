using ArcGIS.Desktop.Framework.Contracts;

namespace GEtools
{
    /// <summary>Controls background cursor synchronization without changing the active map tool.</summary>
    internal class SyncCursor : Button
    {
        public SyncCursor()
        {
            UpdateState();
        }

        protected override void OnClick()
        {
            BackgroundCursorService.Toggle();
            UpdateState();
        }

        private void UpdateState()
        {
            bool active = BackgroundCursorService.IsRunning;
            IsChecked = active;
            Caption = active ? "Deactivate Cursor" : "Sync Cursor";
        }
    }
}
