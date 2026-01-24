using System.Net;

namespace Bayt_API.Endpoints;

public static class WolEndpoints
{
	public static IEndpointRouteBuilder MapWolEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapPost($"{ApiConfig.BaseApiUrlPath}/WoL/addClient", (string clientAddress, string clientLabel) =>
		{
			if (string.IsNullOrWhiteSpace(clientLabel) || !IPAddress.TryParse(clientAddress, out _))
				return Results.BadRequest("Invalid IP address or missing label.");

			return ApiConfig.ApiConfiguration.AddWolClient(clientAddress, clientLabel) ? Results.NoContent()
				: Results.InternalServerError("Failed to add the client to the list of clients. Either the script getNet.sh timed out, or returned malformed data.");
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status500InternalServerError)
		.Produces(StatusCodes.Status400BadRequest)
		.WithSummary("Save a WoL client to this Bayt instance. Both fields are required, and cannot be empty.")
		.WithTags("Wake-on-LAN")
		.WithName("AddWolClient");

		app.MapDelete($"{ApiConfig.BaseApiUrlPath}/WoL/removeClient", (string clientAddress) =>
		{
			if (string.IsNullOrWhiteSpace(clientAddress) || !IPAddress.TryParse(clientAddress, out var clientAddressParsed))
				return Results.BadRequest("Invalid or missing IP address.");

			return ApiConfig.ApiConfiguration.RemoveWolClient(clientAddressParsed) switch
			{
				true => Results.NoContent(),
				false => Results.NotFound("Client was not found in the API's list of clients."),
				null => Results.InternalServerError(
					"Failed to remove the client from the list of clients. Either the script getNet.sh timed out, or returned malformed data.")
			};
		}).Produces(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status404NotFound)
			.Produces(StatusCodes.Status500InternalServerError)
			.Produces(StatusCodes.Status204NoContent)
			.WithSummary("Remove a saved WoL clients from this Bayt instance.")
			.WithTags("Wake-on-LAN")
			.WithName("RemoveWolClient");

		app.MapGet($"{ApiConfig.BaseApiUrlPath}/WoL/getClients", () =>
				Results.Json(ApiConfig.ApiConfiguration.WolClients))
			.Produces(StatusCodes.Status200OK)
			.WithSummary("Fetch the list of the currently saved WoL clients.")
			.WithTags("Wake-on-LAN")
			.WithName("GetWolClients");

		app.MapPost($"{ApiConfig.BaseApiUrlPath}/WoL/wake", (string? ipAddress) =>
		{
			if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out _))
				return Results.BadRequest("ipAddress must be a valid IPv4 address.");

			var clientToWake =
				ApiConfig.ApiConfiguration.WolClientsClass?.Find(client =>
					client.IpAddress.ToString() == ipAddress);
			if (clientToWake is null)
			{
				return Results.BadRequest($"No WoL client with IP '{ipAddress}' was found.");
			}

			WolHandling.WakeClient(clientToWake);

			return Results.NoContent();
		}).Produces(StatusCodes.Status204NoContent)
			.Produces(StatusCodes.Status400BadRequest)
			.WithSummary("Send a wake signal to a specific WoL client.")
			.WithDescription("ipAddress is required. It must be a valid, saved, IPv4 address of the target client.")
			.WithTags("Wake-on-LAN")
			.WithName("WakeWolClient");

		return app;
	}
}