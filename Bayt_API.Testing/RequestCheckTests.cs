using Microsoft.AspNetCore.Http;

namespace Bayt_API.Testing;

public class RequestCheckTests
{
	[Fact]
	public async Task TestRequestChecking()
    {
		var goodContext = new DefaultHttpContext
		{
			Request =
			{
				ContentType = "application/json",
				ContentLength = 100
			}
		};

		var badContext = new DefaultHttpContext
		{
			Request =
			{
				ContentType = "",
				ContentLength = 0
			}
		};

		Assert.Null((await RequestChecking.CheckContType(goodContext)).ErrorMessage);
		Assert.NotNull((await RequestChecking.CheckContType(badContext)).ErrorMessage);
	}
}