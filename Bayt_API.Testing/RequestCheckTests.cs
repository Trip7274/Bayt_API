using Microsoft.AspNetCore.Http;

namespace Bayt_API.Testing;

public class RequestCheckTests
{
	[Fact]
	public async Task TestLegacyRequestChecking()
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

	[Fact]
	public async Task TestJsonRequestCheckingAndProcessing()
	{
		MemoryStream goodBody = new("{ \"This should\": \"work!\" }"u8.ToArray());
		MemoryStream badBody = new("this shouldn't work"u8.ToArray());
		MemoryStream emptyBody = new(""u8.ToArray());

		var goodContext = new DefaultHttpContext
		{
			Request =
			{
				ContentType = "application/json",
				Body = goodBody
			}
		};

		var badContextContentType = new DefaultHttpContext
		{
			Request =
			{
				ContentType = "something/weird",
				Body = goodBody
			}
		};

		var badContextBody = new DefaultHttpContext
		{
			Request =
			{
				ContentType = "application/json",
				Body = badBody
			}
		};

		var emptyContextBody = new DefaultHttpContext
		{
			Request =
			{
				ContentType = "application/json",
				Body = emptyBody
			}
		};

		Assert.IsType<Dictionary<string, string>>(await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, string>>(goodContext));

		// This is kind of a mess
		bool contentTypeThrew = false;
		try
		{
			await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, string>>(badContextContentType);
		}
		catch (BadHttpRequestException)
		{
			contentTypeThrew = true;
		}

		bool invalidBodyThrew = false;
		try
		{
			await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, string>>(badContextBody);
		}
		catch (BadHttpRequestException)
		{
			invalidBodyThrew = true;
		}

		bool emptyBodyThrew = false;
		try
		{
			await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, string>>(emptyContextBody);
		}
		catch (BadHttpRequestException)
		{
			emptyBodyThrew = true;
		}

		Assert.True(contentTypeThrew);
		Assert.True(invalidBodyThrew);
		Assert.True(emptyBodyThrew);
	}
}