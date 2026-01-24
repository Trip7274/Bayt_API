using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Bayt_API.Endpoints.DockerEndpoints.Local;

public static class DlContaintersEndpoints
{
	internal static readonly string BaseDockerUrl = ApiConfig.BaseApiUrlPath + "/docker";

	public static void MapDlContaintersEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet($"{BaseDockerUrl}/containers/getList", async (bool all = true) =>
		{
			if (!DockerLocal.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
			await DockerLocal.DockerContainers.UpdateDataIfNecessary();


			return Results.Json(DockerLocal.DockerContainers.ToDictionary(all));
		}).Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Fetch all or only the currently active containers in the system.")
			.WithTags("Docker")
			.WithName("GetDockerContainers");

		app.MapPost($"{BaseDockerUrl}/containers/start", async (string? containerId) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
			if (requestValidation is not null) return requestValidation;

			var targetContainer = DockerLocal.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

			return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
				: await targetContainer.Start();
		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status304NotModified)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Issues a command to start a specific Docker container.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
			.WithTags("Docker")
			.WithName("StartDockerContainer");

		app.MapPost($"{BaseDockerUrl}/containers/stop", async (string? containerId) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
			if (requestValidation is not null) return requestValidation;

			var targetContainer = DockerLocal.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

			return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
				: await targetContainer.Stop();
		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status304NotModified)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Issues a command to stop a specific Docker container.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
			.WithTags("Docker")
			.WithName("StopDockerContainer");

		app.MapPost($"{BaseDockerUrl}/containers/restart", async (string? containerId) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
			if (requestValidation is not null) return requestValidation;

			var targetContainer = DockerLocal.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

			return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
				: await targetContainer.Restart();
		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status304NotModified)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Issues a command to restart a specific Docker container.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
			.WithTags("Docker")
			.WithName("RestartDockerContainer");

		app.MapPost($"{BaseDockerUrl}/containers/kill", async (string? containerId) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
			if (requestValidation is not null) return requestValidation;

			var targetContainer = DockerLocal.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

			return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
				: await targetContainer.Kill();
		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status409Conflict)
			.Produces(StatusCodes.Status304NotModified)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Issues a command to kill a specific Docker container.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
			.WithTags("Docker")
			.WithName("KillDockerContainer");

		app.MapDelete($"{BaseDockerUrl}/containers/delete", async (string? containerId, bool removeCompose = false, bool removeVolumes = false, bool force = false) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
			if (requestValidation is not null) return requestValidation;

			var targetContainer = DockerLocal.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

			return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
				: await targetContainer.Delete(removeCompose, removeVolumes, force);
		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status409Conflict)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Issues a command to delete a specific Docker container.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID. " +
			                 "removeCompose will delete the entire compose directory if true, " +
			                 "and removeVolumes will remove all anonymous volumes associated with the container if true. " +
			                 "Both default to false.")
			.WithTags("Docker")
			.WithName("DeleteDockerContainer");

		app.MapPost($"{BaseDockerUrl}/containers/pause", async (string? containerId) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
			if (requestValidation is not null) return requestValidation;

			var targetContainer = DockerLocal.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

			return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
				: await targetContainer.Pause();
		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status304NotModified)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Issues a command to pause a specific Docker container.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
			.WithTags("Docker")
			.WithName("PauseDockerContainer");

		app.MapPost($"{BaseDockerUrl}/containers/resume", async (string? containerId) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
			if (requestValidation is not null) return requestValidation;

			var targetContainer = DockerLocal.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

			return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
				: await targetContainer.Unpause();
		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status304NotModified)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Issues a command to resume a specific Docker container.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
			.WithTags("Docker")
			.WithName("ResumeDockerContainer");

		app.MapGet($"{BaseDockerUrl}/containers/streamLogs", async (CancellationToken cancellationToken, string? containerId, bool stdout = true, bool stderr = true, bool timestamps = false) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
			if (requestValidation is not null)
			{
				return requestValidation;
			}
			if (DockerLocal.DockerContainers.Containers.All(container => !container.Id.StartsWith(containerId)))
			{
				return Results.NotFound("Container not found.");
			}

			return Results.ServerSentEvents(DockerLocal.StreamDockerLogs(containerId, stdout, stderr, timestamps, cancellationToken));
		}).Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status404NotFound)
			.WithSummary("Stream the Docker container's live logs.")
			.WithDescription("containerId is required. stdout, stderr, and timestamps are optional to specify which streams to follow, and whether to prefix each line with a timestamp. Will default to stdout=true, stderr=true, and timestamps=false if not specified.")
			.WithTags("Docker")
			.WithName("GetDockerContainerLogs");

		app.MapDelete($"{BaseDockerUrl}/containers/prune", async () =>
		{
			if (!DockerLocal.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");

			var dockerRequest = await DockerLocal.SendRequest("containers/prune", HttpMethod.Post);

			return dockerRequest.Status switch
			{
				HttpStatusCode.OK => Results.Text(dockerRequest.Body, "application/json", Encoding.UTF8, StatusCodes.Status200OK),
				HttpStatusCode.InternalServerError => Results.InternalServerError(
					$"Docker returned an error while pruning containers. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
				_ => Results.InternalServerError(
					$"Docker returned an unknown error while pruning containers. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
			};
		}).Produces(StatusCodes.Status200OK)
			.Produces(StatusCodes.Status500InternalServerError)
			.WithSummary("Prune all stopped Docker containers.")
			.WithDescription("This will delete all stopped Docker containers.")
			.WithTags("Docker")
			.WithName("PruneDockerContainers");

		app.MapGet($"{BaseDockerUrl}/containers/getStats", async (string? containerId) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
			if (requestValidation is not null) return requestValidation;

			DockerLocal.DockerContainer targetContainer;
			try
			{
				targetContainer = DockerLocal.DockerContainers.Containers.First(container => container.Id.StartsWith(containerId));
			}
			catch (InvalidOperationException)
			{
				return Results.NotFound($"Container with ID '{containerId}' was not found.");
			}

			return targetContainer.Stats is not null ? Results.Json(targetContainer.Stats.ToDictionary())
				: Results.BadRequest($"This container is not running. ({targetContainer.State})");
		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Fetch the usage stats of a specific Docker container.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
			.WithTags("Docker")
			.WithName("GetDockerContainerStats");

		app.MapPost($"{BaseDockerUrl}/containers/setMetadata", async (string? containerId, [FromBody] Dictionary<string, string?> metadata) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId, true, true);
			if (requestValidation is not null) return requestValidation;

			metadata = metadata.Where(pair => DockerLocal.DockerContainer.SupportedLabels.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
			if (metadata.Count == 0) return Results.BadRequest("No valid properties were provided. Please include one of: PrettyName, Notes, PreferredIconUrl, or WebpageLink");

			var targetContainer = DockerLocal.DockerContainers.Containers.First(container => container.Id.StartsWith(containerId));
			if (!targetContainer.IsManaged) return Results.BadRequest("This container is not managed by Bayt.");

			bool changesMade = await targetContainer.SetContainerMetadata(metadata);

			return changesMade ? Results.NoContent() : Results.StatusCode(StatusCodes.Status304NotModified);
		}).Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status304NotModified)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Set the metadata of a Docker container, such as PrettyName, Notes, PreferredIconUrl, or WebpageLink.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID. A dictionary<string, string> object is expected in the body in the format: ({ 'metadataKey': 'metadataValue' })")
			.WithTags("Docker")
			.WithName("SetDockerContainerMetadata");
	}
}