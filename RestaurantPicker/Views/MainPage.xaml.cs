using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Web.WebView2.Core;
using RestaurantPicker.Models;
using RestaurantPicker.Services;
using RestaurantPicker.ViewModels;
using Windows.System;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace RestaurantPicker.Views;

public sealed partial class MainPage : Page
{
    private const string AppAuthor = "Chris Murphy";
    private const string AppVersion = "1.0.0";
    private const double NearbyRadiusMiles = 5.0;
    private const int NearbyRadiusMeters = 8047;
    private const string GoogleMapsApiKeyEnvironmentVariable = "GOOGLE_MAPS_API_KEY";
    private const string GoogleMapsApiKeySettingName = "GoogleMapsApiKey";

    private readonly MainViewModel _viewModel;
    private readonly string _settingsFilePath;
    private bool _isProcessingMapSelection;

    public MainPage()
    {
        InitializeComponent();
        AppVersionTextBlock.Text = $"Version {AppVersion}";
        AppAuthorTextBlock.Text = $"Author: {AppAuthor}";

        var appDataDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RestaurantPicker");
        Directory.CreateDirectory(appDataDir);
        var dbPath = System.IO.Path.Combine(appDataDir, "restaurantpicker.db");
        _settingsFilePath = System.IO.Path.Combine(appDataDir, "settings.json");
        _viewModel = new MainViewModel(new SqliteRestaurantRepository(dbPath));
        DataContext = _viewModel;

        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        _viewModel.Restaurants.CollectionChanged += RestaurantsOnCollectionChanged;
        Loaded += MainPage_Loaded;

        GoogleMapsApiKeyTextBox.Text = GetSavedGoogleMapsApiKey();
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
            DrawWheel();

            if (_viewModel.NeedsMinimumRestaurants)
            {
                await ShowNeedRestaurantsDialogAsync();
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Startup failed: {ex.Message}";

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Startup Error",
                Content = ex.ToString(),
                CloseButtonText = "OK"
            };

            await dialog.ShowAsync();
        }
    }

    private void RestaurantsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DrawWheel();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SpinTargetAngle))
        {
            AnimateWheel(_viewModel.SpinTargetAngle);
        }

        if (e.PropertyName == nameof(MainViewModel.SpinResult))
        {
            ResultWebsiteLink.Visibility =
                string.IsNullOrWhiteSpace(_viewModel.SpinResult?.WebsiteUrl)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }
    }

    private void DrawWheel()
    {
        WheelCanvas.Children.Clear();

        var restaurants = _viewModel.Restaurants;
        if (restaurants.Count == 0)
        {
            return;
        }

        var colors = new[]
        {
            "#F44336", "#FF9800", "#FFEB3B", "#8BC34A", "#00BCD4", "#3F51B5", "#E91E63", "#795548"
        };

        const double centerX = 250;
        const double centerY = 250;
        const double radius = 230;
        var currentRotationDegrees = NormalizeAngleDegrees(WheelRotateTransform.Angle);
        var currentRotationRadians = currentRotationDegrees * Math.PI / 180.0;
        var sliceAngle = 360.0 / restaurants.Count;

        for (var i = 0; i < restaurants.Count; i++)
        {
            var startAngle = i * sliceAngle;
            var endAngle = startAngle + sliceAngle;

            var path = new ShapePath
            {
                Fill = new SolidColorBrush(ConvertHexColor(colors[i % colors.Length])),
                Stroke = new SolidColorBrush(ConvertHexColor("#1F1F1F")),
                StrokeThickness = 1.5,
                Data = CreateSliceGeometry(centerX, centerY, radius, startAngle, endAngle)
            };

            WheelCanvas.Children.Add(path);

            var midAngleRadians = ((startAngle + endAngle) / 2.0) * Math.PI / 180.0;

            if (restaurants.Count >= 10)
            {
                DrawRadialLabel(
                    restaurants[i].Name,
                    centerX,
                    centerY,
                    radius,
                    midAngleRadians,
                    currentRotationRadians,
                    currentRotationDegrees);
            }
            else
            {
                DrawHorizontalLabel(
                    restaurants[i].Name,
                    centerX,
                    centerY,
                    radius,
                    midAngleRadians,
                    currentRotationDegrees);
            }
        }
    }

    private void DrawHorizontalLabel(
        string text,
        double centerX,
        double centerY,
        double radius,
        double midAngleRadians,
        double currentRotationDegrees)
    {
        var labelRadius = radius * 0.62;
        var labelX = centerX + Math.Cos(midAngleRadians) * labelRadius;
        var labelY = centerY + Math.Sin(midAngleRadians) * labelRadius;

        var textBlock = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(ConvertHexColor("#111111")),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Width = 120,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWholeWords,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new RotateTransform { Angle = -currentRotationDegrees }
        };

        Canvas.SetLeft(textBlock, labelX - 60);
        Canvas.SetTop(textBlock, labelY - 15);
        WheelCanvas.Children.Add(textBlock);
    }

    private void DrawRadialLabel(
        string text,
        double centerX,
        double centerY,
        double radius,
        double midAngleRadians,
        double currentRotationRadians,
        double currentRotationDegrees)
    {
        var safeText = string.IsNullOrWhiteSpace(text) ? "?" : text.Trim();
        var characters = safeText.ToCharArray();

        const double outerPadding = 16;
        const double minInnerRadiusFactor = 0.22;
        const double charHeightFactor = 0.95;
        const double maxOuterFontSize = 18;
        const double minOuterFontSize = 8;
        const double minInnerFontSize = 5;
        const double innerFontScale = 0.62;

        var outerRadius = radius - outerPadding;
        var minInnerRadius = radius * minInnerRadiusFactor;
        var availableSpan = outerRadius - minInnerRadius;
        var characterCount = characters.Length;

        var outerFontSize = maxOuterFontSize;
        var innerFontSize = Math.Max(minInnerFontSize, outerFontSize * innerFontScale);

        while (outerFontSize > minOuterFontSize &&
               EstimateRadialLabelSpan(characterCount, outerFontSize, innerFontSize, charHeightFactor) > availableSpan)
        {
            outerFontSize -= 0.5;
            innerFontSize = Math.Max(minInnerFontSize, outerFontSize * innerFontScale);
        }

        var displayedAngleRadians = midAngleRadians + currentRotationRadians;
        var isLowerHalf = Math.Sin(displayedAngleRadians) > 0;
        var radialRotation = (displayedAngleRadians * 180.0 / Math.PI) + 90.0;
        if (isLowerHalf)
        {
            radialRotation += 180;
        }

        radialRotation -= currentRotationDegrees;

        var characterHeights = new double[characterCount];
        for (var index = 0; index < characterCount; index++)
        {
            var progressFromOuter = characterCount == 1
                ? 0.0
                : isLowerHalf
                    ? (double)(characterCount - 1 - index) / (characterCount - 1)
                    : (double)index / (characterCount - 1);

            var fontSize = Lerp(outerFontSize, innerFontSize, progressFromOuter);
            characterHeights[index] = fontSize * charHeightFactor;
        }

        var radiusCursor = isLowerHalf
            ? minInnerRadius + (characterHeights[0] / 2.0)
            : outerRadius - (characterHeights[0] / 2.0);

        for (var index = 0; index < characterCount; index++)
        {
            var progressFromOuter = characterCount == 1
                ? 0.0
                : isLowerHalf
                    ? (double)(characterCount - 1 - index) / (characterCount - 1)
                    : (double)index / (characterCount - 1);

            var fontSize = Lerp(outerFontSize, innerFontSize, progressFromOuter);
            var charX = centerX + Math.Cos(midAngleRadians) * radiusCursor;
            var charY = centerY + Math.Sin(midAngleRadians) * radiusCursor;

            var characterBlock = new TextBlock
            {
                Text = characters[index].ToString(),
                Foreground = new SolidColorBrush(ConvertHexColor("#111111")),
                FontSize = fontSize,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = radialRotation }
            };

            Canvas.SetLeft(characterBlock, charX - (fontSize * 0.35));
            Canvas.SetTop(characterBlock, charY - (fontSize * 0.55));
            WheelCanvas.Children.Add(characterBlock);

            if (index < characterCount - 1)
            {
                var step = (characterHeights[index] / 2.0) + (characterHeights[index + 1] / 2.0);
                radiusCursor += isLowerHalf ? step : -step;
            }
        }
    }

    private static double EstimateRadialLabelSpan(int characterCount, double outerFontSize, double innerFontSize, double charHeightFactor)
    {
        if (characterCount <= 0)
        {
            return 0;
        }

        var span = 0.0;
        for (var index = 0; index < characterCount; index++)
        {
            var t = characterCount == 1 ? 0.0 : (double)index / (characterCount - 1);
            span += Lerp(outerFontSize, innerFontSize, t) * charHeightFactor;
        }

        return span;
    }

    private static double Lerp(double start, double end, double t)
    {
        return start + ((end - start) * t);
    }

    private static double NormalizeAngleDegrees(double angle)
    {
        return ((angle % 360.0) + 360.0) % 360.0;
    }

    private static Geometry CreateSliceGeometry(double centerX, double centerY, double radius, double startAngle, double endAngle)
    {
        var startRadians = startAngle * Math.PI / 180.0;
        var endRadians = endAngle * Math.PI / 180.0;

        var startPoint = new Windows.Foundation.Point(
            centerX + Math.Cos(startRadians) * radius,
            centerY + Math.Sin(startRadians) * radius);

        var endPoint = new Windows.Foundation.Point(
            centerX + Math.Cos(endRadians) * radius,
            centerY + Math.Sin(endRadians) * radius);

        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(centerX, centerY),
            IsClosed = true,
            Segments =
            {
                new LineSegment { Point = startPoint },
                new ArcSegment
                {
                    Point = endPoint,
                    Size = new Windows.Foundation.Size(radius, radius),
                    SweepDirection = SweepDirection.Clockwise,
                    IsLargeArc = (endAngle - startAngle) > 180
                }
            }
        };

        return new PathGeometry { Figures = { figure } };
    }

    private void AnimateWheel(double targetAngle)
    {
        var animation = new DoubleAnimation
        {
            To = targetAngle,
            Duration = TimeSpan.FromMilliseconds(3200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            _viewModel.CompleteSpin();
            DrawWheel();
        };
        Storyboard.SetTarget(animation, WheelRotateTransform);
        Storyboard.SetTargetProperty(animation, "Angle");
        storyboard.Begin();
    }

    private static Windows.UI.Color ConvertHexColor(string hex)
    {
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        byte a = 255;
        byte r = 0;
        byte g = 0;
        byte b = 0;

        if (hex.Length == 6)
        {
            r = Convert.ToByte(hex[0..2], 16);
            g = Convert.ToByte(hex[2..4], 16);
            b = Convert.ToByte(hex[4..6], 16);
        }
        else if (hex.Length == 8)
        {
            a = Convert.ToByte(hex[0..2], 16);
            r = Convert.ToByte(hex[2..4], 16);
            g = Convert.ToByte(hex[4..6], 16);
            b = Convert.ToByte(hex[6..8], 16);
        }

        return Windows.UI.Color.FromArgb(a, r, g, b);
    }

    private async Task ShowNeedRestaurantsDialogAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add Restaurants",
            Content = "Please add at least 3 restaurants (name and type are required) before spinning the wheel.",
            CloseButtonText = "OK"
        };

        await dialog.ShowAsync();
    }

    private async void ResultWebsiteLink_Click(object sender, RoutedEventArgs e)
    {
        await LaunchWebsiteFromSenderAsync(sender);
    }

    private async void RestaurantWebsiteLink_Click(object sender, RoutedEventArgs e)
    {
        await LaunchWebsiteFromSenderAsync(sender);
    }

    private async void MapButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowMapPickerDialogAsync();
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }

        private async Task ShowMapPickerDialogAsync()
        {
            var apiKey = ResolveGoogleMapsApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                        var missingKeyDialog = new ContentDialog
                        {
                                XamlRoot = XamlRoot,
                                Title = "Google Maps API Key Required",
                    Content = "Enter a key in the Google Maps API Key field and click Save Map Key, or set GOOGLE_MAPS_API_KEY.",
                                CloseButtonText = "OK"
                        };

                        await missingKeyDialog.ShowAsync();
                        return;
                }

                var mapWebView = new WebView2
                {
                        MinWidth = 900,
                        MinHeight = 600
                };

                mapWebView.CoreWebView2Initialized += (_, initArgs) =>
                {
                    if (initArgs.Exception is not null || mapWebView.CoreWebView2 is null)
                    {
                        return;
                    }

                    mapWebView.CoreWebView2.WebMessageReceived += MapWebViewOnWebMessageReceived;
                };
                mapWebView.NavigateToString(CreateMapPickerHtml(apiKey));

                var dialog = new ContentDialog
                {
                        XamlRoot = XamlRoot,
                        Title = $"Nearby Restaurants ({NearbyRadiusMiles:0} miles)",
                        PrimaryButtonText = "Close",
                        DefaultButton = ContentDialogButton.Primary,
                        Content = mapWebView,
                        FullSizeDesired = true,
                        MaxWidth = 1200,
                        MaxHeight = 860
                };

                await dialog.ShowAsync();
                if (mapWebView.CoreWebView2 is not null)
                {
                    mapWebView.CoreWebView2.WebMessageReceived -= MapWebViewOnWebMessageReceived;
                }
        }

            private async void MapWebViewOnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
                if (_isProcessingMapSelection)
                {
                    return;
                }

                MapSelectionPayload? selection;
                try
                {
                        selection = JsonSerializer.Deserialize<MapSelectionPayload>(args.WebMessageAsJson);
                }
                catch (JsonException)
                {
                        return;
                }

                if (selection is null || string.IsNullOrWhiteSpace(selection.Name))
                {
                        return;
                }

                _isProcessingMapSelection = true;

                try
                {
                    var restaurant = new Restaurant
                    {
                        Name = selection.Name.Trim(),
                        RestaurantType = DetermineRestaurantType(selection.RestaurantType),
                        Address = string.IsNullOrWhiteSpace(selection.Address) ? null : selection.Address.Trim(),
                        WebsiteUrl = string.IsNullOrWhiteSpace(selection.WebsiteUrl) ? null : selection.WebsiteUrl.Trim()
                    };

                    var addDialog = new ContentDialog
                    {
                        XamlRoot = XamlRoot,
                        Title = "Add restaurant from map?",
                        Content = BuildMapSelectionDialogContent(restaurant),
                        PrimaryButtonText = "Add Restaurant",
                        CloseButtonText = "Keep Searching"
                    };

                    var addResult = await addDialog.ShowAsync();
                    if (addResult != ContentDialogResult.Primary)
                    {
                        _viewModel.StatusMessage = "Map selection canceled. Continue searching.";
                        return;
                    }

                    if (IsPotentialDuplicate(restaurant))
                    {
                        var duplicateDialog = new ContentDialog
                        {
                            XamlRoot = XamlRoot,
                            Title = "Possible Duplicate",
                            Content = "A restaurant with the same name (and matching address when available) already exists. Add anyway?",
                            PrimaryButtonText = "Add Anyway",
                            CloseButtonText = "Cancel"
                        };

                        var duplicateResult = await duplicateDialog.ShowAsync();
                        if (duplicateResult != ContentDialogResult.Primary)
                        {
                            _viewModel.StatusMessage = "Duplicate not added. Continue searching.";
                            return;
                        }
                    }

                    _viewModel.CurrentRestaurant = restaurant;
                    await _viewModel.SaveRestaurantAsync();
                    _viewModel.StatusMessage = $"Added from map: {restaurant.Name}";
                }
                finally
                {
                    _isProcessingMapSelection = false;
                }
            }

            private UIElement BuildMapSelectionDialogContent(Restaurant restaurant)
            {
                return new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = restaurant.Name,
                            FontSize = 18,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                        },
                        new TextBlock { Text = $"Type: {restaurant.RestaurantType}" },
                        new TextBlock { Text = string.IsNullOrWhiteSpace(restaurant.Address) ? "Address: (none)" : $"Address: {restaurant.Address}" },
                        new TextBlock { Text = string.IsNullOrWhiteSpace(restaurant.WebsiteUrl) ? "Website: (none)" : $"Website: {restaurant.WebsiteUrl}" }
                    }
                };
            }

            private bool IsPotentialDuplicate(Restaurant candidate)
            {
                var candidateName = NormalizeForComparison(candidate.Name);
                var candidateAddress = NormalizeForComparison(candidate.Address);

                return _viewModel.Restaurants.Any(existing =>
                {
                    var existingName = NormalizeForComparison(existing.Name);
                    if (!string.Equals(existingName, candidateName, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    var existingAddress = NormalizeForComparison(existing.Address);
                    if (string.IsNullOrWhiteSpace(existingAddress) || string.IsNullOrWhiteSpace(candidateAddress))
                    {
                        return true;
                    }

                    return string.Equals(existingAddress, candidateAddress, StringComparison.OrdinalIgnoreCase);
                });
            }

            private static string NormalizeForComparison(string? value)
            {
                return string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : value.Trim().ToUpperInvariant();
            }

            private void SaveMapApiKeyButton_Click(object sender, RoutedEventArgs e)
            {
                var key = GoogleMapsApiKeyTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    _viewModel.StatusMessage = "Map API key is empty.";
                    return;
                }

                SaveSetting(GoogleMapsApiKeySettingName, key);
                _viewModel.StatusMessage = "Map API key saved locally.";
            }

            private void ClearMapApiKeyButton_Click(object sender, RoutedEventArgs e)
            {
                RemoveSetting(GoogleMapsApiKeySettingName);

                GoogleMapsApiKeyTextBox.Text = string.Empty;
                _viewModel.StatusMessage = "Saved map API key cleared.";
            }

            private string GetSavedGoogleMapsApiKey()
            {
                return ReadSetting(GoogleMapsApiKeySettingName);
            }

            private string? ResolveGoogleMapsApiKey()
            {
                var savedKey = GetSavedGoogleMapsApiKey();
                if (!string.IsNullOrWhiteSpace(savedKey))
                {
                    return savedKey;
                }

                return Environment.GetEnvironmentVariable(GoogleMapsApiKeyEnvironmentVariable);
            }

            private string ReadSetting(string key)
            {
                try
                {
                    var settings = ReadSettings();
                    return settings.TryGetValue(key, out var value) ? value : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }

            private void SaveSetting(string key, string value)
            {
                try
                {
                    var settings = ReadSettings();
                    settings[key] = value;
                    WriteSettings(settings);
                }
                catch
                {
                    _viewModel.StatusMessage = "Unable to save map API key.";
                }
            }

            private void RemoveSetting(string key)
            {
                try
                {
                    var settings = ReadSettings();
                    if (settings.Remove(key))
                    {
                        WriteSettings(settings);
                    }
                }
                catch
                {
                    _viewModel.StatusMessage = "Unable to clear saved map API key.";
                }
            }

            private Dictionary<string, string> ReadSettings()
            {
                if (!File.Exists(_settingsFilePath))
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                var json = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return parsed ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            private void WriteSettings(Dictionary<string, string> settings)
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json, Encoding.UTF8);
            }

        private static string DetermineRestaurantType(string? value)
        {
                if (string.IsNullOrWhiteSpace(value))
                {
                        return "Restaurant";
                }

                var normalized = value
                        .Replace('_', ' ')
                        .Trim();

                if (normalized.Length == 0)
                {
                        return "Restaurant";
                }

                return char.ToUpperInvariant(normalized[0]) + normalized[1..];
        }

        private static string CreateMapPickerHtml(string apiKey)
        {
                var escapedApiKey = JsonSerializer.Serialize(apiKey);

                return $$"""
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Restaurant Map Picker</title>
    <style>
        html, body {
            margin: 0;
            padding: 0;
            height: 100%;
            font-family: Segoe UI, sans-serif;
            background: #f2f4f7;
        }
        #layout {
            display: grid;
            grid-template-columns: 320px 1fr;
            height: 100%;
        }
        #sidebar {
            border-right: 1px solid #d9dce1;
            overflow-y: auto;
            background: #ffffff;
            padding: 12px;
            box-sizing: border-box;
        }
        #sidebar h3 {
            margin: 0 0 6px 0;
            font-size: 16px;
        }
        #sidebar p {
            margin: 0 0 10px 0;
            color: #586172;
            font-size: 13px;
        }
        .item {
            border: 1px solid #e1e4ea;
            border-radius: 10px;
            padding: 8px;
            margin-bottom: 8px;
            background: #fbfcfe;
            cursor: pointer;
        }
        .item:hover {
            border-color: #5d8cff;
            background: #f4f8ff;
        }
        .title {
            font-weight: 600;
            font-size: 14px;
            color: #1f2937;
            margin-bottom: 4px;
        }
        .address {
            font-size: 12px;
            color: #4b5563;
        }
        #map {
            height: 100%;
            width: 100%;
        }
        #status {
            font-size: 12px;
            color: #4b5563;
            margin-top: 4px;
        }
    </style>
</head>
<body>
    <div id="layout">
        <div id="sidebar">
            <h3>Nearby Restaurants</h3>
            <p>Showing places within 5 miles of your location.</p>
            <div id="status">Locating you...</div>
            <div id="results"></div>
        </div>
        <div id="map"></div>
    </div>

    <script>
        const apiKey = {{escapedApiKey}};
        let map;
        let userPosition;
        let infoWindow;
        const markers = [];
        let placesService;

        function setStatus(message) {
            const status = document.getElementById("status");
            status.textContent = message;
        }

        function clearMarkers() {
            while (markers.length > 0) {
                const marker = markers.pop();
                marker.setMap(null);
            }
        }

        function addResultItem(place) {
            const results = document.getElementById("results");
            const item = document.createElement("div");
            item.className = "item";

            const title = document.createElement("div");
            title.className = "title";
            title.textContent = place.name || "Unnamed Restaurant";

            const address = document.createElement("div");
            address.className = "address";
            address.textContent = place.vicinity || place.formatted_address || "Address unavailable";

            item.appendChild(title);
            item.appendChild(address);
            item.addEventListener("click", () => selectPlace(place));

            results.appendChild(item);
        }

        function populateResults(places) {
            const results = document.getElementById("results");
            results.innerHTML = "";

            if (!places || places.length === 0) {
                setStatus("No restaurants found in 5 miles.");
                return;
            }

            setStatus(`Found ${places.length} restaurants. Click one to add.`);

            places.forEach(place => {
                addResultItem(place);

                if (!place.geometry || !place.geometry.location) {
                    return;
                }

                const marker = new google.maps.Marker({
                    map,
                    position: place.geometry.location,
                    title: place.name
                });

                marker.addListener("click", () => selectPlace(place));
                markers.push(marker);
            });
        }

        function selectPlace(place) {
            const request = {
                placeId: place.place_id,
                fields: ["name", "formatted_address", "website", "types"]
            };

            placesService.getDetails(request, (details, status) => {
                const chosen = status === google.maps.places.PlacesServiceStatus.OK && details
                    ? details
                    : place;

                const name = chosen.name || "Unnamed Restaurant";
                const address = chosen.formatted_address || chosen.vicinity || "";
                const websiteUrl = chosen.website || "";
                const restaurantType = (chosen.types && chosen.types.length > 0) ? chosen.types[0] : "restaurant";

                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage({
                        name,
                        address,
                        websiteUrl,
                        restaurantType
                    });
                }
            });
        }

        function searchNearbyRestaurants() {
            const request = {
                location: userPosition,
                radius: {{NearbyRadiusMeters}},
                type: "restaurant"
            };

            placesService.nearbySearch(request, (results, status) => {
                clearMarkers();
                if (status !== google.maps.places.PlacesServiceStatus.OK) {
                    setStatus("Unable to load nearby restaurants.");
                    return;
                }

                populateResults(results);
            });
        }

        function initMapAtPosition(position) {
            userPosition = {
                lat: position.coords.latitude,
                lng: position.coords.longitude
            };

            map = new google.maps.Map(document.getElementById("map"), {
                center: userPosition,
                zoom: 13,
                mapTypeControl: false,
                streetViewControl: false
            });

            new google.maps.Marker({
                map,
                position: userPosition,
                title: "Your location",
                icon: {
                    path: google.maps.SymbolPath.CIRCLE,
                    scale: 8,
                    fillColor: "#2563eb",
                    fillOpacity: 1,
                    strokeColor: "#ffffff",
                    strokeWeight: 2
                }
            });

            new google.maps.Circle({
                map,
                center: userPosition,
                radius: {{NearbyRadiusMeters}},
                fillColor: "#3b82f633",
                strokeColor: "#2563eb",
                strokeWeight: 1
            });

            infoWindow = new google.maps.InfoWindow();
            placesService = new google.maps.places.PlacesService(map);
            setStatus("Searching nearby restaurants...");
            searchNearbyRestaurants();
        }

        function fallbackToDefaultLocation() {
            setStatus("Location unavailable. Showing a default map area.");
            initMapAtPosition({ coords: { latitude: 47.6062, longitude: -122.3321 } });
        }

        function initMap() {
            if (!navigator.geolocation) {
                fallbackToDefaultLocation();
                return;
            }

            navigator.geolocation.getCurrentPosition(
                initMapAtPosition,
                fallbackToDefaultLocation,
                { enableHighAccuracy: true, timeout: 10000, maximumAge: 300000 }
            );
        }
    </script>
    <script async defer src="https://maps.googleapis.com/maps/api/js?key=${apiKey}&libraries=places&callback=initMap"></script>
</body>
</html>
""";
        }

        private sealed class MapSelectionPayload
        {
                public string? Name { get; init; }
                public string? Address { get; init; }
                public string? WebsiteUrl { get; init; }
                public string? RestaurantType { get; init; }
        }

    private async Task LaunchWebsiteFromSenderAsync(object sender)
    {
        var url = _viewModel.SpinResult?.WebsiteUrl;
        if (sender is FrameworkElement { Tag: string itemUrl } && !string.IsNullOrWhiteSpace(itemUrl))
        {
            url = itemUrl;
        }

        var uri = CreateWebsiteUri(url);
        if (uri is null)
        {
            return;
        }

        await Launcher.LaunchUriAsync(uri);
    }

    private static Uri? CreateWebsiteUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate($"https://{url}", UriKind.Absolute, out uri))
            {
                return null;
            }
        }

        return uri;
    }
}
