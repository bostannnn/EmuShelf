namespace EmuShelf.App.Services;

/// <summary>The controller pose a game carries with it while departing the shelf centre.</summary>
public readonly record struct PhysicalShelfDeparturePose(long GameKey, float Yaw, float Pitch);
