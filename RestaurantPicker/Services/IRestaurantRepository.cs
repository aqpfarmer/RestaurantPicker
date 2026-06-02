using RestaurantPicker.Models;

namespace RestaurantPicker.Services;

public interface IRestaurantRepository
{
    Task InitializeAsync();
    Task<IReadOnlyList<Restaurant>> GetAllAsync();
    Task<int> AddAsync(Restaurant restaurant);
    Task UpdateAsync(Restaurant restaurant);
    Task DeleteAsync(int id);
}
