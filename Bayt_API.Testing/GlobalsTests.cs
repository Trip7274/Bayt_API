using Xunit.Abstractions;

namespace Bayt_API.Testing;

public class GlobalsTests(ITestOutputHelper testOutputHelper)
{
	[Fact]
	public void SemverScheme()
	{
		testOutputHelper.WriteLine($"API Version: {ApiConfig.ApiVersion}\nVersion: {ApiConfig.Version}\nProcessed Characters: {ApiConfig.ApiVersion} == {ApiConfig.Version[..ApiConfig.Version.IndexOf('.')]}");
		Assert.Equal(ApiConfig.ApiVersion, byte.Parse(ApiConfig.Version[..ApiConfig.Version.IndexOf('.')]));
	}
}