using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Bayt_API;

public static class RequestChecking
{
	public static async Task<T?> ValidateAndDeserializeJsonBody<T>(HttpContext context, bool throwOnEmptyBody = true)
	{
		if (context.Request.ContentType != "application/json" && throwOnEmptyBody)
		{
			throw new BadHttpRequestException(
				$"Content-Type is not 'application/json'. Current Content-Type header: '{context.Request.ContentType}'.");
		}

		string requestBody;
		using (var reader = new StreamReader(context.Request.Body))
		{
			requestBody = await reader.ReadToEndAsync();
		}

		switch (throwOnEmptyBody)
		{
			case true when string.IsNullOrWhiteSpace(requestBody):
				throw new BadHttpRequestException("Request body is empty.");
			case false when string.IsNullOrWhiteSpace(requestBody):
				throw new EndOfStreamException(
					"The code should handle this."); // This is REALLY janky and kinda contradictory
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

	/// <summary>
	/// Ensures that the given container ID is not null, is longer than 12 characters, and Docker is available on this system. This is used to de-duplicate code from Docker endpoints.
	/// </summary>
	/// <param name="containerId">A potentially null string to ensure it's not null and is >= 12 characters</param>
	/// <param name="updateContainers">Whether to update the <see cref="Docker.DockerContainers"/> list by using
	/// <see cref="Docker.DockerContainers.UpdateDataIfNecessary"/>.</param>
	/// <returns>
	///	An IResult object if the request is invalid, otherwise null.
	/// </returns>
	/// <remarks>
	///	The returned IResult should be returned to the user as it contains the appropriate status code and error message.
	/// </remarks>
	public static async Task<IResult?> ValidateDockerRequest([NotNull] string? containerId, bool updateContainers = true)
	{
		if (containerId is null)
		{
			containerId = "123456789012"; // To satisfy the compiler
			return Results.BadRequest("Container ID must not be null.");
		}

		if (containerId.Length < 12)
		{
			return Results.BadRequest("Container ID must be at least 12 characters long.");
		}

		if (!Docker.IsDockerAvailable)
		{
			return Results.InternalServerError("Docker is not available on this system.");
		}

		if (updateContainers) await Docker.DockerContainers.UpdateDataIfNecessary();
		return null;
	}
}