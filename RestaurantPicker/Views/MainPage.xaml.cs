using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
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

    private readonly MainViewModel _viewModel;

    public MainPage()
    {
        InitializeComponent();
        AppVersionTextBlock.Text = $"Version {AppVersion}";
        AppAuthorTextBlock.Text = $"Author: {AppAuthor}";

        var appDataDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RestaurantPicker");
        Directory.CreateDirectory(appDataDir);
        var dbPath = System.IO.Path.Combine(appDataDir, "restaurantpicker.db");
        _viewModel = new MainViewModel(new SqliteRestaurantRepository(dbPath));
        DataContext = _viewModel;

        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        _viewModel.Restaurants.CollectionChanged += RestaurantsOnCollectionChanged;
        Loaded += MainPage_Loaded;
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

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
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
