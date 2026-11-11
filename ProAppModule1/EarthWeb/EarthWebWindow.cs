using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GEtools.EarthWeb
{
    internal enum EarthWebResult
    {
        /// <summary>The Earth window is showing the requested view.</summary>
        Ok,
        /// <summary>Neither Chrome nor Edge (nor the chosen browser) was found.</summary>
        NoBrowser,
        /// <summary>The window was closed and launching wasn't allowed.</summary>
        Closed,
        /// <summary>The browser didn't start, or stopped answering.</summary>
        Failed,
    }

    /// <summary>What the Earth page reports about itself (see EarthPageScripts.ViewInfoJs).</summary>
    internal sealed class EarthViewInfo
    {
        public string Href { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Canvas { get; set; }
        public string Ready { get; set; }
    }

    /// <summary>
    /// Google Earth Web in a Chrome or Edge window driven from ArcGIS Pro over
    /// the browser's DevTools connection (127.0.0.1 only).
    ///
    /// The window runs on a profile of its own (%LocalAppData%\GEtools\EarthWebBrowser),
    /// so it remembers Google's cookie consent between sessions without touching
    /// the user's everyday browser.
    /// </summary>
    internal sealed class EarthWebWindow
    {
        public static EarthWebWindow Instance { get; } = new EarthWebWindow();

        private const double StartupTimeoutS = 20.0;
        private const int PollMs = 250;

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private CdpConnection _conn;
        private bool _loadedEarth;
        private string _markerJs;
        private string _markerScriptId;

        public string ProfileDir { get; set; } = Path.Combine(EarthSettings.Folder, "EarthWebBrowser");

        /// <summary>Which browser to start. Swappable for tests.</summary>
        public Func<string> FindBrowser { get; set; } = () => Browsers.Find(EarthSettings.Browser, EarthSettings.BrowserPath);

        /// <summary>Extra browser arguments (tests run headless).</summary>
        public IList<string> ExtraBrowserArgs { get; } = new List<string>();

        /// <summary>The browser last used, for messages.</summary>
        public string BrowserName { get; private set; } = "the browser";

        /// <summary>Last error, for messages when a call returns Failed.</summary>
        public string LastError { get; private set; }

        public bool IsConnected => _conn != null;

        /// <summary>
        /// Show url in the Earth window. Reuses the open window; if there isn't
        /// one, reattaches to the add-in's browser or (if launchIfNeeded) starts it.
        /// </summary>
        public async Task<EarthWebResult> NavigateAsync(string url, bool bringToFront, bool launchIfNeeded = true)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_conn != null && await TryGoAsync(url, bringToFront).ConfigureAwait(false))
                    return EarthWebResult.Ok;

                // The tab may have been replaced (e.g. a reload), or the browser
                // is running from an earlier session: look for it.
                if (await TryAttachAsync().ConfigureAwait(false) &&
                    await TryGoAsync(url, bringToFront).ConfigureAwait(false))
                    return EarthWebResult.Ok;

                if (!launchIfNeeded)
                    return EarthWebResult.Closed;
                return await LaunchAsync(url).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Draw (js) or clear (null) the target Xs. The script is also registered
        /// to run on every page load, so the X survives full reloads of Earth Web.
        /// </summary>
        public async Task SetMarkersAsync(string js)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _markerJs = js;
                if (_conn == null)
                    return; // applied when we next attach
                try
                {
                    await InstallMarkerAsync().ConfigureAwait(false);
                }
                catch (CdpException)
                {
                    Disconnect();
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>The page's address and globe canvas, or null if unavailable.</summary>
        public async Task<EarthViewInfo> QueryViewAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_conn == null)
                    return null;
                try
                {
                    var value = await _conn.EvaluateAsync(EarthPageScripts.ViewInfoJs).ConfigureAwait(false);
                    if (value.ValueKind != JsonValueKind.String)
                        return null;
                    return JsonSerializer.Deserialize<EarthViewInfo>(value.GetString(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (CdpException)
                {
                    Disconnect();
                    return null;
                }
                catch (JsonException)
                {
                    return null;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Drop a local file on the page at x, y, as if dragged from Explorer.
        /// False if the window couldn't be reached.
        /// </summary>
        public async Task<bool> DropFileAsync(string path, double x, double y)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_conn == null)
                    return false;
                try
                {
                    foreach (var (method, args) in DevTools.DropFileEvents(path, x, y))
                        await _conn.CallAsync(method, args).ConfigureAwait(false);
                    return true;
                }
                catch (CdpException)
                {
                    Disconnect();
                    return false;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Wait for Google Earth Web to finish loading (globe canvas present and
        /// a camera in the address, steady for settle), then drop the file in the
        /// middle of the globe. Google Earth Web has no URL or API for loading a
        /// file, so this is the same as dragging it there by hand.
        /// </summary>
        public async Task<bool> DropWhenReadyAsync(string path, TimeSpan timeout, TimeSpan? settle = null)
        {
            var settleTime = settle ?? TimeSpan.FromSeconds(2);
            var clock = Stopwatch.StartNew();
            TimeSpan? readySince = null;
            while (clock.Elapsed < timeout)
            {
                await Task.Delay(500).ConfigureAwait(false);
                var info = await QueryViewAsync().ConfigureAwait(false);
                if (info == null && _conn == null)
                    return false; // window gone
                bool ready = info != null && info.Canvas && info.Ready == "complete" && GoogleUrls.HasCamera(info.Href);
                if (!ready || clock.Elapsed < settleTime)
                {
                    readySince = null;
                    continue;
                }
                readySince ??= clock.Elapsed;
                if (clock.Elapsed - readySince.Value < settleTime)
                    continue;
                return await DropFileAsync(path, info.Left + info.Width / 2.0, info.Top + info.Height / 2.0)
                    .ConfigureAwait(false);
            }
            return false;
        }

        /// <summary>Stop driving the window. The window itself is left open.</summary>
        public void Detach()
        {
            _gate.Wait();
            try { Disconnect(); }
            finally { _gate.Release(); }
        }

        // ---- internals (called with _gate held) ------------------------------

        private async Task<bool> TryGoAsync(string url, bool bringToFront)
        {
            try
            {
                if (EarthSettings.FastSync && _loadedEarth && url.Contains("/web/@"))
                    await _conn.CallAsync("Runtime.evaluate", new { expression = GoogleUrls.SoftNavJs(url) }).ConfigureAwait(false);
                else
                {
                    await _conn.CallAsync("Page.navigate", new { url }).ConfigureAwait(false);
                    _loadedEarth = url.StartsWith(GoogleUrls.EarthWeb, StringComparison.Ordinal);
                }
                if (bringToFront)
                    await RaiseWindowAsync().ConfigureAwait(false);
                return true;
            }
            catch (CdpException ex)
            {
                LastError = ex.Message;
                Disconnect();
                return false;
            }
        }

        private async Task InstallMarkerAsync()
        {
            if (_markerScriptId != null)
            {
                await _conn.CallAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier = _markerScriptId })
                           .ConfigureAwait(false);
                _markerScriptId = null;
            }
            if (_markerJs != null)
            {
                // Load-time scripts only run while the Page domain is enabled.
                await _conn.CallAsync("Page.enable").ConfigureAwait(false);
                var result = await _conn.CallAsync("Page.addScriptToEvaluateOnNewDocument", new { source = _markerJs })
                                        .ConfigureAwait(false);
                _markerScriptId = result.TryGetProperty("identifier", out var id) ? id.GetString() : null;
            }
            await _conn.CallAsync("Runtime.evaluate", new { expression = _markerJs ?? EarthPageScripts.ClearMarkerJs })
                       .ConfigureAwait(false);
        }

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        /// <summary>
        /// Restore the Earth window if minimized and put it in front.
        /// Page.bringToFront alone only picks the tab.
        /// </summary>
        private async Task RaiseWindowAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows only lets the foreground app (ArcGIS Pro, which was
                // just clicked) hand focus to another process; grant it.
                try { AllowSetForegroundWindow(-1); } // ASFW_ANY
                catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException) { }
            }
            try
            {
                var window = await _conn.CallAsync("Browser.getWindowForTarget").ConfigureAwait(false);
                if (window.TryGetProperty("bounds", out var bounds) &&
                    bounds.TryGetProperty("windowState", out var state) && state.GetString() == "minimized")
                {
                    await _conn.CallAsync("Browser.setWindowBounds",
                        new { windowId = window.GetProperty("windowId").GetInt32(), bounds = new { windowState = "normal" } })
                        .ConfigureAwait(false);
                }
            }
            catch (CdpException)
            {
                // Browser without the Browser domain: just pick the tab.
            }
            await _conn.CallAsync("Page.bringToFront").ConfigureAwait(false);
        }

        /// <summary>Connect to a browser already running on our profile, if any.</summary>
        private async Task<bool> TryAttachAsync()
        {
            Disconnect();
            int? port = DevTools.ReadPort(ProfileDir);
            if (port == null)
                return false;
            string wsUrl = await DevTools.FindPageAsync(port.Value).ConfigureAwait(false);
            if (wsUrl == null)
                return false;
            try
            {
                _conn = await CdpConnection.ConnectAsync(wsUrl).ConfigureAwait(false);
            }
            catch (CdpException ex)
            {
                LastError = ex.Message;
                return false;
            }
            // Whatever is showing may not be Earth Web yet; the first move is a
            // real navigation, then fast sync takes over.
            _loadedEarth = false;
            // Scripts registered on a previous connection died with it.
            _markerScriptId = null;
            if (_markerJs != null)
            {
                try
                {
                    await InstallMarkerAsync().ConfigureAwait(false);
                }
                catch (CdpException ex)
                {
                    LastError = ex.Message;
                    Disconnect();
                    return false;
                }
            }
            return true;
        }

        private async Task<EarthWebResult> LaunchAsync(string url)
        {
            string browser = FindBrowser();
            if (browser == null)
                return EarthWebResult.NoBrowser;
            BrowserName = Browsers.DisplayName(browser);

            // A port file left by an earlier run would point at a dead port.
            try { File.Delete(Path.Combine(ProfileDir, DevTools.PortFileName)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }

            Process process;
            try
            {
                process = DevTools.Launch(browser, ProfileDir, url, ExtraBrowserArgs);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException ||
                                       ex is InvalidOperationException)
            {
                LastError = $"Couldn't start {BrowserName}: {ex.Message}";
                return EarthWebResult.Failed;
            }

            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < StartupTimeoutS)
            {
                await Task.Delay(PollMs).ConfigureAwait(false);
                if (await TryAttachAsync().ConfigureAwait(false))
                {
                    // The window opened on the URL it was launched with.
                    _loadedEarth = url.StartsWith(GoogleUrls.EarthWeb, StringComparison.Ordinal);
                    return EarthWebResult.Ok;
                }
                // The launcher process can hand over to an already-running
                // browser and exit, so only give up early if no port appears.
                if (process != null && process.HasExited && clock.Elapsed.TotalSeconds > 5 &&
                    DevTools.ReadPort(ProfileDir) == null)
                    break;
            }
            LastError = $"{BrowserName} didn't start in time.";
            return EarthWebResult.Failed;
        }

        private void Disconnect()
        {
            _conn?.Dispose();
            _conn = null;
        }
    }
}
