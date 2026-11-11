using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GEtools.EarthWeb
{
    internal sealed class CdpException : Exception
    {
        public CdpException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>Finds a Chromium browser (Chrome or Edge) that the add-in can drive.</summary>
    internal static class Browsers
    {
        /// <summary>Choices for the browser setting. "auto" prefers Chrome, then Edge.</summary>
        public static readonly string[] Choices = { "auto", "chrome", "edge", "custom" };

        private static readonly Dictionary<string, (Environment.SpecialFolder root, string rel)[]> WindowsCandidates =
            new Dictionary<string, (Environment.SpecialFolder, string)[]>
            {
                ["chrome"] = new[]
                {
                    (Environment.SpecialFolder.ProgramFiles, @"Google\Chrome\Application\chrome.exe"),
                    (Environment.SpecialFolder.ProgramFilesX86, @"Google\Chrome\Application\chrome.exe"),
                    (Environment.SpecialFolder.LocalApplicationData, @"Google\Chrome\Application\chrome.exe"),
                },
                ["edge"] = new[]
                {
                    (Environment.SpecialFolder.ProgramFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
                    (Environment.SpecialFolder.ProgramFiles, @"Microsoft\Edge\Application\msedge.exe"),
                },
            };

        private static string FindKind(string kind)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;
            foreach (var (root, rel) in WindowsCandidates[kind])
            {
                string baseDir = Environment.GetFolderPath(root);
                if (string.IsNullOrEmpty(baseDir))
                    continue;
                string path = Path.Combine(baseDir, rel);
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        /// <summary>Path of the browser to drive, or null if it isn't installed.</summary>
        public static string Find(string choice = "auto", string customPath = "")
        {
            switch ((choice ?? "auto").ToLowerInvariant())
            {
                case "custom":
                    return !string.IsNullOrEmpty(customPath) && File.Exists(customPath) ? customPath : null;
                case "chrome":
                case "edge":
                    return FindKind(choice.ToLowerInvariant());
                default:
                    return FindKind("chrome") ?? FindKind("edge");
            }
        }

        public static string DisplayName(string path)
        {
            string name = Path.GetFileName(path ?? "").ToLowerInvariant();
            if (name.Contains("edge")) return "Microsoft Edge";
            if (name.Contains("chromium")) return "Chromium";
            return "Google Chrome";
        }
    }

    /// <summary>Starting a browser with remote debugging on, and finding its pages.</summary>
    internal static class DevTools
    {
        // Loopback only: never go through a system proxy.
        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(2),
        };

        public const string PortFileName = "DevToolsActivePort";

        /// <summary>
        /// Start the browser in app mode on url, with its own profile and the
        /// DevTools port on 127.0.0.1. Port 0 lets the browser pick a free port;
        /// it writes it to DevToolsActivePort in the profile (see ReadPort).
        /// </summary>
        public static Process Launch(string browser, string profileDir, string url, IEnumerable<string> extraArgs = null)
        {
            Directory.CreateDirectory(profileDir);
            var psi = new ProcessStartInfo(browser) { UseShellExecute = false };
            psi.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
            psi.ArgumentList.Add("--remote-debugging-port=0");
            psi.ArgumentList.Add("--user-data-dir=" + profileDir);
            psi.ArgumentList.Add("--no-first-run");
            psi.ArgumentList.Add("--no-default-browser-check");
            psi.ArgumentList.Add("--app=" + url);
            foreach (string arg in extraArgs ?? Enumerable.Empty<string>())
                psi.ArgumentList.Add(arg);
            return Process.Start(psi);
        }

        /// <summary>The DevTools port of a browser running on profileDir, or null.</summary>
        public static int? ReadPort(string profileDir)
        {
            try
            {
                string path = Path.Combine(profileDir, PortFileName);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    return int.TryParse(reader.ReadLine()?.Trim(), out int port) && port > 0 ? port : (int?)null;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>
        /// WebSocket URL of the page to drive: the tab whose address contains
        /// urlHint if there is one, else any normal page. Null if none.
        /// </summary>
        public static async Task<string> FindPageAsync(int port, string urlHint = "earth.google.com")
        {
            string json;
            try
            {
                json = await Http.GetStringAsync($"http://127.0.0.1:{port}/json/list").ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                return null;
            }
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var pages = doc.RootElement.EnumerateArray()
                        .Where(t => Str(t, "type") == "page" && !string.IsNullOrEmpty(Str(t, "webSocketDebuggerUrl")))
                        .ToList();
                    var match = pages.FirstOrDefault(t => (Str(t, "url") ?? "").Contains(urlHint));
                    if (match.ValueKind == JsonValueKind.Object)
                        return Str(match, "webSocketDebuggerUrl");
                    return pages.Count > 0 ? Str(pages[0], "webSocketDebuggerUrl") : null;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>
        /// CDP Input.dispatchDragEvent calls that drop a local file at x, y:
        /// the same as dragging the file from Explorer onto the page.
        /// </summary>
        public static List<(string Method, object Params)> DropFileEvents(string path, double x, double y)
        {
            var data = new { items = Array.Empty<object>(), files = new[] { Path.GetFullPath(path) }, dragOperationsMask = 1 };
            return new[] { "dragEnter", "dragOver", "drop" }
                .Select(kind => ("Input.dispatchDragEvent", (object)new { type = kind, x, y, data }))
                .ToList();
        }
    }

    /// <summary>One DevTools (CDP) WebSocket connection to a page. Calls are serialized.</summary>
    internal sealed class CdpConnection : IDisposable
    {
        private readonly ClientWebSocket _ws;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private int _nextId;

        private CdpConnection(ClientWebSocket ws) { _ws = ws; }

        public bool IsOpen => _ws.State == WebSocketState.Open;

        public static async Task<CdpConnection> ConnectAsync(string wsUrl, int timeoutMs = 3000)
        {
            if (wsUrl == null || !wsUrl.StartsWith("ws://", StringComparison.Ordinal))
                throw new CdpException($"unsupported DevTools URL {wsUrl}");
            // ClientWebSocket sends no Origin header, so Chrome's
            // --remote-allow-origins check doesn't apply.
            var ws = new ClientWebSocket();
            ws.Options.Proxy = null;
            try
            {
                using (var cts = new CancellationTokenSource(timeoutMs))
                    await ws.ConnectAsync(new Uri(wsUrl), cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is CdpException))
            {
                ws.Dispose();
                throw new CdpException("couldn't connect to DevTools: " + ex.Message, ex);
            }
            return new CdpConnection(ws);
        }

        /// <summary>Send one CDP command and return its result (events in between are skipped).</summary>
        public async Task<JsonElement> CallAsync(string method, object parameters = null, int timeoutMs = 5000)
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                int id = ++_nextId;
                var message = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters ?? new { },
                };
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
                using (var cts = new CancellationTokenSource(timeoutMs))
                {
                    await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token)
                             .ConfigureAwait(false);
                    while (true)
                    {
                        string text = await ReceiveAsync(cts.Token).ConfigureAwait(false);
                        using (var doc = JsonDocument.Parse(text))
                        {
                            var root = doc.RootElement;
                            if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number ||
                                idEl.GetInt32() != id)
                                continue; // an event, or a reply to something else
                            if (root.TryGetProperty("error", out var error))
                            {
                                string msg = error.TryGetProperty("message", out var m) ? m.GetString() : error.ToString();
                                throw new CdpException($"{method}: {msg}");
                            }
                            return root.TryGetProperty("result", out var result) ? result.Clone() : default;
                        }
                    }
                }
            }
            catch (CdpException)
            {
                throw;
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException ||
                                       ex is JsonException || ex is ObjectDisposedException ||
                                       ex is InvalidOperationException)
            {
                throw new CdpException($"{method}: {ex.Message}", ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Evaluate JavaScript in the page and return its value (returnByValue).</summary>
        public async Task<JsonElement> EvaluateAsync(string expression, int timeoutMs = 5000)
        {
            var result = await CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, timeoutMs)
                .ConfigureAwait(false);
            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("exceptionDetails", out var details))
                throw new CdpException("page script failed: " + details.ToString());
            return result.ValueKind == JsonValueKind.Object &&
                   result.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v)
                ? v.Clone()
                : default;
        }

        private async Task<string> ReceiveAsync(CancellationToken token)
        {
            var buffer = new byte[64 * 1024];
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    var res = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close)
                        throw new CdpException("DevTools connection closed");
                    ms.Write(buffer, 0, res.Count);
                    if (res.EndOfMessage)
                        return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        public void Dispose()
        {
            try { _ws.Abort(); } catch { /* already gone */ }
            _ws.Dispose();
        }
    }
}
