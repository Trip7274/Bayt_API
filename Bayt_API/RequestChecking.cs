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
	/// <param name="containerId">A potentially null string to ensure it's not null and >= 12 characters</param>
	/// <param name="updateContainers">Whether to update the <see cref="Docker.DockerContainers"/> list by using
	/// <see cref="Docker.DockerContainers.UpdateDataIfNecessary"/>.</param>
	/// <exception cref="ArgumentException">The provided string was either null or shorter than 12 characters.</exception>
	/// <exception cref="FileNotFoundException">Docker is not available on this system.</exception>
	/// <remarks>
	///	The exception messages will have the appropriate user-facing error message.
	/// </remarks>
	public static async Task ValidateDockerRequest([NotNull] string? containerId, bool updateContainers = true)
	{
		if (containerId is null)
		{
			throw new ArgumentException("Container ID must not be null.", nameof(containerId));
		}

		if (containerId.Length < 12)
		{
			throw new ArgumentException("Container ID must be at least 12 characters long.", nameof(containerId));
		}

		if (!Docker.IsDockerAvailable)
		{
			throw new FileNotFoundException("Docker is not available on this system.");
		}

		if (!updateContainers) return;
		await Docker.DockerContainers.UpdateDataIfNecessary();
	}
}