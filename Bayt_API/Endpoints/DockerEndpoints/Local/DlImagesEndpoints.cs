using System.Text;
using System.Text.Json;
using System.Web;

namespace Bayt_API.Endpoints.DockerEndpoints.Local;

public static class DlImagesEndpoints
{
	public static void MapDlImagesEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet($"{DlContaintersEndpoints.BaseDockerUrl}/images/getList", async () =>
		{
			if (!DockerLocal.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
			await DockerLocal.ImagesInfo.UpdateDataIfNecessary();

			return Results.Json(DockerLocal.ImagesInfo.ToDictionary());
		}).Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Get list of Docker images on the system.")
			.WithTags("Docker", "Docker Images")
			.WithName("GetDockerImages");

		app.MapPost($"{DlContaintersEndpoints.BaseDockerUrl}/images/pull", DockerLocal.PullImage)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status409Conflict)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Pull a Docker image.")
			.WithDescription("imageName must be the full repository + image name (e.g., \"jellyfin/jellyfin\"). tagOrDigest will default to \"latest\".")
			.WithTags("Docker", "Docker Images")
			.WithName("PullDockerImage");

		app.MapDelete($"{DlContaintersEndpoints.BaseDockerUrl}/images/delete", async (string? imageId, bool? force = false) =>
		{
			var requestValidation = await RequestChecking.ValidateDockerRequest(imageId, false, updateImages:true);
			if (requestValidation is not null) return requestValidation;
			if (!imageId.StartsWith("sha256:")) imageId = "sha256:" + imageId;

			var targetImage = DockerLocal.ImagesInfo.Images.Find(image => image.Id.StartsWith(imageId));

			return targetImage is null ? Results.NotFound($"Image with ID '{imageId}' was not found.")
				: await targetImage.Delete(force.GetValueOrDefault(false));
		}).Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status409Conflict)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Delete a Docker image.")
			.WithDescription("imageId must contain at least the first 12 characters of the image's ID. force removes the image even if it is being used by stopped containers or has other tags, defaults to false.")
			.WithTags("Docker", "Docker Images")
			.WithName("DeleteDockerImage");

		app.MapGet($"{DlContaintersEndpoints.BaseDockerUrl}/images/search", async (string term, byte limit = 15) =>
		{
			if (!DockerLocal.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
			if (string.IsNullOrWhiteSpace(term)) return Results.BadRequest("Please include a term to search.");
			limit = byte.Clamp(limit, 1, 50);

			var dockerResponse = await DockerLocal.SendRequest($"images/search?term={HttpUtility.UrlEncode(term)}&limit={limit}");

			if (!dockerResponse.IsSuccess) return Results.Text(dockerResponse.Body, dockerResponse.ContentType, Encoding.UTF8, (int) dockerResponse.Status);

			var dockerResponseDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>[]>(dockerResponse.Body) ?? [];
			var resultDict = new Dictionary<string, Dictionary<string, dynamic>>();

			foreach (var dockerImage in dockerResponseDict)
			{
				var imageDict = new Dictionary<string, dynamic>
				{
					{ "starCount", dockerImage["star_count"].GetInt32() },
					{ "isOfficial", dockerImage["is_official"].GetBoolean() },
					{ "description", dockerImage["description"].GetString()! }
				};
				resultDict.Add(dockerImage["name"].GetString()!, imageDict);
			}
			resultDict = resultDict.OrderByDescending(pair => pair.Value["starCount"]).ToDictionary(pair => pair.Key, pair => pair.Value);

			return Results.Json(resultDict);
		}).Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Search for a Docker image from DockerHub. Sorted by descending star count")
			.WithDescription("term must be provided and not be whitespace.")
			.WithTags("Docker", "Docker Images")
			.WithName("SearchDockerHub");

		app.MapGet($"{DlContaintersEndpoints.BaseDockerUrl}/images/check", async (string imageName, bool filterIncompatible = true) =>
		{
			if (!DockerLocal.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
			if (string.IsNullOrWhiteSpace(imageName) || imageName.Length > 256) return Results.BadRequest("Please include a valid image name to search.");
			if (imageName.Contains(':')) imageName = imageName.Split(':')[0];
			if (!imageName.Contains('/')) imageName = "library/" + imageName;
			if (imageName.Count(c => c == '/') > 1) return Results.BadRequest("Image name seems malformed. Example: 'jellyfin/jellyfin' or 'hello-world' (for official images). Only DockerHub images are supported");

			var imageNamespace = imageName.Split('/')[0];
			var imageRepository = imageName.Split('/')[1];

			HttpResponseMessage response;
			using (var client = new HttpClient())
			{
				response = await client.GetAsync($"https://hub.docker.com/v2/namespaces/{imageNamespace}/repositories/{imageRepository}/tags?page_size=10");
			}
			if (!response.IsSuccessStatusCode) return Results.Text(await response.Content.ReadAsStringAsync(), "text/plain", Encoding.UTF8, (int) response.StatusCode);

			// This is a bit messy, but we're just trying to make a buncha DockerHub.TagDetails objects from
			// all the fields in the "results" property in the JSON

			var json = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>()
			           ?? throw new NullReferenceException("Response from DockerHub was null.");

			var tagElements = json["results"].EnumerateArray().ToList();
			List<DockerHub.TagDetails> tags = [];
			tags.AddRange(tagElements.Select(tagElement =>
				new DockerHub.TagDetails(
					tagElement.GetProperty("name").GetString()!,
					tagElement.GetProperty("images"))));

			Dictionary<string, dynamic?> resultDict = [];
			foreach (var tag in tags)
			{
				if (filterIncompatible && !tag.ContainsCompatibleImage()) continue;

				resultDict.Add(tag.Name, tag.ToDictionary(filterIncompatible));
			}

			return Results.Json(resultDict);
		}).Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status429TooManyRequests)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Check a Docker image from DockerHub for various details.")
			.WithDescription("imageName must be provided, and must follow this format: 'jellyfin/jellyfin', or 'python' (for official images). " +
			                 "filterIncompatible defaults to true, and hides images/tags that don't contain any natively-compatible images for this system")
			.WithTags("Docker", "Docker Images", "DockerHub")
			.WithName("CheckDockerImage");
	}
}