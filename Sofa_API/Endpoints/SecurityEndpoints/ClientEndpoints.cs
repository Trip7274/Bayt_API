using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sofa_API.Security;

namespace Sofa_API.Endpoints.SecurityEndpoints;

public static class ClientEndpoints
{
	private static readonly string BaseClientUrl = ApiConfig.BaseApiUrlPath + "/security/clients";

	public static void MapClientSecurityEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapPost($"{BaseClientUrl}/register", async (HttpContext context, string? clientName,
				string? clientIcon = null, string? brandColor = null, string? clientDescription = null,
				[FromBody] Dictionary<string, string?>? requestedPermissions = null) =>
		{
			var cert = context.Connection.ClientCertificate;
			clientName = ParsingMethods.SanitizeString(clientName);


			if (cert is null) return Results.BadRequest("Must use a client certificate");
			if (string.IsNullOrWhiteSpace(clientName)) return Results.BadRequest("Must set a client name");
			if (clientIcon is not null && !clientIcon.StartsWith("https://")) return Results.BadRequest("Client icon must be a valid HTTPS URL");
			if (brandColor is not null && (brandColor.Length != 7 || !brandColor.StartsWith('#') ))
				return Results.BadRequest("Brand color must be a valid HEX code, starting with a '#'.");

			if (Clients.DoesClientExist(cert.GetCertHashString(HashAlgorithmName.SHA256))) return Results.Conflict(new { message = "Client already exists" });

			// Parse through the requested permissions
			Dictionary<string, List<string>> parsedRequestedPermissions = [];
			if (requestedPermissions is not null)
			{
				foreach (var permString in requestedPermissions.Keys)
				{
					if (Permissions.SofaPermission.TryParse(permString, out var sofaPermission))
					{
						if (parsedRequestedPermissions.TryGetValue(sofaPermission.PermissionString, out var foundPermPowers))
						{
							foundPermPowers.AddRange(sofaPermission.PermPowers.Except(foundPermPowers));
						}
						else
						{
							parsedRequestedPermissions.Add(sofaPermission.PermissionString, sofaPermission.PermPowers);
						}
					}
					else
					{
						return Results.BadRequest(new { message = $"Invalid permission string: {permString}" });
					}
				}
			}


			var clientObject = new Client(clientName, cert.GetCertHashString(HashAlgorithmName.SHA256), parsedRequestedPermissions, true);

			// Registration flow
			// Create a registration key (the registering client must present this, or have a master client present it for it (if MCs exist))
			var registrationKey = new byte[32];
			RandomNumberGenerator.Fill(registrationKey);
			var registrationKeyString = Convert.ToBase64String(registrationKey);

			// We'll store pending requests here before accepting or rejecting them
			Directory.CreateDirectory(Path.Combine(SecurityStores.BaseSecurityPath, "requests"));

			string clientNameSlug = ParsingMethods.ConvertTextToSlug(clientName);
			var registrationEntry = new Dictionary<string, dynamic?>
			{
				["FriendlyName"] = clientName,
				["ClientIcon"] = clientIcon,
				["BrandColor"] = brandColor,
				["ClientDescription"] = clientDescription,

				["SlugName"] = clientNameSlug,
				["ID"] = clientObject.Guid,
				["Thumbprint"] = clientObject.Thumbprint,

				["RequestedPermissions"] = requestedPermissions ?? new Dictionary<string, string?>(),

				["RequestingIp"] = context.Connection.RemoteIpAddress?.ToString(),
				["TimeRequested"] = DateTimeOffset.UtcNow,
				["RegistrationKey"] = registrationKeyString
			};
			string registrationEntryPath = Path.Combine(SecurityStores.BaseSecurityPath, "requests", $"{clientNameSlug}.json");

			await File.WriteAllTextAsync(registrationEntryPath, JsonSerializer.Serialize(registrationEntry, ApiConfig.SofaJsonSerializerOptions));
			Clients.AddPendingClient(registrationKeyString, clientObject);
			Clients.AddClient(clientObject);

			Logs.LogBook.Write(new (StreamId.Notice, "Client Registration", $"Client '{clientName}' pending registration. To approve, present it with the registration key from '{registrationEntryPath}'"));

			return Results.Accepted($"{BaseClientUrl}/me/confirm", new { message = "Client registration pending. Please provide the registration key at the confirmation endpoint.", ttl = Clients.PendingTtlSeconds, pendingId = clientObject.Guid });
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.WithSummary("Registers a client with this Sofa instance.")
		.WithDescription("The client must be authenticated with a client certificate. " +
		                 "The certificate should be self-signed. " +
		                 "clientName is required and is the official name of the client." +
		                 "The body is expected to have a Dictionary<string,string?> of requestedPermissions")
		.WithTags("Auth", "Client")
		.WithName("RegisterClient");

		app.MapPost($"{BaseClientUrl}/{{targetClientId}}/confirm", (HttpContext context, string? targetClientId, [FromBody] Dictionary<string, string>? requestDetails) =>
		{
			if (requestDetails is null || !requestDetails.TryGetValue("registrationKey", out var registrationKey))
				return Results.BadRequest(new { message = "Invalid registration key." });

			var connectedClient = SecurityMethods.GetConnectedClient(context, true);
			var targetClient = connectedClient;
			if (Guid.TryParse(targetClientId, out var targetUserGuid) && connectedClient is not null)
			{
				Clients.TryFetchValidClient(targetUserGuid, out targetClient);
				if (targetClient != connectedClient && !connectedClient.CanRegister)
				{
					return Results.StatusCode(StatusCodes.Status403Forbidden);
				}
			}
			else if (targetClientId != "me")
			{
				return Results.BadRequest(new { message = "Invalid target client ID or a suitable client is not connected." });
			}
			if (targetClient is null) return Results.NotFound(new { message = "Invalid target client ID or a suitable client is not connected." });
			if (!targetClient.IsPaused) return Results.BadRequest(new { message = "Client is already confirmed" });

			if (Clients.PendingClients.TryGetValue(registrationKey, out var pendingClient))
			{
				Clients.RemovePendingClient(registrationKey, ParsingMethods.ConvertTextToSlug(targetClient.ClientName));
				pendingClient.Unpause();
				return Results.NoContent();
			}

			return Results.NotFound(new { message = "Invalid registration key." });
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status404NotFound)
		.WithSummary("Confirms a client's registration.")
		.WithDescription("targetClientId must be a valid GUID, or the string 'me' to confirm the currently connected client's registration.")
		.WithTags("Auth", "Client")
		.WithName("ConfirmClientRegistration")
		.RequireAuthorization("ClientAllowPaused");

		app.MapDelete($"{BaseClientUrl}/{{targetClientId}}", (HttpContext context, string? targetClientId) =>
		{
			var connectedClient = SecurityMethods.GetConnectedClient(context)!;
			var targetClient = connectedClient;
			if (Guid.TryParse(targetClientId, out var guid))
			{
				Clients.TryFetchValidClient(guid, out targetClient);
				if (targetClient != connectedClient && !connectedClient.HasPermission(new("clients", ["delete"])))
				{
					return Results.StatusCode(StatusCodes.Status403Forbidden);
				}
			}
			else if (targetClientId != "me")
			{
				return Results.BadRequest(new { message = "Invalid target client ID" });
			}
			if (targetClient is null) return Results.NotFound();

			if (targetClient.Delete())
			{
				return Results.NoContent();
			}
			return Results.InternalServerError(new { message = "Failed to delete client for some reason." });
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status404NotFound)
		.WithSummary("Deletes a client.")
		.WithDescription("targetClientId must be a valid GUID, or the string 'me' to delete the currently connected client.")
		.WithTags("Auth", "Client")
		.WithName("DeleteClient")
		.RequireAuthorization("Client");

		app.MapPost($"{BaseClientUrl}/refresh", async (HttpContext context) =>
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

		app.MapPost($"{BaseClientUrl}/{{targetClientId}}/edit", (HttpContext context, [FromBody] Dictionary<string, string?> changes, string targetClientId) =>
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

		app.MapGet($"{BaseClientUrl}/{{targetClientId}}/info", (HttpContext context, string targetClientId) =>
		{
			var connectedClient = SecurityMethods.GetConnectedClient(context)!;
			var targetClient = connectedClient;
			if (Guid.TryParse(targetClientId, out var targetUserGuid))
			{
				Clients.TryFetchValidClient(targetUserGuid, out targetClient);
				if (targetClient != connectedClient && !connectedClient.HasPermission(new("clients", ["view-info"])))
				{
					return Results.StatusCode(StatusCodes.Status403Forbidden);
				}
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
		.RequireAuthorization("Client");

		app.MapPatch($"{BaseClientUrl}/{{targetClientId}}/permissions", (HttpContext context, string targetClientId, [FromBody] Dictionary<string, List<string>> permissionsToAdd) =>
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
				var sofaPermission = new Permissions.SofaPermission(permission, scopes);
				if (!connectedClient.HasPermission(sofaPermission)) continue;

				targetClient.AddPermission(sofaPermission);
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

		app.MapDelete($"{BaseClientUrl}/{{targetClientId}}/permissions", (HttpContext context, string targetClientId, [FromBody] Dictionary<string, List<string>> permissionsToRemove) =>
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
				var sofaPermission = new Permissions.SofaPermission(permission, scopes);
				if (!connectedClient.HasPermission(sofaPermission) || !targetClient.PermissionList.ContainsKey(permission)) continue;

				targetClient.RemovePermission(sofaPermission);
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

		app.MapPut($"{BaseClientUrl}/{{targetClientId}}/permissions", (HttpContext context, string targetClientId, [FromBody] Dictionary<string, List<string>> permissionsToSet) =>
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
				var sofaPermission = new Permissions.SofaPermission(permission, scopes);

				if (!connectedClient.HasPermission(sofaPermission))
					return Results.BadRequest(new { message = "All permissions must be in the list of connected client's permissions.", missingPermission = sofaPermission.ToString() });
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

		app.MapGet($"{BaseClientUrl}/list", () =>
		{
			return Results.Json(Clients.FetchAllClients());
		}).Produces(StatusCodes.Status200OK)
		.WithSummary("List all registered clients.")
		.WithTags("Auth", "Client")
		.WithName("GetClientList")
		.RequireAuthorization("Client", "clients:list");
	}
}