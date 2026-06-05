// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using VideoArchiveManager.Core.Services;

namespace VideoArchiveManager.App.Helpers.Controls;

/// <summary>
/// Whole-archive map browse surface: hosts a Leaflet map on OpenStreetMap
/// tiles inside a WebView2 and plots every geotagged clip as clustered
/// markers via the bundled Leaflet.markercluster plugin.
///
/// <para>
/// The control raises two events the host window forwards to the catalog:
/// <see cref="ClipSelected"/> when the user clicks a single (un-clustered)
/// marker, and <see cref="FilterToClipsRequested"/> when the user clicks a
/// cluster (the cluster's child clip ids) — turning location into a primary
/// navigation axis ("where did I shoot that?"). <see cref="GetVisibleIdsAsync"/>
/// backs the toolbar's "Filter grid to this view" action by returning the ids
/// of every marker currently inside the map viewport.
/// </para>
/// <para>
/// Like <see cref="LocationMapView"/>, Leaflet + markercluster (CSS / JS) and
/// the marker PNGs are embedded in the assembly and inlined into the page at
/// runtime, so the map library needs no network access; only the OSM tiles
/// are fetched online. WebView2 init failures are swallowed — the control
/// simply renders empty. The catalog is read only; no source file is touched.
/// </para>
/// </summary>
public class GlobalMapView : UserControl
{
    private readonly WebView2 _webView;
    private bool _coreInitialized;
    private bool _firstNavigationDone;

    // Latest points to plot. Held so a SetPoints call that arrives before the
    // WebView2 core / first navigation is ready is applied once the page loads.
    private string _pointsJson = "[]";

    // World view fallback when there are no geotagged clips yet, so the page
    // still renders a sensible map rather than a blank pane.
    private const double FallbackLatitude = 20.0;
    private const double FallbackLongitude = 0.0;

    private const string MapHtmlTemplate = """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1.0">
<style>__LEAFLET_CSS__</style>
<style>__CLUSTER_CSS__</style>
<style>__CLUSTER_DEFAULT_CSS__</style>
<style>
  html, body, #m { height: 100%; margin: 0; padding: 0; }
  body { background: #1e1e1e; }
  .leaflet-container { background: #1e1e1e; }
</style>
</head>
<body>
<div id="m"></div>
<script>__LEAFLET_JS__</script>
<script>__CLUSTER_JS__</script>
<script>
  delete L.Icon.Default.prototype._getIconUrl;
  L.Icon.Default.mergeOptions({
    iconUrl: '__MARKER_ICON__',
    iconRetinaUrl: '__MARKER_ICON_2X__',
    shadowUrl: '__MARKER_SHADOW__'
  });

  var map = L.map('m', { zoomControl: true, attributionControl: true })
    .setView([__LAT__, __LON__], 2);
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
  }).addTo(map);

  var cluster = L.markerClusterGroup({ chunkedLoading: true });
  map.addLayer(cluster);

  function post(payload) {
    try { window.chrome.webview.postMessage(JSON.stringify(payload)); }
    catch (err) { /* host disconnected */ }
  }

  // Plot the supplied [{id, lat, lon}] points: rebuild the cluster group,
  // wire a per-marker click that selects the clip, and fit the viewport to
  // the whole set. Each marker stashes its catalog id under options.clipId so
  // getVisibleIds() / the cluster handler can read it back.
  function setPoints(points) {
    cluster.clearLayers();
    var markers = [];
    var latlngs = [];
    for (var i = 0; i < points.length; i++) {
      var p = points[i];
      var m = L.marker([p.lat, p.lon]);
      m.options.clipId = p.id;
      (function (id) {
        m.on('click', function () { post({ type: 'select', id: id }); });
      })(p.id);
      markers.push(m);
      latlngs.push([p.lat, p.lon]);
    }
    cluster.addLayers(markers);
    // Fit to the raw point bounds rather than cluster.getBounds(): with
    // chunkedLoading the cluster tree may not be fully populated on the same
    // tick, but the point list always is.
    if (latlngs.length > 0) {
      try { map.fitBounds(L.latLngBounds(latlngs), { padding: [32, 32], maxZoom: 14 }); }
      catch (err) { /* single point or degenerate bounds */ }
    }
  }

  // Clicking a cluster zooms to its children (native markercluster behaviour)
  // AND hands the child clip ids back to the host so the grid scopes to them.
  cluster.on('clusterclick', function (e) {
    var children = e.layer.getAllChildMarkers();
    var ids = [];
    for (var i = 0; i < children.length; i++) {
      if (children[i].options.clipId != null) ids.push(children[i].options.clipId);
    }
    post({ type: 'filter', ids: ids });
  });

  // Ids of every marker currently within the map viewport — backs the
  // toolbar "Filter grid to this view" action. Returned by value to the host
  // via ExecuteScriptAsync.
  function getVisibleIds() {
    var bounds = map.getBounds();
    var ids = [];
    cluster.eachLayer(function (m) {
      if (m.options.clipId != null && bounds.contains(m.getLatLng())) {
        ids.push(m.options.clipId);
      }
    });
    return ids;
  }

  setPoints(__POINTS__);
</script>
</body>
</html>
""";

    private static string? _resolvedTemplate;

    private static string ResolvedTemplate =>
        _resolvedTemplate ??= MapHtmlTemplate
            .Replace("__LEAFLET_CSS__", LoadTextResource("Leaflet.leaflet.css"))
            .Replace("__CLUSTER_CSS__", LoadTextResource("Leaflet.MarkerCluster.css"))
            .Replace("__CLUSTER_DEFAULT_CSS__", LoadTextResource("Leaflet.MarkerCluster.Default.css"))
            .Replace("__LEAFLET_JS__", LoadTextResource("Leaflet.leaflet.js"))
            .Replace("__CLUSTER_JS__", LoadTextResource("Leaflet.markercluster.js"))
            .Replace("__MARKER_ICON_2X__", LoadImageDataUri("Leaflet.marker-icon-2x.png"))
            .Replace("__MARKER_ICON__", LoadImageDataUri("Leaflet.marker-icon.png"))
            .Replace("__MARKER_SHADOW__", LoadImageDataUri("Leaflet.marker-shadow.png"));

    private static string LoadTextResource(string logicalName)
    {
        using var stream = ResourceStream(logicalName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string LoadImageDataUri(string logicalName)
    {
        using var stream = ResourceStream(logicalName);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private static Stream ResourceStream(string logicalName) =>
        typeof(GlobalMapView).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"Embedded Leaflet resource '{logicalName}' not found. " +
                "Check the <EmbeddedResource LogicalName=...> entries in VideoArchiveManager.App.csproj.");

    /// <summary>
    /// Raised when the user clicks a single (un-clustered) marker. The host
    /// selects that clip in the grid.
    /// </summary>
    public event EventHandler<int>? ClipSelected;

    /// <summary>
    /// Raised when the user clicks a cluster. Carries the catalog ids of every
    /// clip in that cluster; the host scopes the grid to exactly those clips.
    /// </summary>
    public event EventHandler<IReadOnlyList<int>>? FilterToClipsRequested;

    public GlobalMapView()
    {
        _webView = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1E, 0x1E, 0x1E),
        };
        Content = _webView;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Replaces the plotted clips with <paramref name="points"/>. Safe to call
    /// before the WebView2 has finished initialising — the points are applied
    /// once the page is navigated.
    /// </summary>
    public void SetPoints(IReadOnlyList<MapClipPoint> points)
    {
        _pointsJson = BuildPointsJson(points);

        if (_coreInitialized && _firstNavigationDone)
        {
            _ = PushPointsAsync();
        }
        else if (_coreInitialized)
        {
            _ = NavigateAsync();
        }
        // else: OnLoaded will navigate with the latest _pointsJson.
    }

    /// <summary>
    /// Returns the catalog ids of every marker currently inside the map
    /// viewport. Used by the host's "Filter grid to this view" toolbar action.
    /// </summary>
    public async Task<IReadOnlyList<int>> GetVisibleIdsAsync()
    {
        if (!_coreInitialized || !_firstNavigationDone) return Array.Empty<int>();
        try
        {
            var json = await _webView.CoreWebView2.ExecuteScriptAsync("getVisibleIds()");
            return ParseIdArray(json);
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_coreInitialized) return;
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _coreInitialized = true;

            var settings = _webView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;

            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            await NavigateAsync();
        }
        catch
        {
            // Evergreen runtime missing or blocked. Leave the control empty.
        }
    }

    private Task NavigateAsync()
    {
        if (!_coreInitialized) return Task.CompletedTask;
        try
        {
            var html = ResolvedTemplate
                .Replace("__LAT__", FallbackLatitude.ToString("R", CultureInfo.InvariantCulture))
                .Replace("__LON__", FallbackLongitude.ToString("R", CultureInfo.InvariantCulture))
                .Replace("__POINTS__", _pointsJson);
            _webView.CoreWebView2.NavigateToString(html);
            _firstNavigationDone = true;
        }
        catch
        {
            // Core torn down mid-navigation; ignore.
        }
        return Task.CompletedTask;
    }

    private async Task PushPointsAsync()
    {
        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync($"setPoints({_pointsJson});");
        }
        catch
        {
            // Core torn down mid-update; ignore.
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Treat every inbound message as untrusted: parse defensively and
        // swallow malformed payloads so the page can't crash the window.
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("type", out var typeEl)) return;

            switch (typeEl.GetString())
            {
                case "select":
                    if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id))
                    {
                        ClipSelected?.Invoke(this, id);
                    }
                    break;

                case "filter":
                    if (root.TryGetProperty("ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
                    {
                        var ids = new List<int>(idsEl.GetArrayLength());
                        foreach (var el in idsEl.EnumerateArray())
                        {
                            if (el.TryGetInt32(out var v)) ids.Add(v);
                        }
                        if (ids.Count > 0)
                        {
                            FilterToClipsRequested?.Invoke(this, ids);
                        }
                    }
                    break;
            }
        }
        catch
        {
            // Unparseable / unexpected payload — ignore silently.
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _webView.Dispose();
        }
        catch
        {
            // Disposal can race with pending navigations on tear-down.
        }
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri,
                UseShellExecute = true,
            });
        }
        catch
        {
            // No usable shell handler for http/https — silently no-op.
        }
    }

    // Serialises points to a JS array literal of {id, lat, lon} objects
    // (invariant culture so decimals don't localise). Hand-rolled to keep the
    // payload compact for large catalogs.
    private static string BuildPointsJson(IReadOnlyList<MapClipPoint> points)
    {
        if (points.Count == 0) return "[]";

        var sb = new StringBuilder(points.Count * 40 + 2);
        sb.Append('[');
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var p = points[i];
            sb.Append("{\"id\":")
              .Append(p.Id.ToString(CultureInfo.InvariantCulture))
              .Append(",\"lat\":")
              .Append(p.Latitude.ToString("R", CultureInfo.InvariantCulture))
              .Append(",\"lon\":")
              .Append(p.Longitude.ToString("R", CultureInfo.InvariantCulture))
              .Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static IReadOnlyList<int> ParseIdArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<int>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<int>();
            var ids = new List<int>(doc.RootElement.GetArrayLength());
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.TryGetInt32(out var v)) ids.Add(v);
            }
            return ids;
        }
        catch
        {
            return Array.Empty<int>();
        }
    }
}
