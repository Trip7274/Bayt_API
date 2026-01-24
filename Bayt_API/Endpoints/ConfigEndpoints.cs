using Microsoft.AspNetCore.Mvc;

namespace Bayt_API.Endpoints;

public static class ConfigEndpoints
{
	public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapPost($"{ApiConfig.BaseApiUrlPath}/config/edit", ([FromBody] Dictionary<string, dynamic> newConfigs) =>
		{
			// TODO: Implement Auth and Rate Limiting before blindly trusting the request.

			return ApiConfig.ApiConfiguration.EditConfig(newConfigs) ? Results.NoContent()
				: Results.StatusCode(StatusCodes.Status304NotModified);
		}).Produces(StatusCodes.Status204NoContent)
			.Produces(StatusCodes.Status400BadRequest)
			.WithSummary("Change one or a few configs of the API. Follows the names and types of the ApiConfiguration.json file")
			.WithDescription("The property cannot be 'WatchedMounts', nor 'WolClients'. Those two have their own endpoints. " +
			                 "Format: { '${PropertyName}': '${PropertyValue}' }. Expected to be in the body of the request.")
			.WithTags("Configuration")
			.WithName("EditConfig");


		app.MapGet($"{ApiConfig.BaseApiUrlPath}/config/getList", () =>
				Results.Json(ApiConfig.ApiConfiguration.ToDictionary()))
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Fetch the API's live configs in the form of JSON.")
			.WithTags("Configuration")
			.WithName("GetActiveApiConfigs");


		app.MapPost($"{ApiConfig.BaseApiUrlPath}/config/update", () =>
		{
			ApiConfig.ApiConfiguration.LoadConfig();

			return Results.NoContent();
		}).Produces(StatusCodes.Status200OK)
			.WithSummary("Refresh and sync the API's live configs with the file on-disk.")
			.WithTags("Configuration")
			.WithName("UpdateLiveConfigs");

		return app;
	}
}