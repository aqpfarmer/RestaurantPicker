# Restaurant Picker

Restaurant Picker is a WinUI 3 desktop app that demonstrates MVVM with a SQLite backend.

## Features

- CRUD operations for restaurants (name and type required).
- Wheel-style UI that spins to select a random restaurant.
- Prompt flow when fewer than 3 restaurants exist.
- Ability to spin again or accept the selected restaurant.

## Tech Stack

- C# / .NET (WinUI 3)
- MVVM via CommunityToolkit.Mvvm
- SQLite via Microsoft.Data.Sqlite
- Windows App SDK 2.1.3

## Project Structure

- `RestaurantPicker/Models` data entities.
- `RestaurantPicker/ViewModels` MVVM presentation logic.
- `RestaurantPicker/Services` SQLite CRUD access.
- `RestaurantPicker/Views` XAML pages and UI.

## Build and Run

1. Install .NET SDK 10.
2. From the workspace root, build:
	`C:/Program Files/dotnet/dotnet.exe build ./RestaurantPicker/RestaurantPicker.csproj`
3. For a distributable unpackaged build that does not require machine-installed .NET or Windows App Runtime, publish:
	`C:/Program Files/dotnet/dotnet.exe publish ./RestaurantPicker/RestaurantPicker.csproj -c Release -r win-x64`
4. Run the published executable from the publish folder.

## Notes

- The app targets `net10.0-windows10.0.19041.0`.
- The project is configured for self-contained unpackaged publish on `win-x64`.
- The published output bundles the required .NET and Windows App SDK runtime files.
- SQLite native initialization is performed during app startup.
