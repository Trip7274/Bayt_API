using Xunit.Abstractions;

namespace Bayt_API.UnitTests;

public class GlobalsTests(ITestOutputHelper testOutputHelper)
{
	[Fact]
	public void SemverScheme()
	{
		testOutputHelper.WriteLine($"API Version: {ApiConfig.ApiVersion}\n" +
		                           $"Version: {ApiConfig.Version}\n" +
		                           $"Processed Characters: {ApiConfig.ApiVersion} == {ApiConfig.Version[..ApiConfig.Version.IndexOf('.')]}");
		Assert.Equal(ApiConfig.ApiVersion, byte.Parse(ApiConfig.Version[..ApiConfig.Version.IndexOf('.')]));
	}
}