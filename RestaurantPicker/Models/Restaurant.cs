namespace RestaurantPicker.Models;

public class Restaurant
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RestaurantType { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? WebsiteUrl { get; set; }

    public bool HasWebsite => !string.IsNullOrWhiteSpace(WebsiteUrl);
}
