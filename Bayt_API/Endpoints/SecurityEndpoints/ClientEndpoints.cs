using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Bayt_API.Security;
using Microsoft.AspNetCore.Mvc;

namespace Bayt_API.Endpoints.SecurityEndpoints;

public static class ClientEndpoints
{
	private static readonly string BaseClientUrl = ApiConfig.BaseApiUrlPath + "/security" + "/client";

	public static void MapClientSecurityEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapPost($"{BaseClientUrl}/clients/register", (HttpContext context, string? clientName, string? personalName = null) =>
		{
			var cert = context.Connection.ClientCertificate;

			if (cert is null) return Results.BadRequest("Must use a client certificate");

			clientName = ParsingMethods.SanitizeString(clientName);
			personalName = ParsingMethods.SanitizeString(personalName);
			if (string.IsNullOrWhiteSpace(clientName)) return Results.BadRequest("Must set a client name");
			if (personalName is not null && string.IsNullOrWhiteSpace(personalName)) return Results.BadRequest("Personal name cannot be empty");

			if (Clients.DoesClientExist(cert.GetCertHashString(HashAlgorithmName.SHA256))) return Results.Conflict(new { message = "Client already exists" });

			Logs.LogBook.Write(new (StreamId.Notice, "Client Registration", $"Client '{clientName}' registered!"));

			// If this is the first client, give them admin permissions (for now, this will be changed in the close future)
			if (Clients.Count == 0)
			{
				var adminPerms = new Dictionary<string, List<string>>
				{
					["admin"] = [ "admin" ]
				};

				Clients.AddClient(new (clientName, cert.GetCertHashString(HashAlgorithmName.SHA256), personalName, adminPerms));
			}
			else
			{
				Clients.AddClient(new (clientName, cert.GetCertHashString(HashAlgorithmName.SHA256), personalName));
			}

			return Results.NoContent();
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.WithSummary("Registers a client with this Bayt instance.")
		.WithDescription("The client must be authenticated with a client certificate. " +
		                 "The certificate should be self-signed. " +
		                 "clientName is required and is the official name of the client. " +
		                 "personalName is optional and may be set by the user at their discretion.")
		.WithTags("Auth", "Client")
		.WithName("RegisterClient");

		app.MapPost($"{BaseClientUrl}/clients/refresh", async (HttpContext context) =>
		{
			if (context.Request.ContentLength == 0) return Results.BadRequest(new { message = "Must provide a certificate to refresh." });

			var certFetchTask = Task.Run(async () =>
			{
				List<byte> newCertList = [];
				byte[] buffer = new byte[1024];
				while (true)
				{
					var bytesRead = await context.Request.Body.ReadAsync(buffer);
					if (bytesRead == 0) break;
					newCertList.AddRange(buffer[..bytesRead]);
				}

				return newCertList.ToArray();
			});

			var currentClient = SecurityMethods.GetConnectedClient(context);
			if (currentClient is null) return Results.BadRequest( new { message = "Must be connected to a client to refresh." });

			var newCertBytes = await certFetchTask;
			if (newCertBytes.Length == 0) return Results.BadRequest(new { message = "Must provide a certificate to refresh." });


			var newCert = X509CertificateLoader.LoadCertificate(newCertBytes);

			return !currentClient.RefreshCertificate(newCert, out var message) ? Results.BadRequest(new { message }) : Results.Ok();
		}).Produces(StatusCodes.Status200OK)
		.Produces(StatusCodes.Status400BadRequest)
		.WithSummary("Refreshes the certificate of a client.")
		.WithDescription("Must send the raw bytes of the new certificate in the request body.")
		.WithTags("Auth", "Client")
		.WithName("RefreshClientCertificate")
		.RequireAuthorization("Client");

		app.MapPost($"{BaseClientUrl}/clients/{{targetClientId}}/edit", (HttpContext context, [FromBody] Dictionary<string, string?> changes, string targetClientId) =>
		{
			if (changes.Count == 0) return Results.BadRequest("No changes were provided.");

			var targetClient = SecurityMethods.GetConnectedClient(context);
			if (Guid.TryParse(targetClientId, out var targetUserGuid))
			{
				if (!SecurityMethods.ChallengePermission(context, new("clients", ["edit-details"])))
				{
					return Results.StatusCode(StatusCodes.Status403Forbidden);
				}
				Clients.TryFetchValidClient(targetUserGuid, out targetClient);
			}
			else if (targetClientId != "me")
			{
				return Results.BadRequest("Invalid target client ID");
			}
			if (targetClient is null) return Results.NotFound();

			if (changes.ContainsKey("Permissions") || changes.ContainsKey("PermissionList"))
			{
				return Results.BadRequest(new { message = "Changing permissions is not allowed through this endpoint." });
			}
			if (changes.ContainsKey("Guid"))
			{
				return Results.BadRequest(new { message = "Changing a client's GUID is not possible." });
			}

			return targetClient.Edit(changes) ? Results.NoContent() : Results.StatusCode(StatusCodes.Status304NotModified);
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status304NotModified)
		.WithSummary("Edit a client's details.")
		.WithDescription("targetClientId must be a valid GUID, or the string 'me' to edit the currently connected client.")
		.WithTags("Auth", "Client")
		.WithName("EditClient")
		.RequireAuthorization("MultiAuth");

		app.MapGet($"{BaseClientUrl}/clients/{{targetClientId}}/info", (HttpContext context, string targetClientId) =>
		{
			var targetClient = SecurityMethods.GetConnectedClient(context);
			if (Guid.TryParse(targetClientId, out var targetUserGuid))
			{
				if (!SecurityMethods.ChallengePermission(context, new("clients", ["view-info"])))
				{
					return Results.StatusCode(StatusCodes.Status403Forbidden);
				}
				Clients.TryFetchValidClient(targetUserGuid, out targetClient);
			}
			else if (targetClientId != "me")
			{
				return Results.BadRequest("Invalid target client ID");
			}

			return targetClient is not null
				? Results.Json(targetClient.ToDictionary())
				: Results.NotFound();
		}).Produces(StatusCodes.Status200OK)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status404NotFound)
		.WithSummary("Fetch information about a specific client.")
		.WithDescription("targetClientId must be a valid GUID, or the string 'me' to fetch information about the currently connected client.")
		.WithTags("Auth", "Client")
		.WithName("GetClientInfo")
		.RequireAuthorization("MultiAuth");

		app.MapPatch($"{BaseClientUrl}/clients/{{targetClientId}}/permissions", (HttpContext context, string targetClientId, [FromBody] Dictionary<string, List<string>> permissionsToAdd) =>
		{
			var connectedClient = SecurityMethods.GetConnectedClient(context)!;
			var targetClient = connectedClient;
			if (Guid.TryParse(targetClientId, out var targetClientGuid))
			{
				Clients.TryFetchValidClient(targetClientGuid, out targetClient);
			}
			if (targetClient is null) return Results.NotFound();
			if (targetClient == connectedClient)
			{
				if (!SecurityMethods.ChallengePermission(context, new("admin", ["admin"])))
					return Results.StatusCode(StatusCodes.Status403Forbidden);
			}

			bool changesMade = false;
			foreach (var (permission, scopes) in permissionsToAdd)
			{
				var baytPermission = new Permissions.BaytPermission(permission, scopes);
				if (!connectedClient.HasPermission(baytPermission)) continue;

				targetClient.AddPermission(baytPermission);
				changesMade = true;
			}

			if (changesMade) SecurityStores.SaveClient(targetClient);
			return changesMade ? Results.NoContent() : Results.StatusCode(StatusCodes.Status304NotModified);
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status304NotModified)
		.WithSummary("Add one or more permissions to a client.")
		.WithDescription("targetClientId must be a valid GUID, or the string 'me' to add permissions to the currently connected client.")
		.WithTags("Auth", "Client")
		.WithName("AddClientPermissions")
		.RequireAuthorization("MultiAuth", "clients:edit-permissions");

		app.MapDelete($"{BaseClientUrl}/clients/{{targetClientId}}/permissions", (HttpContext context, string targetClientId, [FromBody] Dictionary<string, List<string>> permissionsToRemove) =>
		{
			var connectedClient = SecurityMethods.GetConnectedClient(context)!;
			var targetClient = connectedClient;
			if (Guid.TryParse(targetClientId, out var targetClientGuid))
			{
				Clients.TryFetchValidClient(targetClientGuid, out targetClient);
			}
			if (targetClient is null) return Results.NotFound();
			if (targetClient == connectedClient)
			{
				if (!SecurityMethods.ChallengePermission(context, new("admin", ["admin"])))
					return Results.StatusCode(StatusCodes.Status403Forbidden);
			}

			bool changesMade = false;
			foreach (var (permission, scopes) in permissionsToRemove)
			{
				var baytPermission = new Permissions.BaytPermission(permission, scopes);
				if (!connectedClient.HasPermission(baytPermission) || !targetClient.PermissionList.ContainsKey(permission)) continue;

				targetClient.RemovePermission(baytPermission);
				changesMade = true;
			}

			if (changesMade) SecurityStores.SaveClient(targetClient);
			return changesMade ? Results.NoContent() : Results.StatusCode(StatusCodes.Status304NotModified);
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status304NotModified)
		.WithSummary("Remove one or more permissions from a client.")
		.WithDescription("targetClientId must be a valid GUID, or the string 'me' to remove permissions from the currently connected client.")
		.WithTags("Auth", "Client")
		.WithName("RemoveClientPermissions")
		.RequireAuthorization("MultiAuth", "clients:edit-permissions");

		app.MapPut($"{BaseClientUrl}/clients/{{targetClientId}}/permissions", (HttpContext context, string targetClientId, [FromBody] Dictionary<string, List<string>> permissionsToSet) =>
		{
			var connectedClient = SecurityMethods.GetConnectedClient(context)!;
			var targetClient = connectedClient;

			if (Guid.TryParse(targetClientId, out var targetClientGuid))
			{
				Clients.TryFetchValidClient(targetClientGuid, out targetClient);
			}
			else if (targetClientId != "me")
			{
				return Results.BadRequest("Invalid target client ID");
			}

			if (targetClient is null) return Results.NotFound();
			if (targetClient == connectedClient)
			{
				if (!SecurityMethods.ChallengePermission(context, new("admin", ["admin"])))
					return Results.StatusCode(StatusCodes.Status403Forbidden);
			}

			foreach (var (permission, scopes) in permissionsToSet)
			{
				var baytPermission = new Permissions.BaytPermission(permission, scopes);

				if (!connectedClient.HasPermission(baytPermission))
					return Results.BadRequest(new { message = "All permissions must be in the list of connected client's permissions.", missingPermission = baytPermission.ToString() });
			}

			targetClient.SetPermissions(permissionsToSet);
			SecurityStores.SaveClient(targetClient);
			return Results.NoContent();
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status304NotModified)
		.WithSummary("Replace a client's permissions with the given list.")
		.WithDescription("targetClientId must be a valid GUID, or the string 'me' to set the currently connected client's permissions. " +
		                 "Body must include the new list of permissions in a { \"<PermissionString>\": [\"<scope>\"] } format.\n" +
		                 "This will return a 400 Bad Request if any of the permissions are not in the list of connected client's permissions.")
		.WithTags("Auth", "Client")
		.WithName("SetClientPermissions")
		.RequireAuthorization("MultiAuth", "clients:edit-permissions");

		app.MapGet($"{BaseClientUrl}/clients/list", () =>
		{
			return Results.Json(Clients.FetchAllClients());
		}).Produces(StatusCodes.Status200OK)
		.WithSummary("List all registered clients.")
		.WithTags("Auth", "Client")
		.WithName("GetClientList")
		.RequireAuthorization("Client", "clients:list");
	}
}