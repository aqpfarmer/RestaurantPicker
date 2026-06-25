using RestaurantPicker.Models;

namespace RestaurantPicker.Services;

public interface IRestaurantRepository
{
    Task InitializeAsync();
    Task<IReadOnlyList<Restaurant>> GetAllAsync();
    Task<int> AddAsync(Restaurant restaurant);
    Task UpdateAsync(Restaurant restaurant);
    Task DeleteAsync(int id);

    Task<IReadOnlyList<RestaurantSet>> GetAllSetsAsync();
    Task<int> AddSetAsync(string name, IEnumerable<int> restaurantIds);
    Task DeleteSetAsync(int setId);
    Task<IReadOnlyList<Restaurant>> GetRestaurantsBySetAsync(int setId);
}
