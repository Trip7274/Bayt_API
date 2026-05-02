using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Sofa_API.Security;

namespace Sofa_API.Endpoints.SecurityEndpoints;

public static class UserEndpoints
{
	private static readonly string BaseUsersUrl = ApiConfig.BaseApiUrlPath + "/security/users";

	public static void MapUserSecurityEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapPost($"{BaseUsersUrl}/register", (string username, string password, string? profilePictureUrl = null) =>
		{
			if (Users.DoesUserExist(username)) return Results.Conflict(new { message = "User already exists" });

			// If this is the first user, give them admin permissions
			if (Users.Count == 0)
			{
				var adminPerms = new Dictionary<string, List<string>>
				{
					["admin"] = ["admin"]
				};
				Users.AddUser(new (username, password, profilePictureUrl, adminPerms));
			}
			else
			{
				Users.AddUser(new (username, password, profilePictureUrl));
			}
			Logs.LogBook.Write(new (StreamId.Notice, "User Registration", $"User '{username}' registered!"));
			return Results.NoContent();
		}).Produces(StatusCodes.Status200OK)
		.Produces(StatusCodes.Status409Conflict)
		.WithSummary("Registers a new user with no permissions.")
		.WithTags("Auth", "User")
		.WithName("RegisterUser")
		.RequireAuthorization("Client", "users:register");

		app.MapPost($"{BaseUsersUrl}/login", (string username, string password) =>
		{
			if (!Users.AuthenticateUser(username, password, out var user))
				return Results.Unauthorized();

			Claim[] claims =
			[
				new (ClaimTypes.Name, user.Name),
				new (ClaimTypes.NameIdentifier, user.Guid.ToString())
			];

			var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

			var now = DateTimeOffset.UtcNow;
			var loginProps = new AuthenticationProperties
			{
				IsPersistent = true,
				AllowRefresh = true,
				ExpiresUtc = now.AddDays(7),
				IssuedUtc = now
			};

			return Results.SignIn(new ClaimsPrincipal(identity), loginProps, CookieAuthenticationDefaults.AuthenticationScheme);
		}).Produces(StatusCodes.Status200OK)
		.Produces(StatusCodes.Status401Unauthorized)
		.WithSummary("Logs the user in, and returns a cookie to use for future requests.")
		.WithTags("Auth", "User")
		.WithName("LoginUser")
		.RequireAuthorization("Client");

		app.MapPost($"{BaseUsersUrl}/logout", () =>
				Results.SignOut(authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme]))
		.Produces(StatusCodes.Status200OK)
		.Produces(StatusCodes.Status400BadRequest)
		.WithSummary("Logs the user out.")
		.WithTags("Auth", "User")
		.WithName("LogoutUser")
		.RequireAuthorization("Client");

		app.MapPost($"{BaseUsersUrl}/{{targetUserId}}/edit", (HttpContext context, [FromBody] Dictionary<string, string?> changes, string targetUserId) =>
		{
			if (changes.Count == 0) return Results.BadRequest("No changes were provided.");

			var targetUser = SecurityMethods.GetConnectedUser(context);
			if (Guid.TryParse(targetUserId, out var targetUserGuid))
			{
				if (!SecurityMethods.ChallengePermission(context, new("users", ["edit-details"])))
				{
					return Results.StatusCode(StatusCodes.Status403Forbidden);
				}
				Users.TryFetchUser(targetUserGuid, out targetUser);
			}
			else if (targetUserId != "me")
			{
				return Results.BadRequest("Invalid target user ID");
			}
			if (targetUser is null) return Results.NotFound();

			if (changes.ContainsKey("Permissions") || changes.ContainsKey("PermissionList"))
			{
				return Results.BadRequest(new { message = "Changing permissions is not allowed through this endpoint." });
			}
			if (changes.ContainsKey("Guid"))
			{
				return Results.BadRequest(new { message = "Changing a user's GUID is not possible." });
			}
			if (changes.ContainsKey("Salt"))
			{
				return Results.BadRequest(new { message = "Setting a user's salt is not possible. You may only change the password, which will randomize the salt." });
			}

			return targetUser.Edit(changes) ? Results.NoContent() : Results.StatusCode(StatusCodes.Status304NotModified);
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status304NotModified)
		.WithSummary("Edit a user's details.")
		.WithDescription("targetUserId must be a valid GUID, or the string 'me' to edit the currently connected user.")
		.WithTags("Auth", "User")
		.WithName("EditUser")
		.RequireAuthorization("MultiAuth");

		app.MapGet($"{BaseUsersUrl}/{{targetUserId}}/info", (HttpContext context, string targetUserId) =>
		{
			var targetUser = SecurityMethods.GetConnectedUser(context);
			if (Guid.TryParse(targetUserId, out var targetUserGuid))
			{
				if (!SecurityMethods.ChallengePermission(context, new("users", ["view-details"])))
				{
					return Results.StatusCode(StatusCodes.Status403Forbidden);
				}
				Users.TryFetchUser(targetUserGuid, out targetUser);
			}
			else if (targetUserId != "me")
			{
				return Results.BadRequest("Invalid target user ID");
			}

			return targetUser is not null
				? Results.Json(targetUser.ToDictionary())
				: Results.NotFound();
		}).Produces(StatusCodes.Status200OK)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status404NotFound)
		.WithSummary("Fetch information about a specific user.")
		.WithDescription(
			"targetUserId must be a valid GUID, or the string 'me' to fetch information about the currently connected user.")
		.WithTags("Auth", "User")
		.WithName("GetUserInfo")
		.RequireAuthorization("MultiAuth");

		app.MapPatch($"{BaseUsersUrl}/{{targetUserId}}/permissions", (HttpContext context, string targetUserId, [FromBody] Dictionary<string, List<string>> permissionsToAdd) =>
		{
			var connectedUser = SecurityMethods.GetConnectedUser(context)!;
			var targetUser = connectedUser;

			if (Guid.TryParse(targetUserId, out var targetUserGuid))
			{
				Users.TryFetchUser(targetUserGuid, out targetUser);
			}
			else if (targetUserId != "me")
			{
				return Results.BadRequest("Invalid target user ID");
			}
			if (targetUser is null) return Results.NotFound();

			// User has to be an admin to modify their own permissions
			if (targetUser == connectedUser)
			{
				if (!SecurityMethods.ChallengePermission(context, new("admin", ["admin"])))
					return Results.StatusCode(StatusCodes.Status403Forbidden);
			}

			bool changesMade = false;
			foreach (var (permission, scopes) in permissionsToAdd)
			{
				var sofaPermission = new Permissions.SofaPermission(permission, scopes);
				if (!connectedUser.HasPermission(sofaPermission)) continue;

				targetUser.AddPermission(sofaPermission);
				changesMade = true;
			}

			if (changesMade) SecurityStores.UserStores.SaveUser(targetUser);
			return changesMade ? Results.NoContent() : Results.StatusCode(StatusCodes.Status304NotModified);
		}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status304NotModified)
		.WithSummary("Add one or more permissions to a user.")
		.WithDescription("targetUserId must be a valid GUID, or the string 'me' to add permissions to the currently connected user.")
		.WithTags("Auth", "User")
		.WithName("AddUserPermissions")
		.RequireAuthorization("MultiAuth", "users:edit-permissions");

		app.MapDelete($"{BaseUsersUrl}/{{targetUserId}}/permissions", (HttpContext context, string targetUserId, [FromBody] Dictionary<string, List<string>> permissionsToRemove) =>
		{
			var connectedUser = SecurityMethods.GetConnectedUser(context)!;
			var targetUser = connectedUser;

			if (Guid.TryParse(targetUserId, out var targetUserGuid))
			{
				Users.TryFetchUser(targetUserGuid, out targetUser);
			}
			else if (targetUserId != "me")
			{
				return Results.BadRequest("Invalid target user ID");
			}
			if (targetUser is null) return Results.NotFound();

			// User has to be an admin to modify their own permissions
			if (targetUser == connectedUser)
			{
				if (!SecurityMethods.ChallengePermission(context, new("admin", ["admin"])))
					return Results.StatusCode(StatusCodes.Status403Forbidden);
			}

			bool changesMade = false;
			foreach (var (permission, scopes) in permissionsToRemove)
			{
				var sofaPermission = new Permissions.SofaPermission(permission, scopes);
				if (!connectedUser.HasPermission(sofaPermission) || !targetUser.PermissionList.ContainsKey(permission)) continue;

				targetUser.RemovePermission(sofaPermission);
				changesMade = true;
			}

			if (changesMade) SecurityStores.UserStores.SaveUser(targetUser);
			return changesMade ? Results.NoContent() : Results.StatusCode(StatusCodes.Status304NotModified);
			}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status304NotModified)
		.WithSummary("Remove one or more permissions from a user.")
		.WithDescription("targetUserId must be a valid GUID, or the string 'me' to remove permissions from the currently connected user.")
		.WithTags("Auth", "User")
		.WithName("RemoveUserPermissions")
		.RequireAuthorization("MultiAuth", "users:edit-permissions");

		app.MapPut($"{BaseUsersUrl}/{{targetUserId}}/permissions", (HttpContext context, string targetUserId, [FromBody] Dictionary<string, List<string>> permissionsToSet) =>
		{
			var connectedUser = SecurityMethods.GetConnectedUser(context)!;
			var targetUser = connectedUser;

			if (Guid.TryParse(targetUserId, out var targetUserGuid))
			{
				Users.TryFetchUser(targetUserGuid, out targetUser);
			}
			else if (targetUserId != "me")
			{
				return Results.BadRequest("Invalid target user ID");
			}
			if (targetUser is null) return Results.NotFound();

			// User has to be an admin to modify their own permissions
			if (targetUser == connectedUser)
			{
				if (!SecurityMethods.ChallengePermission(context, new("admin", ["admin"])))
					return Results.StatusCode(StatusCodes.Status403Forbidden);
			}

			foreach (var (permission, scopes) in permissionsToSet)
			{
				var sofaPermission = new Permissions.SofaPermission(permission, scopes);

				if (!connectedUser.HasPermission(sofaPermission))
					return Results.BadRequest(new { message = "All permissions must be in the list of connected user's permissions.", missingPermission = sofaPermission.ToString() });
			}

			targetUser.SetPermissions(permissionsToSet);
			SecurityStores.UserStores.SaveUser(targetUser);
			return Results.NoContent();
			}).Produces(StatusCodes.Status204NoContent)
		.Produces(StatusCodes.Status400BadRequest)
		.Produces(StatusCodes.Status304NotModified)
		.WithSummary("Replace a user's permissions with the given list.")
		.WithDescription("targetUserId must be a valid GUID, or the string 'me' to set the currently connected user's permissions. " +
		                 "Body must include the new list of permissions in a { \"<PermissionString>\": [\"<scope>\"] } format.\n" +
		                 "This will return a 400 Bad Request if any of the permissions are not in the list of connected user's permissions.")
		.WithTags("Auth", "User")
		.WithName("SetUserPermissions")
		.RequireAuthorization("MultiAuth", "users:edit-permissions");

		app.MapGet($"{BaseUsersUrl}/list", () =>
		{
			return Results.Json(Users.FetchAllUsers());
		}).Produces(StatusCodes.Status200OK)
		.WithSummary("List all registered users.")
		.WithTags("Auth", "User")
		.WithName("GetUserList")
		.RequireAuthorization("MultiAuth", "users:list");
	}
}