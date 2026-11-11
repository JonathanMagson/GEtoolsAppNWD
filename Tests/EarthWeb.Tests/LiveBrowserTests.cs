using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GEtools.EarthWeb;
using Xunit;

namespace GEtools.Tests
{
    /// <summary>
    /// Drives a real Chromium browser through EarthWebWindow, against a local
    /// test page (no network). Set GETOOLS_TEST_BROWSER to the browser's path
    /// to run it; otherwise it passes without doing anything.
    /// </summary>
    public class LiveBrowserTests
    {
        private const string Page = @"<!doctype html><html><body style='margin:0'>
<canvas width='400' height='300' style='width:400px;height:300px'></canvas>
<script>
  var dropped = '';
  addEventListener('dragover', function (e) { e.preventDefault(); });
  addEventListener('drop', function (e) {
    e.preventDefault(); dropped = e.dataTransfer.files.length ? e.dataTransfer.files[0].name : 'none';
  });
  setInterval(function () {
    var marker = document.getElementById('getools-targets') ? 'marker' : 'nomarker';
    var hash = '#' + marker + '|' + dropped;
    if (location.hash !== hash) history.replaceState(null, '', hash);
  }, 50);
</script></body></html>";

        private static async Task<string> WaitForHash(EarthWebWindow w, string contains)
        {
            var clock = Stopwatch.StartNew();
            string href = null;
            while (clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                href = (await w.QueryViewAsync())?.Href;
                if (href != null && href.Contains(contains)) return href;
                await Task.Delay(100);
            }
            return href;
        }

        [Fact]
        public async Task LaunchMarkAndDrop()
        {
            string browser = Environment.GetEnvironmentVariable("GETOOLS_TEST_BROWSER");
            if (string.IsNullOrEmpty(browser)) return;

            string dir = Path.Combine(Path.GetTempPath(), "getools-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string page = Path.Combine(dir, "page.html");
            File.WriteAllText(page, Page);
            string kmz = Path.Combine(dir, "layer.kmz");
            File.WriteAllText(kmz, "x");

            var w = new EarthWebWindow { ProfileDir = Path.Combine(dir, "profile"), FindBrowser = () => browser };
            w.ExtraBrowserArgs.Add("--headless=new");
            w.ExtraBrowserArgs.Add("--no-sandbox");
            try
            {
                string url = new Uri(page).AbsoluteUri;
                Assert.Equal(EarthWebResult.Ok, await w.NavigateAsync(url, bringToFront: false));
                Assert.True(w.IsConnected);

                Assert.Contains("nomarker|", await WaitForHash(w, "nomarker|"));
                await w.SetMarkersAsync(EarthPageScripts.MarkerJs(new[] { (-33.5, 151.25) }));
                Assert.Contains("#marker|", await WaitForHash(w, "#marker|"));

                var info = await w.QueryViewAsync();
                Assert.True(info.Canvas);
                Assert.Equal(400, info.Width);
                Assert.True(await w.DropFileAsync(kmz, info.Left + info.Width / 2, info.Top + info.Height / 2));
                Assert.Contains("|layer.kmz", await WaitForHash(w, "|layer.kmz"));

                // A second navigation reuses the same window (no relaunch), and
                // the marker script is re-run on the new page load.
                Assert.Equal(EarthWebResult.Ok, await w.NavigateAsync(url + "?again", bringToFront: true));
                Assert.Contains("?again#marker|", await WaitForHash(w, "?again#marker|"));

                await w.SetMarkersAsync(null);
                Assert.Contains("nomarker", await WaitForHash(w, "nomarker"));

                // Detached: the next call reattaches to the running browser.
                w.Detach();
                Assert.False(w.IsConnected);
                Assert.Equal(EarthWebResult.Ok, await w.NavigateAsync(url + "?third", false, launchIfNeeded: false));
            }
            finally
            {
                w.Detach();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.ProcessName.Contains("chrom") && File.Exists($"/proc/{p.Id}/cmdline") &&
                            File.ReadAllText($"/proc/{p.Id}/cmdline").Contains(dir))
                            p.Kill(true);
                    }
                    catch { }
                }
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
