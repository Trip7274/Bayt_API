using Microsoft.AspNetCore.Mvc;

namespace Bayt_API.Endpoints;

public static class MountsEndpoints
{
	public static IEndpointRouteBuilder MapMountsEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet($"{ApiConfig.BaseApiUrlPath}/mounts/getList", () =>
				Results.Json(ApiConfig.ApiConfiguration.WatchedMounts))
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Fetch the list of the currently watched mounts.")
			.WithTags("Mounts")
			.WithName("GetMountsList");


		app.MapPost($"{ApiConfig.BaseApiUrlPath}/mounts/add", ([FromBody] Dictionary<string, string?> mountPoints) =>
		{
			foreach (var mountPoint in mountPoints.Where(mountPoint => !Directory.Exists(mountPoint.Key)))
			{
				mountPoints.Remove(mountPoint.Key);
			}

			if (mountPoints.Count == 0)
			{
				return Results.BadRequest("Mountpoints list must contain at least 1 valid element.");
			}

			bool wereChangesMade = ApiConfig.ApiConfiguration.AddMountpoint(mountPoints);

			return !wereChangesMade ? Results.StatusCode(StatusCodes.Status304NotModified) : Results.NoContent();
		}).Produces(StatusCodes.Status204NoContent)
			.Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status304NotModified)
			.WithSummary("Add one or more mounts to the list of watched mounts.")
			.WithDescription("Format: { '${MountPoint}': '${MountLabel}' }. Expected to be in the body of the request.")
			.WithTags("Mounts")
			.WithName("AddMounts");


		app.MapDelete($"{ApiConfig.BaseApiUrlPath}/mounts/remove", ([FromBody] Dictionary<string, List<string>> mountPointsFull) =>
		{
		List<string> mountPoints;
		try
		{
			mountPoints = mountPointsFull.First(mountPoint => mountPoint.Key == "Mounts").Value;
		}
		catch (InvalidOperationException)
		{
			return Results.BadRequest("List must contain a 'Mounts' key.");
		}

		if (mountPoints.Count == 0)
		{
			return Results.BadRequest("List must contain more than 0 elements.");
		}

		ApiConfig.ApiConfiguration.RemoveMountpoints(mountPoints);

		return Results.NoContent();
		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Remove one or more mounts from the list of watched mounts.")
			.WithDescription("Format: { 'Mounts': ['${Mountpoint1}', '${Mountpoint2}', '...'] }. Expected to be in the body of the request.")
			.WithTags("Mounts")
			.WithName("RemoveMounts");


		return app;
	}
}