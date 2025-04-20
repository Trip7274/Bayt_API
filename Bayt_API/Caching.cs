namespace Bayt_API;

public static class Caching
{
	public static bool IsDataStale()
	{
		return Globals.LastUpdated.AddSeconds(Globals.SecondsToUpdate) >= DateTime.Now;
	}
}