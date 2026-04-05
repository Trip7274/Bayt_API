using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace Sofa_API.Security;

public static class Permissions
{
	/*
	 Expecting:
	 "users:list" -> "User can read user list"
	 "docker-compose:read,write" -> "Client can read/write docker compose containers"
	 "wol:*" -> "Can do anything regarding WoL endpoints"
	 "docker-compose:!create" -> "Explicitly CANNOT create additional docker compose containers"
	 "docker-compose:*,!create" -> "Client can do anything regarding docker compose containers, but CANNOT create additional ones"
	*/
	public sealed class SofaPermission : IAuthorizationRequirement
	{
		public SofaPermission(string permissionString, List<string>? permPowers = null)
		{
			if (string.IsNullOrWhiteSpace(permissionString)) throw new ArgumentException("Permission string cannot be empty", nameof(permissionString));

			PermissionString = permissionString;
			if (permPowers is not null) PermPowers = permPowers;
		}
		public SofaPermission(string permissionString, string? permPowers = null)
		{
			if (string.IsNullOrWhiteSpace(permissionString)) throw new ArgumentException("Permission string cannot be empty", nameof(permissionString));

			PermissionString = permissionString;
			if (string.IsNullOrWhiteSpace(permPowers)) return;

			if (!permPowers.Contains(',')) PermPowers = [permPowers];
			else
			{
				PermPowers = permPowers.Split(',').Select(p => p.Trim()).ToList();
			}
		}

		public string PermissionString { get; }
		public List<string> PermPowers { get; } = [];


		public static bool TryParse(string constructedPerm, [NotNullWhen(true)] out SofaPermission? permReq)
		{
			if (constructedPerm.Count(c => c == ':') != 1)
			{
				permReq = null;
				return false;
			}

			constructedPerm = constructedPerm.ToLowerInvariant();
			var splitString = constructedPerm.Split(':');
			if (splitString.Length != 2)
			{
				permReq = null;
				return false;
			}

			// Input: "docker-compose:create,list"

			var permissionString = splitString[0];
			if (!splitString[1].Contains(','))
			{
				permReq = new SofaPermission(permissionString, splitString[1]);
				return true;
			}
			var permPowers = splitString[1].Split(',').Select(p => p.Trim()).ToList();


			permReq = new SofaPermission(permissionString, permPowers);
			return true;
		}

		public bool Allows(List<string> challengedPerms)
		{
			var hasNegated = challengedPerms.Any(p => p.StartsWith('!'));
			var hasWildcard = challengedPerms.Any(p => p == "*");

			// "docker-compose:*"
			if (!hasNegated && hasWildcard) return true;

			// "docker-compose:*,!create"
			if (hasWildcard)
			{
				var negativePermPowers = challengedPerms.Where(p => p.StartsWith('!')).Select(p => p[1..]).ToList();
				return !PermPowers.Intersect(negativePermPowers).Any();
			}

			// "docker-compose:create" OR "docker-compose:!create"
			var intersection = PermPowers.Intersect(challengedPerms).ToList();
			if (hasNegated) intersection.RemoveAll(p => p.StartsWith('!'));
			return intersection.Count == PermPowers.Count;
		}

		public string PermPowersString => string.Join(',', PermPowers);

		public override string ToString() => $"{PermissionString}:{PermPowersString}";
		public static implicit operator string(SofaPermission perm) => perm.ToString();
	}

	public sealed class PermissionHandler : AuthorizationHandler<SofaPermission>
	{
		protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SofaPermission requirement)
		{
			// Try to fetch the Client.Guid connecting
			var clientIdentity = context.User.Identities.FirstOrDefault(identity => identity is
			{
				IsAuthenticated: true, AuthenticationType: CertificateAuthenticationDefaults.AuthenticationScheme
			});
			var clientIdentifier = clientIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

			// Try to fetch the User.Guid connecting
			var cookieIdentity = context.User.Identities.FirstOrDefault(identity => identity is
			{
				IsAuthenticated: true, AuthenticationType: CookieAuthenticationDefaults.AuthenticationScheme
			});

			var userIdentifier = cookieIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

			if (clientIdentifier is null && userIdentifier is null) return Task.CompletedTask;

			HasPermissions? clientRequester = null;
			HasPermissions? userRequester = null;
			if (Guid.TryParse(clientIdentifier, out var clientGuid) && !HasPermissions.TryFetchRequester(clientGuid, out clientRequester) ||
			    Guid.TryParse(userIdentifier, out var userGuid) && !HasPermissions.TryFetchRequester(userGuid, out userRequester))
				return Task.CompletedTask;

			if ((clientRequester?.HasPermission(requirement) ?? true) &&
			    (userRequester?.HasPermission(requirement) ?? true))
			{
				context.Succeed(requirement);
			}
			return Task.CompletedTask;
		}
	}

	public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
		: DefaultAuthorizationPolicyProvider(options)
	{
		public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
		{
			if (!SofaPermission.TryParse(policyName, out var requirement))
				return await base.GetPolicyAsync(policyName);

			var builder = new AuthorizationPolicyBuilder();

			builder.AddRequirements(requirement);
			return builder.Build();

		}
	}

	public sealed class SofaAuthorizationMessageHandler : IAuthorizationMiddlewareResultHandler
	{
		private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

		public async Task HandleAsync(
			RequestDelegate next,
			HttpContext context,
			AuthorizationPolicy policy,
			PolicyAuthorizationResult authorizeResult)
		{
			if (authorizeResult.Forbidden &&
			    (authorizeResult.AuthorizationFailure?.FailureReasons ?? []).Any())
			{
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				context.Response.ContentType = "text/plain";

				var message = authorizeResult.AuthorizationFailure?.FailureReasons.FirstOrDefault()?.Message ?? "Access denied.";
				await context.Response.WriteAsync(message);
				return;
			}

			if (authorizeResult.Challenged)
			{
				if (context.Response.ContentLength > 0) return;

				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				context.Response.ContentType = "text/plain";
				await context.Response.WriteAsync("Authentication is required.");
				return;
			}

			await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
		}
	}
}