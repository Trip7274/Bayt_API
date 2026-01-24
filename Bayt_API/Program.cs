using System.Net;
using System.Net.Sockets;
using Bayt_API;
using Bayt_API.Endpoints;
using Bayt_API.Endpoints.DockerEndpoints.Local;

var builder = WebApplication.CreateBuilder(args);

Logs.LogBook.Write(new (StreamId.Info, "Network Initalization", $"Adding URL '{IPAddress.Loopback}:{ApiConfig.NetworkPort}' to listen list"));
builder.WebHost.ConfigureKestrel(opts => opts.Listen(IPAddress.Loopback, ApiConfig.NetworkPort));

if (!ParsingMethods.IsEnvVarTrue("BAYT_LOCALHOST_ONLY"))
{
	var localIp = StatsApi.GetLocalIpAddress();

	Logs.LogBook.Write(new (StreamId.Info, "Network Initalization", $"Adding URL '{localIp}:{ApiConfig.NetworkPort}' to listen list"));
	builder.WebHost.ConfigureKestrel(opts => opts.Listen(localIp, ApiConfig.NetworkPort));
}

if (!ParsingMethods.IsEnvVarTrue("BAYT_DISABLE_SOCK"))
{
	if (File.Exists(ApiConfig.UnixSocketPath)) File.Delete(ApiConfig.UnixSocketPath);
	Logs.LogBook.Write(new (StreamId.Info, "Network Initalization", $"Adding URL 'unix:{ApiConfig.UnixSocketPath}' to listen list"));
	builder.WebHost.ConfigureKestrel(opts => opts.ListenUnixSocket(ApiConfig.UnixSocketPath));
}


Logs.LogBook.Write(new (StreamId.Notice, "Configuration",
	$"Loaded configuration from: '{ApiConfig.ConfigFilePath}'"));

Logs.LogBook.Write(new (StreamId.Notice, "Logging",
	$"Saving logs in: '{Path.Combine(ApiConfig.ApiConfiguration.PathToLogFolder,
		$"[{DateOnly.FromDateTime(DateTime.Now).ToString("O")}] baytLog.log")}'"));

Logs.LogBook.Write(new (StreamId.Notice, "Client Data",
	$"Serving clientData from: '{ApiConfig.ApiConfiguration.PathToDataFolder}'"));

if (DockerLocal.IsDockerComposeAvailable)
{
	Logs.LogBook.Write(new (StreamId.Notice, "Docker",
		$"Fetching Docker folders from: '{ApiConfig.ApiConfiguration.PathToDockerFolder}'"));
}


builder.Logging.ClearProviders();
var app = builder.Build();

app.UseHttpsRedirection();

if (Environment.OSVersion.Platform != PlatformID.Unix)
{
	Logs.LogBook.Write(new (StreamId.Warning, "Initialization",
		$"Detected OS is '{Environment.OSVersion.Platform}', which doesn't appear to be Unix-like. This is unsupported, here be dragons."));
}


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
if (ParsingMethods.IsEnvVarTrue("BAYT_SKIP_FIRST_FETCH"))
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
Logs.LogBook.Write(new (StreamId.Ok, "Initialization", "::: Bayt API is ready :::"));

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
		$"Port {ApiConfig.NetworkPort} is already in use. Another instance of Bayt may be running."));
}
finally
{
	Logs.LogBook.Write(new (StreamId.Info, "Shutdown", "Bayt is shutting down."));
	Logs.LogBook.Dispose();
}