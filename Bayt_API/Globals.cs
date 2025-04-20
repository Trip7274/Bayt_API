namespace Bayt_API;

public static class Globals
{
	public const string Version = "0.4.3";
	public const byte ApiVersion = 1;

	public static ushort SecondsToUpdate = 5; // TODO: Add some way to change this using the API.

	public static DateTime LastUpdated { get; set; }
}