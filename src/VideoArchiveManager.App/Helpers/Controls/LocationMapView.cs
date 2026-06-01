using System.Globalization;
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
/// WebView2 init failures (older runtime missing, sandboxed environment)
/// are swallowed silently — the control simply renders empty, and the
/// "Open in map" hyperlink in the clip-info popup still works as a
/// fallback. Tile fetches need internet; offline users see the dark
/// body background only.
/// </para>
/// </summary>
public class LocationMapView : UserControl
{
    private readonly WebView2 _webView;
    private bool _coreInitialized;
    private bool _firstNavigationDone;
    private bool _updatePending;

    // Regional-view fallback used when the bound clip has no GPS — keeps
    // the sidebar layout stable instead of collapsing the map area on
    // every non-geotagged clip. Haugesund, NO (≈ author's home town).
    private const double FallbackLatitude = 59.4138;
    private const double FallbackLongitude = 5.2680;

    // Leaflet + OSM page template. __LAT__ / __LON__ / __HAS__ are
    // replaced with invariant-culture-formatted values before the first
    // navigation. Attribution is included inline per OSM's tile usage
    // policy. The dark body background matches the app's dark theme so
    // the panel doesn't flash white while tiles load.
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
<link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
<script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
<style>
  html, body, #m { height: 100%; margin: 0; padding: 0; }
  body { background: #1e1e1e; }
  .leaflet-container { background: #1e1e1e; }
  body.no-location .leaflet-tile-pane { filter: grayscale(1) brightness(0.55); }
  body.no-location .leaflet-control-attribution { opacity: 0.55; }
</style>
</head>
<body>
<div id="m"></div>
<script>
  var map = null, marker = null;
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
  setLocation(__LAT__, __LON__, __HAS__);
</script>
</body>
</html>
""";

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

            QueueUpdate();
        }
        catch
        {
            // Evergreen runtime missing or blocked. Leave the control
            // empty; parent visibility binding plus the clip-info popup's
            // "Open in map" hyperlink keep the workflow functional.
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
                var html = MapHtmlTemplate
                    .Replace("__LAT__", latText)
                    .Replace("__LON__", lonText)
                    .Replace("__HAS__", hasText);
                _webView.CoreWebView2.NavigateToString(html);
                _firstNavigationDone = true;
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
