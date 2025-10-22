using System.Diagnostics.CodeAnalysis;

namespace Bayt_API;

public static class RequestChecking
{
	/// <summary>
	///		Ensures that the given container ID is not null, is longer than 12 characters, and Docker is available on this system. This is used to de-duplicate code from Docker endpoints.
	/// </summary>
	///	<param name="id">A potentially null string to ensure it's not null and is >= 12 characters</param>
	/// <param name="updateContainers">
	///		Whether to update the <see cref="Docker.DockerContainers"/> list by using
	///     <see cref="Docker.DockerContainers.UpdateDataIfNecessary"/>.
	/// </param>
	/// <param name="checkCompose">Whether to check if Docker-Compose exists and is executable or not.</param>
	/// <param name="updateImages">
	///		Whether to update the <see cref="Docker.ImagesInfo"/> list by using
	///		<see cref="Docker.ImagesInfo.UpdateDataIfNecessary"/>.
	/// </param>
	/// <returns>
	///		An IResult object if the request is invalid, otherwise null.
	/// </returns>
	/// <remarks>
	/// 	The returned IResult should be returned to the user as it contains the appropriate status code and error message.
	/// </remarks>
	public static async Task<IResult?> ValidateDockerRequest([NotNull] string? id, bool updateContainers = true, bool checkCompose = false, bool updateImages = false)
	{
		if (id is null)
		{
			id = "123456789012"; // To satisfy the compiler
			return Results.BadRequest("ID must not be null.");
		}

		if (id.Length < 12)
		{
			return Results.BadRequest("ID must be at least 12 characters long.");
		}

		if (!Docker.IsDockerAvailable)
		{
			return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
		}
		if (checkCompose && !Docker.IsDockerComposeAvailable)
		{
			return Results.InternalServerError("Docker-Compose is not available on this system or the Docker integration was disabled.");
		}

		if (updateContainers) await Docker.DockerContainers.UpdateDataIfNecessary();
		if (updateImages) await Docker.ImagesInfo.UpdateDataIfNecessary();
		return null;
	}
}