using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace VideoArchiveManager.App.Helpers.Controls;

/// <summary>
/// Sidebar location preview: hosts a Leaflet map on OpenStreetMap tiles
/// inside a WebView2 control, centered on the bound GPS coordinates.
///
/// <para>
/// Designed for the right-sidebar editor in <c>MainWindow.xaml</c>. The
/// map is always rendered: when <see cref="Latitude"/> / <see cref="Longitude"/>
/// are set the page zooms in (z=13) and drops a marker; when they're both
/// null the page falls back to the author's home town (Haugesund, NO) at
/// a wider regional zoom (z=11), hides the marker, and grayscales /
/// darkens the tile pane so the panel reads as "no GPS data" while still
/// preserving layout. The map page is loaded once via <c>NavigateToString</c>;
/// subsequent coordinate changes call the page's
/// <c>setLocation(lat, lon, hasLocation)</c> function via
/// <c>ExecuteScriptAsync</c> so the marker pans rather than the whole
/// Leaflet stack reinitialising on every selection change.
/// </para>
/// <para>
/// When <see cref="IsPickingMode"/> is true the page installs a click
/// handler on the Leaflet map and posts the picked <c>(lat, lon)</c>
/// back to the host via <c>chrome.webview.postMessage</c>; the host
/// then raises <see cref="LocationPicked"/>. This is how the manual GPS
/// picker in the sidebar editor lets the user assign coordinates to
/// clips that have no embedded location data.
/// </para>
/// <para>
/// Leaflet 1.9.4 (CSS + JS) and the marker PNGs are embedded in the
/// assembly and inlined into the page at runtime, so the map library
/// itself needs no network access. Only the OSM <em>tiles</em> are
/// fetched online: offline users get the Leaflet chrome + marker over
/// the dark body background, and the manual GPS picker still works
/// (clicks resolve to coordinates without any tiles loaded).
/// </para>
/// <para>
/// WebView2 init failures (older runtime missing, sandboxed environment)
/// are swallowed silently — the control simply renders empty, and the
/// "Open in map" hyperlink in the clip-info popup still works as a
/// fallback.
/// </para>
/// </summary>
public class LocationMapView : UserControl
{
    private readonly WebView2 _webView;
    private bool _coreInitialized;
    private bool _firstNavigationDone;
    private bool _updatePending;
    private bool _pickModePending;

    // Regional-view fallback used when the bound clip has no GPS — keeps
    // the sidebar layout stable instead of collapsing the map area on
    // every non-geotagged clip. Haugesund, NO (≈ author's home town).
    private const double FallbackLatitude = 59.4138;
    private const double FallbackLongitude = 5.2680;

    // Leaflet + OSM page template. The bundled Leaflet 1.9.4 CSS and JS are
    // injected inline (no CDN) via the __LEAFLET_CSS__ / __LEAFLET_JS__ tokens
    // and the marker PNGs via __MARKER_*__ data URIs — see BuildResolvedTemplate.
    // __LAT__ / __LON__ / __HAS__ are then replaced with invariant-culture-
    // formatted values before each navigation. Tile attribution is included
    // inline per OSM's tile usage policy. The dark body background matches the
    // app's dark theme so the panel doesn't flash white while tiles load.
    //
    // When `hasLocation` is false the page (a) drops to a regional zoom,
    // (b) removes the marker, and (c) toggles a `no-location` class on
    // <body> that grayscales + dims the tile pane. The control itself
    // is still alive and (technically) interactive so the user can pan
    // / zoom around if they're curious; it just visually de-emphasises
    // the area to communicate "we don't know where this clip was shot".
    private const string MapHtmlTemplate = """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1.0">
<style>__LEAFLET_CSS__</style>
<style>
  html, body, #m { height: 100%; margin: 0; padding: 0; }
  body { background: #1e1e1e; }
  .leaflet-container { background: #1e1e1e; }
  body.no-location .leaflet-tile-pane { filter: grayscale(1) brightness(0.55); }
  body.no-location .leaflet-control-attribution { opacity: 0.55; }
  body.pick-mode .leaflet-container { cursor: crosshair !important; }
</style>
</head>
<body>
<div id="m"></div>
<script>__LEAFLET_JS__</script>
<script>
  // Point Leaflet's default marker at the bundled base64 images so the
  // pin renders with zero network access. Deleting _getIconUrl disables
  // Leaflet's relative-path auto-detection (which has no base URL under
  // NavigateToString and would otherwise produce a broken icon request).
  delete L.Icon.Default.prototype._getIconUrl;
  L.Icon.Default.mergeOptions({
    iconUrl: '__MARKER_ICON__',
    iconRetinaUrl: '__MARKER_ICON_2X__',
    shadowUrl: '__MARKER_SHADOW__'
  });
  var map = null, marker = null;
  var pickHandler = null;
  var pickModeRequested = false;
  function setLocation(lat, lon, hasLocation) {
    var zoom = hasLocation ? 13 : 11;
    if (!map) {
      map = L.map('m', { zoomControl: true, attributionControl: true }).setView([lat, lon], zoom);
      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
      }).addTo(map);
      marker = L.marker([lat, lon]);
      if (hasLocation) marker.addTo(map);
      // Host may have requested pick mode before the map existed
      // (race during first NavigateToString); apply the pending state now.
      if (pickModeRequested) setPickMode(true);
    } else {
      map.setView([lat, lon], zoom);
      marker.setLatLng([lat, lon]);
      if (hasLocation) {
        marker.addTo(map);
      } else {
        map.removeLayer(marker);
      }
    }
    document.body.classList.toggle('no-location', !hasLocation);
  }
  function setPickMode(enabled) {
    pickModeRequested = enabled;
    document.body.classList.toggle('pick-mode', enabled);
    if (!map) return;
    if (enabled && !pickHandler) {
      pickHandler = function(e) {
        try {
          window.chrome.webview.postMessage(JSON.stringify({
            type: 'pick',
            lat: e.latlng.lat,
            lon: e.latlng.lng
          }));
        } catch (err) { /* host disconnected */ }
      };
      map.on('click', pickHandler);
    } else if (!enabled && pickHandler) {
      map.off('click', pickHandler);
      pickHandler = null;
    }
  }
  setLocation(__LAT__, __LON__, __HAS__);
</script>
</body>
</html>
""";

    // MapHtmlTemplate with the bundled Leaflet CSS / JS / marker images
    // baked in, leaving only the per-navigation __LAT__ / __LON__ / __HAS__
    // tokens. Built once on first use (the assets are constant) and reused
    // for every navigation; failure to load a resource is fatal at dev time
    // (the resource names are wrong) rather than a silent runtime fallback.
    private static string? _resolvedTemplate;

    private static string ResolvedTemplate =>
        _resolvedTemplate ??= MapHtmlTemplate
            .Replace("__LEAFLET_CSS__", LoadTextResource("Leaflet.leaflet.css"))
            .Replace("__LEAFLET_JS__", LoadTextResource("Leaflet.leaflet.js"))
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
        typeof(LocationMapView).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"Embedded Leaflet resource '{logicalName}' not found. " +
                "Check the <EmbeddedResource LogicalName=...> entries in VideoArchiveManager.App.csproj.");

    public static readonly DependencyProperty LatitudeProperty =
        DependencyProperty.Register(
            nameof(Latitude),
            typeof(double?),
            typeof(LocationMapView),
            new PropertyMetadata(null, OnCoordinateChanged));

    public static readonly DependencyProperty LongitudeProperty =
        DependencyProperty.Register(
            nameof(Longitude),
            typeof(double?),
            typeof(LocationMapView),
            new PropertyMetadata(null, OnCoordinateChanged));

    public static readonly DependencyProperty IsPickingModeProperty =
        DependencyProperty.Register(
            nameof(IsPickingMode),
            typeof(bool),
            typeof(LocationMapView),
            new PropertyMetadata(false, OnIsPickingModeChanged));

    public double? Latitude
    {
        get => (double?)GetValue(LatitudeProperty);
        set => SetValue(LatitudeProperty, value);
    }

    public double? Longitude
    {
        get => (double?)GetValue(LongitudeProperty);
        set => SetValue(LongitudeProperty, value);
    }

    /// <summary>
    /// When true, the embedded Leaflet page installs a click handler that
    /// posts <c>(lat, lon)</c> back to the host on every map click and
    /// raises <see cref="LocationPicked"/>. Driven by the VM's
    /// <c>IsPickingLocation</c> while the user is assigning a manual GPS
    /// location to a clip without embedded coordinates.
    /// </summary>
    public bool IsPickingMode
    {
        get => (bool)GetValue(IsPickingModeProperty);
        set => SetValue(IsPickingModeProperty, value);
    }

    /// <summary>
    /// Raised when the user clicks the map while <see cref="IsPickingMode"/>
    /// is true. Subscribers receive the lat/lon picked.
    /// </summary>
    public event EventHandler<LocationPickedEventArgs>? LocationPicked;

    public LocationMapView()
    {
        _webView = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1E, 0x1E, 0x1E),
        };
        Content = _webView;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnCoordinateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LocationMapView self)
        {
            self.QueueUpdate();
        }
    }

    private static void OnIsPickingModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LocationMapView self)
        {
            self.ApplyPickMode((bool)e.NewValue);
        }
    }

    // Forward the IsPickingMode DP into the embedded page. If the WebView2
    // core isn't initialized yet we cache the desired state in
    // _pickModePending and apply it once OnLoaded finishes; the JS side
    // also defers internally if the Leaflet map isn't constructed yet,
    // so we cover both startup orderings.
    private async void ApplyPickMode(bool enabled)
    {
        _pickModePending = enabled;
        if (!_coreInitialized || !_firstNavigationDone) return;
        try
        {
            var script = enabled ? "setPickMode(true);" : "setPickMode(false);";
            await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch
        {
            // Stale call during tear-down; ignore.
        }
    }

    // Coalesce Lat / Lon changes into a single map update. When the bound
    // VideoItemViewModel switches, WPF sets Latitude and Longitude back-to-back
    // on the same dispatcher turn; debouncing to Background priority lets both
    // assignments land before we issue a NavigateToString / ExecuteScriptAsync,
    // avoiding a brief visible pan to (newLat, oldLon).
    private void QueueUpdate()
    {
        if (_updatePending) return;
        _updatePending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _updatePending = false;
            UpdateMap();
        }), DispatcherPriority.Background);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_coreInitialized) return;
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _coreInitialized = true;

            // Strip browser-y affordances: this is a passive preview, not
            // a navigable surface. Users open the full OSM page via the
            // existing "Open in map" hyperlink in the clip-info popup.
            var settings = _webView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;

            // Route any in-page navigation (e.g. clicking the OSM
            // attribution link) to the user's default browser instead of
            // hijacking the embedded map view.
            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            // Inbound JS-to-host bridge for the manual GPS picker. The
            // embedded page posts {type:"pick", lat, lon} on map click
            // when pick mode is active; we forward as a LocationPicked
            // event so the VM can update its PendingLatitude/Longitude.
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            QueueUpdate();
        }
        catch
        {
            // Evergreen runtime missing or blocked. Leave the control
            // empty; parent visibility binding plus the clip-info popup's
            // "Open in map" hyperlink keep the workflow functional.
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Treat every inbound message as untrusted: parse defensively,
        // validate the type, range-check the coords, and swallow any
        // exceptions so a malformed payload can't crash the editor.
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            if (typeEl.GetString() != "pick") return;
            if (!root.TryGetProperty("lat", out var latEl)) return;
            if (!root.TryGetProperty("lon", out var lonEl)) return;
            if (!latEl.TryGetDouble(out var lat)) return;
            if (!lonEl.TryGetDouble(out var lon)) return;
            if (!double.IsFinite(lat) || !double.IsFinite(lon)) return;
            if (lat < -90 || lat > 90 || lon < -180 || lon > 180) return;

            LocationPicked?.Invoke(this, new LocationPickedEventArgs(lat, lon));
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
            // Disposal can race with pending navigations on tear-down;
            // swallow rather than crash the editor pane.
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

    private async void UpdateMap()
    {
        if (!_coreInitialized) return;

        // Fall back to Haugesund when GPS is missing so the panel always
        // shows *something*; the JS layer takes the hasLocation flag and
        // grayscales / hides the marker so the user can still tell the
        // clip doesn't actually carry coordinates.
        var hasLocation = Latitude.HasValue && Longitude.HasValue;
        var lat = Latitude ?? FallbackLatitude;
        var lon = Longitude ?? FallbackLongitude;

        var latText = lat.ToString("R", CultureInfo.InvariantCulture);
        var lonText = lon.ToString("R", CultureInfo.InvariantCulture);
        var hasText = hasLocation ? "true" : "false";

        try
        {
            if (!_firstNavigationDone)
            {
                var html = ResolvedTemplate
                    .Replace("__LAT__", latText)
                    .Replace("__LON__", lonText)
                    .Replace("__HAS__", hasText);
                _webView.CoreWebView2.NavigateToString(html);
                _firstNavigationDone = true;

                // If pick mode was requested before the page existed
                // (race during a fast click on the "+ Set location" CTA
                // right after selection), forward it now so the click
                // handler installs on the very first idle frame.
                if (_pickModePending)
                {
                    ApplyPickMode(true);
                }
            }
            else
            {
                var script = $"setLocation({latText}, {lonText}, {hasText});";
                await _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
        }
        catch
        {
            // CoreWebView2 can be torn down mid-update (control unloaded);
            // ignore stale calls.
        }
    }
}

/// <summary>
/// Carries the picked latitude / longitude from
/// <see cref="LocationMapView.LocationPicked"/>.
/// </summary>
public sealed class LocationPickedEventArgs : EventArgs
{
    public LocationPickedEventArgs(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }
    public double Longitude { get; }
}
