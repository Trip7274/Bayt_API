using System.Text.Json;

namespace Bayt_API;

public static class RequestChecking
{

	public sealed record RequestCheckResult(string RequestBody, string? ErrorMessage);

	public static async Task<RequestCheckResult> CheckContType(HttpContext context)
	{
		string? errorMessage;

		if (context.Request.ContentLength == 0)
		{
			errorMessage = "ContentLength is zero, please include a JSON array of mount points in the request body, e.g. [\"/mnt/hdd\", \"/mnt/ssd\"].";
		}
		else if (context.Request.ContentType != "application/json")
		{
			errorMessage = $"Content-Type is not application/json, current Content-Type header: '{context.Request.ContentType}'.";
		}
		else
		{
			errorMessage = null;
		}

		string requestBody;
		using (var reader = new StreamReader(context.Request.Body))
		{
			requestBody = await reader.ReadToEndAsync();
		}

		return new RequestCheckResult(requestBody, errorMessage);
	}

	public static async Task<T?> ValidateAndDeserializeJsonBody<T>(HttpContext context, bool throwOnEmptyBody = true)
	{
		if (context.Request.ContentType != "application/json")
		{
			throw new BadHttpRequestException($"Content-Type is not 'application/json'. Current Content-Type header: '{context.Request.ContentType}'.");
		}

		string requestBody;
		using (var reader = new StreamReader(context.Request.Body))
		{
			requestBody = await reader.ReadToEndAsync();
		}

		if (throwOnEmptyBody && string.IsNullOrWhiteSpace(requestBody))
		{
			throw new BadHttpRequestException("Request body is empty.");
		}

		T? deserializedBody;
		try
		{
			deserializedBody = JsonSerializer.Deserialize<T>(requestBody);
		}
		catch (JsonException e)
		{
			throw new BadHttpRequestException($"Was unable to process request JSON. Error message: {e.Message}");
		}

		return deserializedBody;
	}
}