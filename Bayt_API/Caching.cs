namespace Bayt_API;

public static class Caching
{
	public static bool IsDataStale()
	{
		return ApiConfig.LastUpdated.AddSeconds(ApiConfig.MainConfigs.ConfigProps.SecondsToUpdate) > DateTime.Now;
	}
}