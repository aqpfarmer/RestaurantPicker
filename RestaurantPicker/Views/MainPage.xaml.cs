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
    private readonly MainViewModel _viewModel;

    public MainPage()
    {
        InitializeComponent();

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
            var labelRadius = radius * 0.62;
            var labelX = centerX + Math.Cos(midAngleRadians) * labelRadius;
            var labelY = centerY + Math.Sin(midAngleRadians) * labelRadius;

            var textBlock = new TextBlock
            {
                Text = restaurants[i].Name,
                Foreground = new SolidColorBrush(ConvertHexColor("#111111")),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Width = 120,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.WrapWholeWords
            };

            Canvas.SetLeft(textBlock, labelX - 60);
            Canvas.SetTop(textBlock, labelY - 15);
            WheelCanvas.Children.Add(textBlock);
        }
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
        var url = _viewModel.SpinResult?.WebsiteUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        await Launcher.LaunchUriAsync(uri);
    }
}
