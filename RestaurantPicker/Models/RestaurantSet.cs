namespace RestaurantPicker.Models;

public class RestaurantSet
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public bool IsDefault => Id == 1;
}
