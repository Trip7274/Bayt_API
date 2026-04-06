using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Sofa_API;
using Sofa_API.Endpoints;
using Sofa_API.Endpoints.DockerEndpoints.Local;
using Sofa_API.Endpoints.SecurityEndpoints;
using Sofa_API.Logging;
using Sofa_API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

var builder = WebApplication.CreateBuilder(args);


builder.WebHost.ConfigureKestrel(kestrel =>
{
	bool isHttps = Certificates.SofaPublicKey is not null;

	kestrel.ConfigureHttpsDefaults(https =>
	{
		https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
		https.ClientCertificateValidation = (cert, _, _) =>
		{
			// Run basic certificate checks
			if (cert.IsExpiredOrTooNew()) return false;

			// Make sure the cert isn't too long-lasting. (4-month max lifespan)
			return cert.NotAfter - cert.NotBefore <= TimeSpan.FromDays(30 * 4);
		};

		https.ServerCertificate = Certificates.SofaCertificate;
		https.SslProtocols = SslProtocols.Tls12 & SslProtocols.Tls13;
	});

	kestrel.Listen(IPAddress.Loopback, ApiConfig.NetworkPort);
	if (isHttps)
	{
		Logs.LogBook.Write(new (StreamId.Info, "Network Initalization", $"Adding URL 'https://{IPAddress.Loopback}:{ApiConfig.HttpsNetworkPort}' (HTTP at {ApiConfig.NetworkPort}) to listen list"));
		kestrel.Listen(IPAddress.Loopback, ApiConfig.HttpsNetworkPort, listenOptions =>
		{
			listenOptions.UseHttps();
			listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
		});
	}
	else
	{
		Logs.LogBook.Write(new (StreamId.Info, "Network Initalization", $"Adding URL 'http://{IPAddress.Loopback}:{ApiConfig.NetworkPort}' to listen list"));
	}

	if (!ParsingMethods.IsEnvVarTrue("SOFA_LOCALHOST_ONLY"))
	{
		var localIp = StatsApi.GetLocalIpAddress();

		kestrel.Listen(localIp, ApiConfig.NetworkPort, options => options.Protocols = HttpProtocols.Http2);
		if (isHttps)
		{
			Logs.LogBook.Write(new (StreamId.Info, "Network Initalization", $"Adding URL 'https://{localIp}:{ApiConfig.HttpsNetworkPort}' (HTTP at {ApiConfig.NetworkPort}) to listen list"));
			kestrel.Listen(localIp, ApiConfig.HttpsNetworkPort, listenOptions =>
			{
				listenOptions.UseHttps();
				listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
			});
		}
		else
		{
			Logs.LogBook.Write(new (StreamId.Info, "Network Initalization", $"Adding URL 'http://{localIp}:{ApiConfig.NetworkPort}' to listen list"));
		}
	}
	if (!ParsingMethods.IsEnvVarTrue("SOFA_DISABLE_SOCK"))
	{
		if (File.Exists(ApiConfig.UnixSocketPath)) File.Delete(ApiConfig.UnixSocketPath);
		Logs.LogBook.Write(new (StreamId.Info, "Network Initalization", $"Adding URL 'unix:{ApiConfig.UnixSocketPath}' to listen list"));
		kestrel.ListenUnixSocket(ApiConfig.UnixSocketPath);
	}
});

builder.AddAuthenticationSchemes();
builder.AddAuthorizationSchemes();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, Permissions.PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, Permissions.PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, Permissions.SofaAuthorizationMessageHandler>();

Logs.LogBook.Write(new (StreamId.Notice, "Configuration",
	$"Loaded configuration from: '{ApiConfig.ConfigFilePath}'"));

Logs.LogBook.Write(new (StreamId.Notice, "Logging",
	$"Saving logs in: '{Path.Combine(ApiConfig.ApiConfiguration.PathToLogFolder,
		$"[{DateOnly.FromDateTime(DateTime.Now).ToString("O")}] sofaLog.log")}'"));

Logs.LogBook.Write(new (StreamId.Notice, "Client Data",
	$"Serving clientData from: '{ApiConfig.ApiConfiguration.PathToDataFolder}'"));

if (DockerLocal.IsDockerComposeAvailable)
{
	Logs.LogBook.Write(new (StreamId.Notice, "Docker",
		$"Fetching Docker folders from: '{ApiConfig.ApiConfiguration.PathToDockerFolder}'"));
}


builder.Logging.ClearProviders();
var app = builder.Build();
if (ApiConfig.TerminalVerbosity > (byte) StreamId.Request || ApiConfig.ApiConfiguration.LogVerbosity > (byte) StreamId.Request)
	app.UseMiddleware<RequestLoggingMiddleware>();

if (Environment.OSVersion.Platform != PlatformID.Unix)
{
	Logs.LogBook.Write(new (StreamId.Warning, "Initialization",
		$"Detected OS is '{Environment.OSVersion.Platform}', which doesn't appear to be Unix-like. This is unsupported, here be dragons."));
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapClientSecurityEndpoints();
app.MapUserSecurityEndpoints();

app.MapSofaEndpoints();

app.MapStatsEndpoints();
app.MapConfigEndpoints();
app.MapMountsEndpoints();
app.MapWolEndpoints();
app.MapClientDataEndpoints();
app.MapPowerEndpoints();

app.MapDlContaintersEndpoints();
app.MapDlComposeEndpoints();
app.MapDlImagesEndpoints();

if (DockerLocal.IsDockerAvailable)
{
	Logs.LogBook.Write(new (StreamId.Info, "Docker", "Docker is available. Docker endpoints will be available."));

	if (DockerLocal.IsDockerComposeAvailable)
	{
		Logs.LogBook.Write(new (StreamId.Info, "Docker", "Docker-Compose is available. Docker-Compose endpoints will be available."));
	}
}
if (ParsingMethods.IsEnvVarTrue("SOFA_SKIP_FIRST_FETCH"))
{
	Logs.LogBook.Write(new (StreamId.Info, "Initialization", "Skipping first fetch cycle. This may cause the first request to be slow."));
}
else
{
	// Do a fetch cycle to let the constructors run.
	List<Task> fetchTasks = [
		Task.Run(StatsApi.CpuData.UpdateDataIfNecessary),
		Task.Run(GpuHandling.FullGpusData.UpdateDataIfNecessary),
		Task.Run(StatsApi.MemoryData.UpdateDataIfNecessary),
		Task.Run(DiskHandling.FullDisksData.UpdateDataIfNecessary),
		Task.Run(StatsApi.BatteryList.UpdateDataIfNecessary)
	];

	if (DockerLocal.IsDockerAvailable)
	{
		fetchTasks.Add(Task.Run(DockerLocal.DockerContainers.UpdateDataIfNecessary));
		fetchTasks.Add(Task.Run(DockerLocal.ImagesInfo.UpdateDataIfNecessary));
	}

	Logs.LogBook.Write(new (StreamId.Info, "Initialization", "Running an initial fetch cycle..."));
	await Task.WhenAll(fetchTasks);

	Logs.LogBook.Write(new (StreamId.Ok, "Initialization", "Fetch cycle complete."));
}
Logs.LogBook.Write(new (StreamId.Ok, "Initialization", "::: Sofa API is ready :::"));

try
{
	app.Run();
}
catch (SocketException e) when (e.Message == "Cannot assign requested address")
{
	Logs.LogBook.Write(new (StreamId.Fatal, "Network Initalization",
		"Something went wrong while binding to one of the targetted IP addresses. Make sure the targetted IP address is valid."));
}
catch (SocketException e) when (e.Message == "Permission denied")
{
	Logs.LogBook.Write(new (StreamId.Fatal, "Network Initalization",
		"The current user does not have permission to bind to one of the IP addresses or ports."));
}
catch (IOException e) when (e.InnerException is not null && e.InnerException.Message == "Address already in use")
{
	Logs.LogBook.Write(new (StreamId.Fatal, "Network Initalization",
		$"Port {ApiConfig.NetworkPort} is already in use. Another instance of Sofa may be running."));
}
finally
{
	Logs.LogBook.Write(new (StreamId.Info, "Shutdown", "Sofa is shutting down."));
	Logs.LogBook.Dispose();
}