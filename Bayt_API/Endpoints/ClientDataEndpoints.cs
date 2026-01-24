using System.Text;

namespace Bayt_API.Endpoints;

public static class ClientDataEndpoints
{
	public static void MapClientDataEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet($"{ApiConfig.BaseApiUrlPath}/clientData/{{folderName}}/{{fileName}}", async (string? folderName, string? fileName) =>
		{
			DataEndpointManagement.DataFileMetadata fileRecord;
			try
			{
				fileRecord = new DataEndpointManagement.DataFileMetadata(folderName, fileName);
			}
			catch(ArgumentException e)
			{
				return Results.BadRequest(e.Message);
			}
			catch (Exception e) when(e is FileNotFoundException or DirectoryNotFoundException)
			{
				return Results.NotFound(e.Message);
			}

			if (!fileRecord.FileName.EndsWith(".json"))
			{
				return Results.File(fileRecord.ReadStream!, "application/ocetet-stream",
					fileRecord.FileName, fileRecord.LastWriteTime);
			}

			await fileRecord.ReadStream!.DisposeAsync();
			return Results.Text(Encoding.UTF8.GetString(fileRecord.FileData ?? []), "application/json", Encoding.UTF8, StatusCodes.Status200OK);

		}).Produces(StatusCodes.Status200OK)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status404NotFound)
			.WithSummary("Fetch a specific file from a specific folder in the base clientData folder.")
			.WithDescription("Both parameters are required and must be valid, non-empty file/folder names. If the file ends with .json, it will be returned as an application/json response. Otherwise, it will be returned as a application/octet-stream object.")
			.WithTags("clientData")
			.WithName("GetClientData");

		app.MapPut($"{ApiConfig.BaseApiUrlPath}/clientData/{{folderName}}/{{fileName}}", async (HttpContext context, string? folderName, string? fileName) =>
		{
			DataEndpointManagement.DataFileMetadata fileRecord;

			try
			{
				fileRecord = new(folderName, fileName, createMissing: true);
			}
			catch (ArgumentException e)
			{
				return Results.BadRequest(e.Message);
			}

			var memoryStream = new MemoryStream();
			await context.Request.Body.CopyToAsync(memoryStream);

			fileRecord.FileData = memoryStream.ToArray();

			return Results.NoContent();
		}).Produces(StatusCodes.Status204NoContent)
			.Produces(StatusCodes.Status400BadRequest)
			.WithSummary("Replace/Set a specific file under a specific folder in the base clientData folder. Will create the folder if it doesn't exist.")
			.WithDescription("Both parameters are required and must be valid, non-empty file/folder names. Expects the file's content in the body of the request.")
			.WithTags("clientData")
			.WithName("SetClientData");

		app.MapDelete($"{ApiConfig.BaseApiUrlPath}/clientData/{{folderName}}/{{fileName}}", (string? folderName, string? fileName) =>
		{
			try
			{
				new DataEndpointManagement.DataFileMetadata(folderName, fileName).DeleteFile();
			}
			catch (ArgumentException e)
			{
				return Results.BadRequest(e.Message);
			}
			catch (UnauthorizedAccessException)
			{
				return Results.Text("The file seems to either be read-only, or the current user doesn't have the appropriate permissions.",
					"text/plain", statusCode:StatusCodes.Status403Forbidden);
			}
			catch (IOException e) when (e is not FileNotFoundException or DirectoryNotFoundException)
			{
				return Results.Conflict("The file seems to be in use. It was not deleted.");
			}
			catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
			{
				return Results.NotFound(e.Message);
			}

			return Results.NoContent();
		}).Produces(StatusCodes.Status204NoContent)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status403Forbidden)
			.Produces(StatusCodes.Status409Conflict)
			.WithSummary("Delete the specified file under the clientData folder.")
			.WithDescription("Both parameters are required and must be valid, non-empty file/folder names.")
			.WithTags("clientData")
			.WithName("DeleteClientData");

		app.MapDelete($"{ApiConfig.BaseApiUrlPath}/clientData/{{folderName}}", (string? folderName) =>
		{
			try
			{
				DataEndpointManagement.DeleteDataFolder(folderName);
			}
			catch (ArgumentException)
			{
				return Results.BadRequest("Folder name must not be empty or invalid.");
			}
			catch (DirectoryNotFoundException)
			{
				return Results.NotFound($"Folder '{folderName}' was not found.");
			}
			catch (UnauthorizedAccessException)
			{
				return Results.StatusCode(StatusCodes.Status403Forbidden);
			}
			catch (IOException)
			{
				return Results.Conflict("The folder may be in use, read-only, or one of its contents is read-only. It was not deleted.");
			}

			return Results.NoContent();
		}).Produces(StatusCodes.Status204NoContent)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status403Forbidden)
			.Produces(StatusCodes.Status409Conflict)
			.WithSummary("Delete a specific data folder. Will recursively delete everything inside it.")
			.WithDescription("folderName must be a valid, non-empty folder name.")
			.WithTags("clientData")
			.WithName("DeleteClientDataFolder");
	}
}