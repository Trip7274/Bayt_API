namespace Bayt_API.Testing;

public class CachingTests
{
	[Fact]
	public void FreshDataKeep()
	{
		ApiConfig.LastUpdated = DateTime.Now.AddSeconds(-5);
		ApiConfig.MainConfigs.ConfigProps.SecondsToUpdate = 30;
		Assert.True(Caching.IsDataFresh()); // Doesn't trigger data update
	}

	[Fact]
	public void StaleDataRefresh()
	{
		ApiConfig.LastUpdated = DateTime.Now.AddSeconds(-ApiConfig.MainConfigs.ConfigProps.SecondsToUpdate * 2);
		Assert.False(Caching.IsDataFresh()); // Triggers data update
	}

	[Fact]
	public void NoCacheKeep()
	{
		ApiConfig.LastUpdated = DateTime.Now;
		ApiConfig.MainConfigs.ConfigProps.SecondsToUpdate = 0;
		Assert.False(Caching.IsDataFresh()); // Triggers data update
	}
}