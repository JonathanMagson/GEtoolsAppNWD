using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace GEtools.EarthWeb
{
    /// <summary>
    /// JavaScript run inside the Google Earth Web page.
    ///
    /// Earth Web has no way to add a placemark from outside the page, so the
    /// add-in draws a red X itself over the globe, in the browser window it
    /// controls. To keep the X on the ground, the script reads the camera
    /// Earth Web writes into its address (/@lat,lon,&lt;alt&gt;a,&lt;range&gt;d,
    /// &lt;fov&gt;y,&lt;heading&gt;h,&lt;tilt&gt;t) and projects the clicked point onto
    /// the globe's canvas. Earth Web only rewrites its address once the view
    /// settles, so the X hides while you drag or scroll and comes back after.
    ///
    /// Shared with the QGIS version of GEtools (earth_marker.py).
    /// </summary>
    internal static class EarthPageScripts
    {
        public const string DefaultColor = "#e60000";
        public const int DefaultSize = 30;

        // Idempotent: re-running it just replaces the targets and their style.
        // It's also installed to run on every page load, before the document
        // exists, hence the wait for <body>.
        private const string MarkerTemplate = """
            (function (targets, style) {
              if (window.__getoolsMarker) { window.__getoolsMarker.setTargets(targets, style); return; }

              var DEG = Math.PI / 180, A = 6378137.0, E2 = 6.69437999014e-3;

              function ecef(lat, lon, h) {
                var sp = Math.sin(lat * DEG), cp = Math.cos(lat * DEG);
                var n = A / Math.sqrt(1 - E2 * sp * sp);
                return [(n + h) * cp * Math.cos(lon * DEG), (n + h) * cp * Math.sin(lon * DEG),
                        (n * (1 - E2) + h) * sp];
              }
              function toEnu(d, lat, lon) {  // ECEF offset -> east, north, up at lat/lon
                var sp = Math.sin(lat * DEG), cp = Math.cos(lat * DEG);
                var sl = Math.sin(lon * DEG), cl = Math.cos(lon * DEG);
                return [-sl * d[0] + cl * d[1],
                        -sp * cl * d[0] - sp * sl * d[1] + cp * d[2],
                        cp * cl * d[0] + cp * sl * d[1] + sp * d[2]];
              }
              function dot(a, b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }

              function parseCamera(href) {
                var m = /\/@(-?[\d.]+),(-?[\d.]+),([^\/?#]*)/.exec(href);
                if (!m) return null;
                var cam = {lat: +m[1], lon: +m[2], a: 0, d: 0, y: 35, h: 0, t: 0};
                m[3].split(',').forEach(function (part) {
                  var key = part.slice(-1), value = parseFloat(part);
                  if ('adyht'.indexOf(key) >= 0 && !isNaN(value)) cam[key] = value;
                });
                return cam.d > 0 ? cam : null;
              }

              // Screen position of target for the camera in the URL, or null.
              function project(cam, tgt, rect) {
                var l = ecef(cam.lat, cam.lon, cam.a), t = ecef(tgt.lat, tgt.lon, cam.a);
                var p = toEnu([t[0] - l[0], t[1] - l[1], t[2] - l[2]], cam.lat, cam.lon);
                var h = cam.h * DEG, tilt = cam.t * DEG;
                var view = [Math.sin(tilt) * Math.sin(h), Math.sin(tilt) * Math.cos(h), -Math.cos(tilt)];
                var up = [Math.cos(tilt) * Math.sin(h), Math.cos(tilt) * Math.cos(h), Math.sin(tilt)];
                var right = [Math.cos(h), -Math.sin(h), 0];
                // Camera sits cam.d metres back from the look-at point along the view.
                var q = [p[0] + cam.d * view[0], p[1] + cam.d * view[1], p[2] + cam.d * view[2]];
                var depth = dot(q, view);
                if (depth <= 1) return null;
                var f = (rect.height / 2) / Math.tan(cam.y * DEG / 2);
                var x = rect.left + rect.width / 2 + f * dot(q, right) / depth;
                var y = rect.top + rect.height / 2 - f * dot(q, up) / depth;
                if (x < rect.left || x > rect.right || y < rect.top || y > rect.bottom) return null;
                return [x, y];
              }

              // The globe is the largest <canvas>, possibly inside shadow DOM.
              function globeRect() {
                var best = null, bestArea = 0;
                (function walk(root) {
                  root.querySelectorAll('*').forEach(function (el) {
                    if (el.tagName === 'CANVAS') {
                      var r = el.getBoundingClientRect(), area = r.width * r.height;
                      if (area > bestArea) { best = r; bestArea = area; }
                    }
                    if (el.shadowRoot) walk(el.shadowRoot);
                  });
                })(document);
                if (best) return best;
                return {left: 0, top: 0, width: innerWidth, height: innerHeight,
                        right: innerWidth, bottom: innerHeight};
              }

              var layer = document.createElement('div');
              layer.id = 'getools-targets';
              layer.style.cssText = 'position:fixed;left:0;top:0;width:0;height:0;' +
                'pointer-events:none;z-index:2147483647;';

              var state = {targets: targets, style: style, href: null, moving: false, rect: ''};

              // Built with DOM calls, not innerHTML: Google pages enforce Trusted
              // Types, which blocks assigning HTML strings.
              var SVG = 'http://www.w3.org/2000/svg';
              function cross(style) {
                var size = style.size, el = document.createElement('div');
                el.className = 'getools-target';
                el.style.cssText = 'position:absolute;width:' + size + 'px;height:' + size + 'px;display:none;';
                var svg = document.createElementNS(SVG, 'svg');
                svg.setAttribute('width', size);
                svg.setAttribute('height', size);
                svg.setAttribute('viewBox', '0 0 30 30');
                [['white', 7], [style.color, 4]].forEach(function (stroke) {
                  var path = document.createElementNS(SVG, 'path');
                  path.setAttribute('d', 'M5 5L25 25M25 5L5 25');
                  path.setAttribute('stroke', stroke[0]);
                  path.setAttribute('stroke-width', stroke[1]);
                  path.setAttribute('stroke-linecap', 'round');
                  svg.appendChild(path);
                });
                el.appendChild(svg);
                return el;
              }

              function rebuild() {
                while (layer.firstChild) layer.removeChild(layer.firstChild);
                state.targets.forEach(function () { layer.appendChild(cross(state.style)); });
              }

              function place() {
                var cam = parseCamera(location.href), rect = globeRect();
                var crosses = layer.children, size = state.style.size;
                state.targets.forEach(function (target, i) {
                  var pos = (cam && !state.moving) ? project(cam, target, rect) : null;
                  var el = crosses[i];
                  if (!el) return;
                  if (!pos) { el.style.display = 'none'; return; }
                  el.style.left = (pos[0] - size / 2) + 'px';
                  el.style.top = (pos[1] - size / 2) + 'px';
                  el.style.display = 'block';
                });
              }

              // Hide while the user moves the globe: the URL lags behind the camera.
              var down = null;
              function onDown(e) { down = [e.clientX, e.clientY]; }
              function onMove(e) {
                if (down && Math.abs(e.clientX - down[0]) + Math.abs(e.clientY - down[1]) > 3) {
                  state.moving = true; place();
                }
              }
              function onUp() { down = null; }
              function onWheel() { state.moving = true; place(); }
              addEventListener('pointerdown', onDown, true);
              addEventListener('pointermove', onMove, true);
              addEventListener('pointerup', onUp, true);
              addEventListener('wheel', onWheel, {capture: true, passive: true});

              var timer = setInterval(function () {
                if (!layer.isConnected && document.body) document.body.appendChild(layer);
                var r = globeRect(), key = [r.left, r.top, r.width, r.height].join();
                if (location.href !== state.href || key !== state.rect) {
                  state.href = location.href; state.rect = key; state.moving = false;
                  place();
                }
              }, 150);

              window.__getoolsMarker = {
                setTargets: function (t, s) {
                  state.targets = t; state.style = s; state.moving = false; rebuild(); place();
                },
                remove: function () {
                  clearInterval(timer); layer.remove(); delete window.__getoolsMarker;
                  removeEventListener('pointerdown', onDown, true);
                  removeEventListener('pointermove', onMove, true);
                  removeEventListener('pointerup', onUp, true);
                  removeEventListener('wheel', onWheel, {capture: true});
                }
              };
              rebuild();
              place();
            })(__TARGETS__, __STYLE__);
            """;

        /// <summary>JavaScript that shows an X at each (lat, lon) in targets.</summary>
        public static string MarkerJs(IEnumerable<(double Lat, double Lon)> targets,
                                      string color = DefaultColor, int size = DefaultSize)
        {
            var points = targets.Select(t => new { lat = t.Lat, lon = t.Lon }).ToArray();
            var style = new { color, size };
            return MarkerTemplate
                .Replace("__TARGETS__", JsonSerializer.Serialize(points))
                .Replace("__STYLE__", JsonSerializer.Serialize(style));
        }

        public const string ClearMarkerJs = """
            window.__getoolsMarker && window.__getoolsMarker.remove();
            """;

        // Reports the page address (camera) and the globe canvas, as JSON. The
        // canvas is the largest <canvas> on the page, possibly in shadow DOM.
        public const string ViewInfoJs = """
            (function () {
              var best = null, bestArea = 0;
              (function walk(root) {
                root.querySelectorAll('*').forEach(function (el) {
                  if (el.tagName === 'CANVAS') {
                    var r = el.getBoundingClientRect(), area = r.width * r.height;
                    if (area > bestArea) { best = r; bestArea = area; }
                  }
                  if (el.shadowRoot) walk(el.shadowRoot);
                });
              })(document);
              var r = best || {left: 0, top: 0, width: innerWidth, height: innerHeight};
              return JSON.stringify({href: location.href, left: r.left, top: r.top,
                                     width: r.width, height: r.height, canvas: !!best,
                                     ready: document.readyState});
            })()
            """;
    }
}
