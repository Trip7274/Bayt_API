using System.Net;
using System.Text.Json;

namespace Bayt_API.Endpoints.DockerEndpoints.Local;

public static class DlComposeEndpoints
{
	public static void MapDlComposeEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet($"{DlContaintersEndpoints.BaseDockerUrl}/containers/getCompose", async (string? containerId) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId, true, true);
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
			if (targetContainer.ComposePath is null)
				return Results.NotFound($"Container with ID '{containerId}' does not have a compose file.");

			var stream = new FileStream(targetContainer.ComposePath, FileMode.Open, FileAccess.Read, FileShare.Read);

			return Results.File(stream, "application/ocetet-stream", Path.GetFileName(targetContainer.ComposePath),
				File.GetLastWriteTime(targetContainer.ComposePath));

		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Fetch the compose file of a specific Docker container. Will return the exact contents in an 'application/ocetet-stream' response.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
			.WithTags("Docker")
			.WithName("GetDockerContainerCompose");

		app.MapPut($"{DlContaintersEndpoints.BaseDockerUrl}/containers/setCompose", async (HttpContext context, string? containerId, bool restartContainer = false) =>
		{
			if (!context.Request.Headers.ContentEncoding.Contains("chunked") && context.Request.ContentLength is null or 0)
			{
				return Results.StatusCode(StatusCodes.Status411LengthRequired);
			}

			// Request validation
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId, true, true);
			if (requestValidation is not null) return requestValidation;

			// Container validation
			DockerLocal.DockerContainer targetContainer;
			try
			{
				targetContainer = DockerLocal.DockerContainers.Containers.First(container => container.Id.StartsWith(containerId));
			}
			catch (InvalidOperationException)
			{
				return Results.NotFound($"Container with ID '{containerId}' was not found.");
			}
			if (targetContainer.ComposePath is null)
				return Results.NotFound("This container does not have a compose file.");
			if (!targetContainer.IsManaged) return Results.BadRequest("This container is not managed by Bayt.");

			// Actual logic
			await using (var fileStream = new FileStream(targetContainer.ComposePath, FileMode.Truncate, FileAccess.Write,
				             FileShare.None))
			{
				await context.Request.Body.CopyToAsync(fileStream);
			}

			if (!restartContainer) return Results.NoContent();


			var dockerRequest = await DockerLocal.SendRequest($"containers/{containerId}/restart", HttpMethod.Post);
			return dockerRequest.Status switch
			{
				HttpStatusCode.NoContent => Results.NoContent(),
				HttpStatusCode.NotFound => Results.NotFound($"[Reboot step] Container with ID '{containerId}' was not found."),
				HttpStatusCode.InternalServerError => Results.InternalServerError(
					$"[Reboot step] Docker returned an error while restarting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
				_ => Results.InternalServerError(
					$"[Reboot step] Docker returned an unknown error while restarting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
			};
		}).Produces(StatusCodes.Status204NoContent)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status411LengthRequired)
			.WithSummary("Replace a Docker container's compose file if it's managed by Bayt.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID. Will default to not restarting the container after replacing its compose. The new compose's contents are expected to be in the body of the request.")
			.WithTags("Docker")
			.WithName("SetDockerContainerCompose");

		app.MapPost($"{DlContaintersEndpoints.BaseDockerUrl}/containers/own", async (string? containerId) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId, true, true);
			if (requestValidation is not null) return requestValidation;

			var defaultSidecarContents = JsonSerializer.Serialize(DockerLocal.DockerContainers.GetDefaultMetadata(), ApiConfig.BaytJsonSerializerOptions);

			DockerLocal.DockerContainer targetContainer;
			try
			{
				targetContainer = DockerLocal.DockerContainers.Containers.First(container => container.Id.StartsWith(containerId));
			}
			catch (InvalidOperationException)
			{
				return Results.NotFound($"Container with ID '{containerId}' was not found.");
			}
			if (targetContainer.ComposePath is null)
				return Results.NotFound($"Container with ID '{containerId}' does not have a compose file.");
			if (targetContainer.IsManaged) return Results.StatusCode(StatusCodes.Status304NotModified);

			var composeDir = Path.GetDirectoryName(targetContainer.ComposePath) ?? "/";
			await File.WriteAllTextAsync(Path.Combine(composeDir, ".BaytMetadata.json"), defaultSidecarContents);

			// Fetch image icons
			string[] imageIcons = await DockerLocal.LoadImageIcons(targetContainer.ImageName);
			await DockerLocal.SetImageIcons(targetContainer.ImageName, imageIcons);

			return Results.NoContent();
		}).Produces(StatusCodes.Status204NoContent)
			.Produces(StatusCodes.Status304NotModified)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status500InternalServerError)
			.WithSummary("Mark a Docker container as managed by Bayt.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
			.WithTags("Docker")
			.WithName("OwnDockerContainer");

		app.MapDelete($"{DlContaintersEndpoints.BaseDockerUrl}/containers/disown", async (string? containerId) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(containerId, true, true);
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
			if (targetContainer.ComposePath is null)
				return Results.NotFound($"Container with ID '{containerId}' does not have a compose file.");
			if (!targetContainer.IsManaged) return Results.StatusCode(StatusCodes.Status304NotModified);

			var composeDir = Path.GetDirectoryName(targetContainer.ComposePath) ?? "/";
			File.Delete(Path.Combine(composeDir, ".BaytMetadata.json"));

			return Results.NoContent();
		}).Produces(StatusCodes.Status204NoContent)
			.Produces(StatusCodes.Status304NotModified)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status500InternalServerError)
			.WithSummary("Mark a Docker container as unmanaged by Bayt.")
			.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
			.WithTags("Docker")
			.WithName("DisownDockerContainer");

		app.MapPost($"{DlContaintersEndpoints.BaseDockerUrl}/containers/create", async (HttpContext context, string? containerName, bool startContainer = true, bool deleteIfFailed = true) =>
		{
			if (!DockerLocal.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
			if (!DockerLocal.IsDockerComposeAvailable) return Results.InternalServerError("Docker-Compose is not available on this system or the Docker integration was disabled.");

			var containerNameSlug = ParsingMethods.ConvertTextToSlug(containerName);
			if (string.IsNullOrWhiteSpace(containerNameSlug)) return Results.BadRequest($"{nameof(containerName)} is required and must contain at least one ASCII character.");
			if (!context.Request.Headers.ContentEncoding.Contains("chunked") && context.Request.ContentLength is null or 0)
			{
				return Results.StatusCode(StatusCodes.Status411LengthRequired);
			}

			var defaultMetadata = DockerLocal.DockerContainers.GetDefaultMetadata(containerName);
			var defaultComposeSidecarContent = JsonSerializer.Serialize(defaultMetadata, ApiConfig.BaytJsonSerializerOptions);

			var containerExists = Directory.EnumerateDirectories(ApiConfig.ApiConfiguration.PathToComposeFolder).Any(directory => Path.GetFileNameWithoutExtension(directory) == containerNameSlug);
			if (containerExists) return Results.Conflict($"A container with the name '{containerNameSlug}' already exists.");

			var composePath = Path.Combine(ApiConfig.ApiConfiguration.PathToComposeFolder, containerNameSlug);
			Directory.CreateDirectory(composePath);

			await File.WriteAllTextAsync(Path.Combine(composePath, ".BaytMetadata.json"), defaultComposeSidecarContent);
			string yamlFilePath = Path.Combine(composePath, "docker-compose.yml");
			await using (var fileStream = new FileStream(yamlFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await context.Request.Body.CopyToAsync(fileStream);
			}

			if (!startContainer) return Results.NoContent();

			var composeShell = await ShellMethods.RunShell("docker-compose", ["-f", yamlFilePath, "up", "-d"]);

			if (!composeShell.IsSuccess && deleteIfFailed)
			{
				// In case the docker-compose file left any services running
				await ShellMethods.RunShell("docker-compose", ["-f", yamlFilePath, "down"]);
				await ShellMethods.RunShell("docker-compose", ["-f", yamlFilePath, "rm"]);

				Directory.Delete(composePath, true);
				return Results.InternalServerError($"Non-zero exit code from starting the container. Container was deleted. " +
				                                   $"Stdout: {composeShell.StandardOutput} " +
				                                   $"Stderr: {composeShell.StandardError}");
			}
			if (!composeShell.IsSuccess)
			{
				return Results.InternalServerError($"Non-zero exit code from starting the container. " +
				                                   $"Stdout: {composeShell.StandardOutput} " +
				                                   $"Stderr: {composeShell.StandardError}");
			}

			return Results.Text(yamlFilePath, "plain/text");
		}).Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status409Conflict)
			.Produces(StatusCodes.Status411LengthRequired)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Create and optionally start a new Docker container from a compose file.")
			.WithDescription("containerName is required and must contain at least one ASCII character. " +
			                 "deleteIfFailed defaults to true and deletes the compose directory in case a non-zero exit code was reported by docker-compose. " +
			                 "startContainer defaults to true The compose file is expected to be in the body of the request.")
			.WithTags("Docker")
			.WithName("CreateDockerContainer");
	}
}