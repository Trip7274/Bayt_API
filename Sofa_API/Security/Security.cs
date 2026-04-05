using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace Sofa_API.Security;

public static class SecurityMethods
{
	// Authn and Authz extensions
	extension (WebApplicationBuilder builder)
	{
		public void AddAuthenticationSchemes()
		{
			builder.Services.AddAuthentication().AddCertificate(options =>
			{
				options.AllowedCertificateTypes = CertificateTypes.All;
				options.RevocationMode = X509RevocationMode.NoCheck;
				options.ValidateValidityPeriod = false;

				options.Events = new CertificateAuthenticationEvents
				{
					OnCertificateValidated = context =>
					{
						var thumbprint = context.ClientCertificate.GetCertHashString(HashAlgorithmName.SHA256);
						if (Clients.TryFetchValidClient(thumbprint, out var client, true))
						{
							context.Principal = new ClaimsPrincipal(
								new ClaimsIdentity(
								[
									new Claim(ClaimTypes.Thumbprint, client.Thumbprint),
									new Claim(ClaimTypes.NameIdentifier, client.Guid.ToString())
								], CertificateAuthenticationDefaults.AuthenticationScheme));

							context.Success();
							return Task.CompletedTask;
						}

						Logs.LogBook.Write(new LogEntry(StreamId.Verbose, "Authn [Certificate]", $"Certificate thumbprint '{thumbprint}' was not found in the database."));
						context.Fail("Invalid certificate");
						return Task.CompletedTask;
					},
					OnAuthenticationFailed = context =>
					{
						switch (context.Exception.Message)
						{
							case "Client certificate failed validation.":
							{
								context.Fail("The presented certificate failed validation.");
								break;
							}
							case "Invalid certificate":
							{
								context.Fail("The presented certificate was not found in Sofa's trusted database.");
								break;
							}
							default:
							{
								Console.WriteLine($"Failed to authenticate certificate: {context.Exception.Message}");
								break;
							}
						}

						return Task.CompletedTask;
					}
				};
			}).AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
			{
				options.Cookie.Name = "SofaCookie";
				options.Cookie.HttpOnly = true;
				options.Cookie.SameSite = SameSiteMode.Strict;
				options.ExpireTimeSpan = TimeSpan.FromDays(7);
				options.SlidingExpiration = true;

				options.Events = new CookieAuthenticationEvents
				{
					OnRedirectToLogin = context =>
					{
						context.Response.StatusCode = StatusCodes.Status401Unauthorized;
						context.Response.ContentType = "text/plain";
						return context.Response.WriteAsync("User authentication is required.");
					},
					OnRedirectToAccessDenied = context =>
					{
						context.Response.StatusCode = StatusCodes.Status403Forbidden;
						context.Response.ContentType = "text/plain";
						return context.Response.WriteAsync("You are authenticated, but you lack sufficient permissions to access this resource.");
					}
				};
			});
		}
		public void AddAuthorizationSchemes()
		{
			builder.Services.AddAuthorization(options =>
			{
				options.AddPolicy("User", policy =>
				{
					policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme);
					policy.RequireAuthenticatedUser();

					policy.RequireAssertion(AuthnUser);
				});
				options.AddPolicy("Client", policy =>
				{
					policy.AddAuthenticationSchemes(CertificateAuthenticationDefaults.AuthenticationScheme);
					policy.RequireAuthenticatedUser();

					policy.RequireAssertion(context => AuthnClient(context, false));
				});
				options.AddPolicy("ClientAllowPaused", policy =>
				{
					policy.AddAuthenticationSchemes(CertificateAuthenticationDefaults.AuthenticationScheme);
					policy.RequireAuthenticatedUser();

					policy.RequireAssertion(context => AuthnClient(context, true));
				});
				options.AddPolicy("MultiAuth", policy =>
				{
					policy.AddAuthenticationSchemes(
						CertificateAuthenticationDefaults.AuthenticationScheme,
						CookieAuthenticationDefaults.AuthenticationScheme);
					policy.RequireAuthenticatedUser();

					policy.RequireAssertion(context =>
					{
						var hasCertificate = context.User.Identities.Any(identity => identity is { IsAuthenticated: true, AuthenticationType: CertificateAuthenticationDefaults.AuthenticationScheme }) && AuthnClient(context);
						var hasCookie = context.User.Identities.Any(identity => identity is { IsAuthenticated: true, AuthenticationType: CookieAuthenticationDefaults.AuthenticationScheme }) && AuthnUser(context);

						if (!hasCertificate)
						{
							context.Fail(new(null!, "A valid client certificate is required for this operation."));
							return false;
						}
						if (!hasCookie)
						{
							context.Fail(new(null!, "A valid user cookie is required for this operation."));
							return false;
						}

						return hasCookie && hasCertificate;
					});
				});

				options.DefaultPolicy = options.GetPolicy("MultiAuth")!;
			});
		}
	}

	public static bool AuthnUser(AuthorizationHandlerContext context)
	{
		var cookieIdentity = context.User.Identities.FirstOrDefault(identity => identity is
		{
			IsAuthenticated: true, AuthenticationType: CookieAuthenticationDefaults.AuthenticationScheme
		});

		var userIdentifier = cookieIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

		return Guid.TryParse(userIdentifier, out var guid) && Users.DoesUserExist(guid);
	}
	public static bool AuthnClient(AuthorizationHandlerContext context, bool allowPaused = false)
	{
		var clientIdentity = context.User.Identities.FirstOrDefault(identity => identity is
		{
			IsAuthenticated: true, AuthenticationType: CertificateAuthenticationDefaults.AuthenticationScheme
		});

		var guidString = clientIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

		return Guid.TryParse(guidString, out var guid) &&
		       Clients.TryFetchValidClient(guid, out var client, allowPaused) &&
		       (!client.IsPaused || allowPaused);
	}


	public static Client? GetConnectedClient(HttpContext context, bool allowPaused = false)
	{
		var clientIdentity = context.User.Identities.FirstOrDefault(identity => identity is
		{
			IsAuthenticated: true, AuthenticationType: CertificateAuthenticationDefaults.AuthenticationScheme
		});
		var clientIdentifier = clientIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

		if (!Guid.TryParse(clientIdentifier, out var guid))
			return null;

		return Clients.TryFetchValidClient(guid, out var client, allowPaused) ? client : null;
	}
	public static User? GetConnectedUser(HttpContext context)
	{
		var cookieIdentity = context.User.Identities.FirstOrDefault(identity => identity is
		{
			IsAuthenticated: true, AuthenticationType: CookieAuthenticationDefaults.AuthenticationScheme
		});

		var userIdentifier = cookieIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

		if (!Guid.TryParse(userIdentifier, out var guid)) return null;

		return Users.TryFetchUser(guid, out var user) ? user : null;
	}
	public static (Client?, User?) GetConnectedUserAndClient(HttpContext context, bool allowPaused = false) => (
		GetConnectedClient(context, allowPaused),
		GetConnectedUser(context)
	);


	public static bool ChallengePermission(HttpContext context, Permissions.SofaPermission challengingPermission)
	{
		var (client, user) = GetConnectedUserAndClient(context);
		if (client is null || user is null) return false;
		return user.HasPermission(challengingPermission) && client.HasPermission(challengingPermission);
	}
}