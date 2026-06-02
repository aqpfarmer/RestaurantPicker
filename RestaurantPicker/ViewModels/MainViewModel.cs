using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPicker.Models;
using RestaurantPicker.Services;

namespace RestaurantPicker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IRestaurantRepository _repository;
    private readonly Random _random = new();

    private Restaurant _currentRestaurant;
    private Restaurant? _selectedRestaurant;
    private Restaurant? _spinResult;
    private Restaurant? _pendingSpinResult;
    private string _statusMessage;
    private bool _needsMinimumRestaurants;
    private bool _isSpinning;
    private double _spinTargetAngle;
    private int _selectedSliceIndex;

    public event EventHandler<string>? SelectionAccepted;

    public Restaurant CurrentRestaurant
    {
        get => _currentRestaurant;
        set => SetProperty(ref _currentRestaurant, value);
    }

    public Restaurant? SelectedRestaurant
    {
        get => _selectedRestaurant;
        set
        {
            if (SetProperty(ref _selectedRestaurant, value))
            {
                EditSelectedRestaurantCommand.NotifyCanExecuteChanged();
                DeleteSelectedRestaurantCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Restaurant? SpinResult
    {
        get => _spinResult;
        set => SetProperty(ref _spinResult, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsSpinning
    {
        get => _isSpinning;
        set
        {
            if (SetProperty(ref _isSpinning, value))
            {
                SpinWheelCommand.NotifyCanExecuteChanged();
                AcceptSelectionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool NeedsMinimumRestaurants
    {
        get => _needsMinimumRestaurants;
        set => SetProperty(ref _needsMinimumRestaurants, value);
    }

    public double SpinTargetAngle
    {
        get => _spinTargetAngle;
        set => SetProperty(ref _spinTargetAngle, value);
    }

    public int SelectedSliceIndex
    {
        get => _selectedSliceIndex;
        set => SetProperty(ref _selectedSliceIndex, value);
    }

    public ObservableCollection<Restaurant> Restaurants { get; } = new();

    public bool CanSpin => Restaurants.Count >= 3;
    public bool CanSpinWheel => CanSpin && !IsSpinning;
    public bool CanAcceptSelection => SpinResult is not null && !IsSpinning;

    public MainViewModel(IRestaurantRepository repository)
    {
        _repository = repository;
        _currentRestaurant = new Restaurant();
        _statusMessage = "Load restaurants to begin.";
        _selectedSliceIndex = -1;
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await _repository.InitializeAsync();
        await ReloadRestaurantsAsync();
    }

    [RelayCommand]
    public async Task SaveRestaurantAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentRestaurant.Name) || string.IsNullOrWhiteSpace(CurrentRestaurant.RestaurantType))
        {
            StatusMessage = "Name and restaurant type are required.";
            return;
        }

        if (CurrentRestaurant.Id == 0)
        {
            var addedId = await _repository.AddAsync(CurrentRestaurant);
            CurrentRestaurant.Id = addedId;
            StatusMessage = "Restaurant added.";
        }
        else
        {
            await _repository.UpdateAsync(CurrentRestaurant);
            StatusMessage = "Restaurant updated.";
        }

        CurrentRestaurant = new Restaurant();
        await ReloadRestaurantsAsync();
    }

    [RelayCommand]
    public void EditSelectedRestaurant()
    {
        if (SelectedRestaurant is null)
        {
            StatusMessage = "Select a restaurant to edit.";
            return;
        }

        CurrentRestaurant = new Restaurant
        {
            Id = SelectedRestaurant.Id,
            Name = SelectedRestaurant.Name,
            RestaurantType = SelectedRestaurant.RestaurantType,
            Address = SelectedRestaurant.Address,
            WebsiteUrl = SelectedRestaurant.WebsiteUrl
        };

        StatusMessage = "Editing selected restaurant.";
    }

    [RelayCommand]
    public async Task DeleteSelectedRestaurantAsync()
    {
        if (SelectedRestaurant is null)
        {
            StatusMessage = "Select a restaurant to delete.";
            return;
        }

        await _repository.DeleteAsync(SelectedRestaurant.Id);
        StatusMessage = "Restaurant deleted.";
        SelectedRestaurant = null;
        await ReloadRestaurantsAsync();
    }

    [RelayCommand]
    public void ClearForm()
    {
        CurrentRestaurant = new Restaurant();
        StatusMessage = "Form cleared.";
    }

    [RelayCommand(CanExecute = nameof(CanSpinWheel))]
    public void SpinWheel()
    {
        if (!CanSpin)
        {
            NeedsMinimumRestaurants = true;
            StatusMessage = "Add at least 3 restaurants to spin.";
            return;
        }

        SelectedSliceIndex = _random.Next(Restaurants.Count);
        _pendingSpinResult = Restaurants[SelectedSliceIndex];
        SpinResult = null;
        IsSpinning = true;

        var sliceAngle = 360.0 / Restaurants.Count;
        var selectedSliceCenter = (SelectedSliceIndex * sliceAngle) + (sliceAngle / 2.0);
        var pointerAngle = 270.0;
        var desiredAngle = (pointerAngle - selectedSliceCenter + 360.0) % 360.0;

        // Add full rotations for animation appeal and include previous target to keep momentum.
        SpinTargetAngle += (6 * 360.0) + desiredAngle;
        StatusMessage = "Spinning wheel...";
    }

    public void CompleteSpin()
    {
        if (!IsSpinning)
        {
            return;
        }

        IsSpinning = false;
        SpinResult = _pendingSpinResult;
        _pendingSpinResult = null;

        if (SpinResult is not null)
        {
            StatusMessage = $"Wheel stopped on: {SpinResult.Name}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanAcceptSelection))]
    public void AcceptSelection()
    {
        if (IsSpinning)
        {
            StatusMessage = "Wait for the wheel to stop before accepting.";
            return;
        }

        if (SpinResult is null)
        {
            StatusMessage = "Spin first to choose a restaurant.";
            return;
        }

        StatusMessage = $"Accepted: {SpinResult.Name} ({SpinResult.RestaurantType}).";
        SelectionAccepted?.Invoke(this, $"You chose {SpinResult.Name} ({SpinResult.RestaurantType}).");
    }

    private async Task ReloadRestaurantsAsync()
    {
        var allRestaurants = await _repository.GetAllAsync();

        Restaurants.Clear();
        foreach (var restaurant in allRestaurants)
        {
            Restaurants.Add(restaurant);
        }

        NeedsMinimumRestaurants = Restaurants.Count < 3;
        if (NeedsMinimumRestaurants)
        {
            StatusMessage = "Add at least 3 restaurants before spinning.";
        }

        OnPropertyChanged(nameof(CanSpin));
        SpinWheelCommand.NotifyCanExecuteChanged();
        AcceptSelectionCommand.NotifyCanExecuteChanged();
    }
}
