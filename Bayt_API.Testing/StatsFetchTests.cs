namespace Bayt_API.Testing;

public class StatsFetchTests
{
	[Fact]
	public void MemoryDataFetching()
	{
		Assert.IsType<StatsApi.MemoryData>(StatsApi.GetMemoryData()); // Mostly to check the method if it crashes or not
	}

	[Fact]
	public void GpuDataFetching()
	{
		Assert.IsType<GpuHandling.GpuData>(GpuHandling.GetGpuDataList()[0]); // Exception testing
	}

	[Fact]
	public void CpuDataFetching()
	{
		Assert.IsType<StatsApi.CpuData>(StatsApi.GetCpuData()); // Exception testing
	}
	// This feels like it needs more testing, but I'm not sure what to test
}