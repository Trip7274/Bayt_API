using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Bayt_API;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

Logs.LogStream.Write(new LogEntry(StreamId.Info, "Network Initalization", $"Adding URL '{IPAddress.Loopback}:{ApiConfig.NetworkPort}' to listen list"));
builder.WebHost.ConfigureKestrel(opts => opts.Listen(IPAddress.Loopback, ApiConfig.NetworkPort));

if (Environment.GetEnvironmentVariable("BAYT_LOCALHOST_ONLY") != "1")
{
	var localIp = StatsApi.GetLocalIpAddress();

	Logs.LogStream.Write(new LogEntry(StreamId.Info, "Network Initalization", $"Adding URL '{localIp}:{ApiConfig.NetworkPort}' to listen list"));
	builder.WebHost.ConfigureKestrel(opts => opts.Listen(localIp, ApiConfig.NetworkPort));
}

if (Environment.GetEnvironmentVariable("BAYT_DISABLE_SOCK") != "1")
{
	if (File.Exists(ApiConfig.UnixSocketPath)) File.Delete(ApiConfig.UnixSocketPath);
	Logs.LogStream.Write(new LogEntry(StreamId.Info, "Network Initalization", $"Adding URL 'unix:{ApiConfig.UnixSocketPath}' to listen list"));
	builder.WebHost.ConfigureKestrel(opts => opts.ListenUnixSocket(ApiConfig.UnixSocketPath));
}


Logs.LogStream.Write(new LogEntry(StreamId.Notice, "Configuration",
	$"Loaded configuration from: '{ApiConfig.ConfigFilePath}'"));

Logs.LogStream.Write(new LogEntry(StreamId.Notice, "Client Data",
	$"Loaded clientData from: '{ApiConfig.ApiConfiguration.PathToDataFolder}'"));

if (Docker.IsDockerComposeAvailable)
{
	Logs.LogStream.Write(new LogEntry(StreamId.Notice, "Containers",
		$"Loaded containers from: '{ApiConfig.ApiConfiguration.PathToComposeFolder}'"));
}


builder.Logging.ClearProviders();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();

if (Environment.OSVersion.Platform != PlatformID.Unix)
{
	Logs.LogStream.Write(new LogEntry(StreamId.Warning, "Initialization",
		$"Detected OS is '{Environment.OSVersion.Platform}', which doesn't appear to be Unix-like. This is unsupported, here be dragons."));
}



app.MapGet($"{ApiConfig.BaseApiUrlPath}/stats/getCurrent", async (bool? meta, bool? system, bool? cpu, bool? gpu, bool? memory, bool? mounts) =>
	{
		Dictionary<ApiConfig.SystemStats, bool?> requestedStatsRaw = new() {
			{ ApiConfig.SystemStats.Meta, meta },
			{ ApiConfig.SystemStats.System, system },
			{ ApiConfig.SystemStats.Cpu, cpu },
			{ ApiConfig.SystemStats.Gpu, gpu },
			{ ApiConfig.SystemStats.Memory, memory },
			{ ApiConfig.SystemStats.Mounts, mounts }
		};
		List<ApiConfig.SystemStats> requestedStats = [];
		if (requestedStatsRaw.All(stat => !stat.Value.HasValue))
		{
			requestedStats = ApiConfig.PossibleStats.ToList();
		}
		else
		{
			requestedStats.AddRange(from statKvp in requestedStatsRaw where
				statKvp.Value.HasValue && statKvp.Value.Value select statKvp.Key);
		}

		if (requestedStats.Count == 0)
		{
			return Results.BadRequest("No stats were requested.");
		}
		Logs.LogStream.Write(new LogEntry(StreamId.Verbose, "GetStats", $"Got a request for: {string.Join(", ", requestedStats.Select(stat => stat.ToString()))}"));

		// Request checks done

		Dictionary<string, Dictionary<string, dynamic?>[]> responseDictionary = [];

		// Queue and update all the requested stats up asynchronously
		List<Task> fetchTasks = [];
		foreach (var stat in requestedStats)
		{
			switch (stat)
			{
				case ApiConfig.SystemStats.Cpu:
				{
					fetchTasks.Add(Task.Run(StatsApi.CpuData.UpdateDataIfNecessary));
					break;
				}

				case ApiConfig.SystemStats.Gpu:
				{
					fetchTasks.Add(Task.Run(GpuHandling.FullGpusData.UpdateDataIfNecessary));
					break;
				}

				case ApiConfig.SystemStats.Memory:
				{
					fetchTasks.Add(Task.Run(StatsApi.MemoryData.UpdateDataIfNecessary));
					break;
				}

				case ApiConfig.SystemStats.Mounts:
				{
					fetchTasks.Add(Task.Run(DiskHandling.FullDisksData.UpdateDataIfNecessary));
					break;
				}
			}
		}
		await Task.WhenAll(fetchTasks);

		// Request assembly
		foreach (var requestedStat in requestedStats)
		{
			switch (requestedStat)
			{
				case ApiConfig.SystemStats.Meta:
				{
					responseDictionary.Add("Meta", [
						new Dictionary<string, dynamic?>
						{
							{ nameof(ApiConfig.Version), ApiConfig.Version },
							{ nameof(ApiConfig.ApiVersion), ApiConfig.ApiVersion },
							{ nameof(ApiConfig.ApiConfiguration.SecondsToUpdate),
								ApiConfig.ApiConfiguration.SecondsToUpdate },
							{ "BaytUptime", ApiConfig.BaytStartStopwatch.Elapsed }
						}
					]);
					break;
				}

				case ApiConfig.SystemStats.System:
				{
					responseDictionary.Add("System", [ StatsApi.GeneralSpecs.ToDictionary()! ]);
					break;
				}

				case ApiConfig.SystemStats.Cpu:
				{
					responseDictionary.Add("CPU", [ StatsApi.CpuData.ToDictionary() ]);
					break;
				}

				case ApiConfig.SystemStats.Gpu:
				{
					responseDictionary.Add("GPU", GpuHandling.FullGpusData.ToDictionary());
					break;
				}

				case ApiConfig.SystemStats.Memory:
				{
					responseDictionary.Add("Memory", [ StatsApi.MemoryData.ToDictionary()! ]);
					break;
				}

				case ApiConfig.SystemStats.Mounts:
				{
					responseDictionary.Add("Mounts", DiskHandling.FullDisksData.ToDictionary());
					break;
				}
				default:
				{
					throw new ArgumentOutOfRangeException(requestedStat.ToString());
				}
			}
		}
		Logs.LogStream.Write(new LogEntry(StreamId.Verbose, "GetStats", $"Sent off the response with {responseDictionary.Count} fields."));
		return Results.Json(responseDictionary);
	}).Produces(StatusCodes.Status200OK)
	.Produces(StatusCodes.Status400BadRequest)
	.WithSummary("Returns the stats/metrics of the server according to what was requested. Defaults to all in case none were specified.")
	.WithTags("Stats")
	.WithName("GetSystemMetrics");



// API Config management endpoints

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



// Mount management endpoints

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



// WoL endpoints

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



// Client Data endpoints
// I'd like to note that I feel like the names for these can still use some work.

app.MapGet($"{ApiConfig.BaseApiUrlPath}/clientData/getData", async (string? folderName, string? fileName) =>
{
	if (folderName is null || fileName is null)
	{
		return Results.BadRequest("Both the folder and file name must be specified.");
	}

	DataEndpointManagement.DataFileMetadata fileRecord;
	try
	{
		fileRecord = DataEndpointManagement.GetDataFile(folderName, fileName);
	}
	catch(ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (Exception e) when(e is FileNotFoundException or DirectoryNotFoundException)
	{
		return Results.NotFound(e.Message);
	}

	if (!fileRecord.FileName.EndsWith(".json"))
	{
		return Results.File(fileRecord.FileStreamRead, "application/ocetet-stream",
			fileRecord.FileName, fileRecord.LastWriteTime);
	}

	await fileRecord.FileStreamRead.DisposeAsync();
	return Results.Text(Encoding.UTF8.GetString(fileRecord.FileData ?? []), "application/json", Encoding.UTF8, StatusCodes.Status200OK);

}).Produces(StatusCodes.Status200OK)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.WithSummary("Fetch a specific file from a specific folder in the base clientData folder.")
	.WithDescription("Both parameters are required and must be valid, non-empty file/folder names. If the file ends with .json, it will be returned as a JSON object. Otherwise, it will be returned as a binary file.")
	.WithTags("clientData")
	.WithName("GetClientData");

app.MapPut($"{ApiConfig.BaseApiUrlPath}/clientData/setData", async (HttpContext context, string? folderName, string? fileName) =>
{
	if (folderName is null || fileName is null)
	{
		return Results.BadRequest("Both the folder and file name must be specified.");
	}

	var memoryStream = new MemoryStream();
	await context.Request.Body.CopyToAsync(memoryStream);
	DataEndpointManagement.DataFileMetadata metadataObject;

	try
	{
		metadataObject = new(folderName, fileName, memoryStream.ToArray());
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	await DataEndpointManagement.SetDataFile(metadataObject);

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.WithSummary("Replace/Set a specific file under a specific folder in the base clientData folder. Will create the folder if it doesn't exist.")
	.WithDescription("Both parameters are required and must be valid, non-empty file/folder names. Expects the file's content in the body of the request.")
	.WithTags("clientData")
	.WithName("SetClientData");

app.MapDelete($"{ApiConfig.BaseApiUrlPath}/clientData/deleteData", (string? folderName, string? fileName) =>
{
	if (folderName is null || fileName is null)
	{
		return Results.BadRequest("Both folder and file name must be specified.");
	}

	try
	{
		DataEndpointManagement.DeleteDataFile(folderName, fileName);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException)
	{
		return Results.StatusCode(StatusCodes.Status304NotModified);
	}
	catch (DirectoryNotFoundException)
	{
		return Results.NotFound($"Folder '{folderName}' was not found.");
	}

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status304NotModified)
	.WithSummary("Delete the specified file under the clientData folder.")
	.WithDescription("Both parameters are required and must be valid, non-empty file/folder names.")
	.WithTags("clientData")
	.WithName("DeleteClientData");

app.MapDelete($"{ApiConfig.BaseApiUrlPath}/clientData/deletefolder", (string? folderName) =>
{
	if (string.IsNullOrWhiteSpace(folderName)) return Results.BadRequest("Folder name must be specified.");

	try
	{
		DataEndpointManagement.DeleteDataFolder(folderName);
	}
	catch (ArgumentException)
	{
		return Results.BadRequest("Folder name must not be empty or invalid.");
	}
	catch (DirectoryNotFoundException)
	{
		return Results.StatusCode(StatusCodes.Status304NotModified);
	}

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status304NotModified)
	.WithSummary("Delete a specific data folder. Will recursively delete everything inside it.")
	.WithDescription("folderName must be a valid, non-empty folder name.")
	.WithTags("clientData")
	.WithName("DeleteClientDataFolder");



// Power endpoints
app.MapPost($"{ApiConfig.BaseApiUrlPath}/power/shutdown", async () =>
{
	Logs.LogStream.Write(new LogEntry(StreamId.Info, "Server Power", "Recieved poweroff request, attempting to shut down..."));
	var operationShell = await ShellMethods.RunShell("sudo", ["-n", "/sbin/poweroff"]);

	// Realistically, execution shouldn't get this far.

	if (operationShell.IsSuccess) return Results.NoContent();

	Dictionary<string, string> errorMessage = new()
	{
		{ "Message", "Seems like the shutdown operation failed. Did you run SetupBayt.sh on this user?" },
		{ "stdout", operationShell.StandardOutput },
		{ "stderr", operationShell.StandardError }
	};
	return Results.InternalServerError(errorMessage);

}).Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Shutdown the system.")
	.WithTags("Power")
	.WithName("ShutdownServer");

app.MapPost($"{ApiConfig.BaseApiUrlPath}/power/restart", async () =>
{
	Logs.LogStream.Write(new LogEntry(StreamId.Info, "Server Power", "Recieved restart request, attempting to restart..."));
	var operationShell = await ShellMethods.RunShell("sudo", ["-n", "/sbin/reboot"]);

	if (operationShell.IsSuccess) return Results.NoContent();

	Dictionary<string, string> errorMessage = new()
	{
		{ "Message", "Seems like the restart operation failed. Did you run SetupBayt.sh on this user?" },
		{ "stdout", operationShell.StandardOutput },
		{ "stderr", operationShell.StandardError }
	};
	return Results.InternalServerError(errorMessage);

}).Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Restart the system.")
	.WithTags("Power")
	.WithName("RestartServer");



// Docker endpoints

string baseDockerUrl = $"{ApiConfig.BaseApiUrlPath}/docker";

app.MapGet($"{baseDockerUrl}/containers/getList", async (bool all = true) =>
{
	if (!Docker.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
	await Docker.DockerContainers.UpdateDataIfNecessary();


	return Results.Json(Docker.DockerContainers.ToDictionary(all));
}).Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status200OK)
	.WithSummary("Fetch all or only the currently active containers in the system.")
	.WithTags("Docker")
	.WithName("GetDockerContainers");

app.MapPost($"{baseDockerUrl}/containers/start", async (string? containerId) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
	if (requestValidation is not null) return requestValidation;

	var targetContainer = Docker.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

	return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
		: await targetContainer.Start();
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to start a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("StartDockerContainer");

app.MapPost($"{baseDockerUrl}/containers/stop", async (string? containerId) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
	if (requestValidation is not null) return requestValidation;

	var targetContainer = Docker.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

	return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
		: await targetContainer.Stop();
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to stop a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("StopDockerContainer");

app.MapPost($"{baseDockerUrl}/containers/restart", async (string? containerId) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
	if (requestValidation is not null) return requestValidation;

	var targetContainer = Docker.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

	return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
		: await targetContainer.Restart();
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to restart a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("RestartDockerContainer");

app.MapPost($"{baseDockerUrl}/containers/kill", async (string? containerId) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
	if (requestValidation is not null) return requestValidation;

	var targetContainer = Docker.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

	return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
		: await targetContainer.Kill();
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status409Conflict)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to kill a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("KillDockerContainer");

app.MapDelete($"{baseDockerUrl}/containers/delete", async (string? containerId, bool removeCompose = false, bool removeVolumes = false, bool force = false) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
	if (requestValidation is not null) return requestValidation;

	var targetContainer = Docker.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

	return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
		: await targetContainer.Delete(removeCompose, removeVolumes, force);
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status409Conflict)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to delete a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID. " +
	                 "removeCompose will delete the entire compose directory if true, " +
	                 "and removeVolumes will remove all anonymous volumes associated with the container if true. " +
	                 "Both default to false.")
	.WithTags("Docker")
	.WithName("DeleteDockerContainer");

app.MapPost($"{baseDockerUrl}/containers/pause", async (string? containerId) =>
	{
		var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
		if (requestValidation is not null) return requestValidation;

		var targetContainer = Docker.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

		return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
			: await targetContainer.Pause();
	}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to pause a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("PauseDockerContainer");

app.MapPost($"{baseDockerUrl}/containers/resume", async (string? containerId) =>
	{
		var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
		if (requestValidation is not null) return requestValidation;

		var targetContainer = Docker.DockerContainers.Containers.Find(container => container.Id.StartsWith(containerId));

		return targetContainer is null ? Results.NotFound($"Container with ID '{containerId}' was not found.")
			: await targetContainer.Unpause();
	}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to resume a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("ResumeDockerContainer");

app.MapGet($"{baseDockerUrl}/containers/streamLogs", Docker.StreamDockerLogs)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.WithSummary("Stream the Docker container's live logs.")
	.WithDescription("containerId is required. stdout, stderr, and timestamps are optional to specify which streams to follow, and whether to prefix each line with a timestamp. Will default to stdout=true, stderr=true, and timestamps=false if not specified.")
	.WithTags("Docker")
	.WithName("GetDockerContainerLogs");



// Docker Compose endpoints

app.MapGet($"{baseDockerUrl}/containers/getCompose", async (string? containerId) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId, true, true);
	if (requestValidation is not null) return requestValidation;

	Docker.DockerContainer targetContainer;
	try
	{
		targetContainer = Docker.DockerContainers.Containers.First(container => container.Id.StartsWith(containerId));
	}
	catch (InvalidOperationException)
	{
		return Results.NotFound($"Container with ID '{containerId}' was not found.");
	}
	if (targetContainer.ComposePath is null)
		return Results.NotFound($"Container with ID '{containerId}' does not have a compose file.");

	var stream = new FileStream(targetContainer.ComposePath, FileMode.Open, FileAccess.Read, FileShare.Read);

	return Results.File(stream, "application/ocetet-stream", Path.GetFileName(targetContainer.ComposePath),
		File.GetLastWriteTime(targetContainer.ComposePath));

}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status200OK)
	.WithSummary("Fetch the compose file of a specific Docker container. Will return the exact contents in an 'application/ocetet-stream' response.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("GetDockerContainerCompose");

app.MapPut($"{baseDockerUrl}/containers/setCompose", async (HttpContext context, string? containerId, bool restartContainer = false) =>
{
	if (!context.Request.Headers.ContentEncoding.Contains("chunked") && context.Request.ContentLength is null or 0)
	{
		return Results.StatusCode(StatusCodes.Status411LengthRequired);
	}

	// Request validation
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId, true, true);
	if (requestValidation is not null) return requestValidation;

	// Container validation
	Docker.DockerContainer targetContainer;
	try
	{
		targetContainer = Docker.DockerContainers.Containers.First(container => container.Id.StartsWith(containerId));
	}
	catch (InvalidOperationException)
	{
		return Results.NotFound($"Container with ID '{containerId}' was not found.");
	}
	if (targetContainer.ComposePath is null)
		return Results.NotFound("This container does not have a compose file.");
	if (!targetContainer.IsManaged) return Results.BadRequest("This container is not managed by Bayt.");

	// Actual logic
	await using (var fileStream = new FileStream(targetContainer.ComposePath, FileMode.Truncate, FileAccess.Write,
		             FileShare.None))
	{
		await context.Request.Body.CopyToAsync(fileStream);
	}

	if (!restartContainer) return Results.NoContent();


	var dockerRequest = await Docker.SendRequest($"containers/{containerId}/restart", "POST");
	return dockerRequest.Status switch
	{
		HttpStatusCode.NoContent => Results.NoContent(),
		HttpStatusCode.NotFound => Results.NotFound($"[Reboot step] Container with ID '{containerId}' was not found."),
		HttpStatusCode.InternalServerError => Results.InternalServerError(
			$"[Reboot step] Docker returned an error while restarting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		_ => Results.InternalServerError(
			$"[Reboot step] Docker returned an unknown error while restarting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
	};
	}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status411LengthRequired)
	.WithSummary("Replace a Docker container's compose file if it's managed by Bayt.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID. Will default to not restarting the container after replacing its compose. The new compose's contents are expected to be in the body of the request.")
	.WithTags("Docker")
	.WithName("SetDockerContainerCompose");

app.MapPost($"{baseDockerUrl}/containers/own", async (string? containerId) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId, true, true);
	if (requestValidation is not null) return requestValidation;

	var defaultSidecarContents = JsonSerializer.Serialize(Docker.DockerContainers.GetDefaultMetadata(), ApiConfig.BaytJsonSerializerOptions);

	Docker.DockerContainer targetContainer;
	try
	{
		targetContainer = Docker.DockerContainers.Containers.First(container => container.Id.StartsWith(containerId));
	}
	catch (InvalidOperationException)
	{
		return Results.NotFound($"Container with ID '{containerId}' was not found.");
	}
	if (targetContainer.ComposePath is null)
		return Results.NotFound($"Container with ID '{containerId}' does not have a compose file.");
	if (targetContainer.IsManaged) return Results.StatusCode(StatusCodes.Status304NotModified);

	var composeDir = Path.GetDirectoryName(targetContainer.ComposePath) ?? "/";
	File.WriteAllText(Path.Combine(composeDir, ".BaytMetadata.json"), defaultSidecarContents);

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status500InternalServerError)
	.WithSummary("Mark a Docker container as managed by Bayt.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("OwnDockerContainer");

app.MapDelete($"{baseDockerUrl}/containers/disown", async (string? containerId) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId, true, true);
	if (requestValidation is not null) return requestValidation;

	Docker.DockerContainer targetContainer;
	try
	{
		targetContainer = Docker.DockerContainers.Containers.First(container => container.Id.StartsWith(containerId));
	}
	catch (InvalidOperationException)
	{
		return Results.NotFound($"Container with ID '{containerId}' was not found.");
	}
	if (targetContainer.ComposePath is null)
		return Results.NotFound($"Container with ID '{containerId}' does not have a compose file.");
	if (!targetContainer.IsManaged) return Results.StatusCode(StatusCodes.Status304NotModified);

	var composeDir = Path.GetDirectoryName(targetContainer.ComposePath) ?? "/";
	File.Delete(Path.Combine(composeDir, ".BaytMetadata.json"));

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status500InternalServerError)
	.WithSummary("Mark a Docker container as unmanaged by Bayt.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("DisownDockerContainer");

app.MapDelete($"{baseDockerUrl}/containers/prune", async () =>
{
	if (!Docker.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");

	var dockerRequest = await Docker.SendRequest("containers/prune", "POST");

	return dockerRequest.Status switch
	{
		HttpStatusCode.OK => Results.Text(dockerRequest.Body, "application/json", Encoding.UTF8, StatusCodes.Status200OK),
		HttpStatusCode.InternalServerError => Results.InternalServerError(
			$"Docker returned an error while pruning containers. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		_ => Results.InternalServerError(
			$"Docker returned an unknown error while pruning containers. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
	};
}).Produces(StatusCodes.Status200OK)
	.Produces(StatusCodes.Status500InternalServerError)
	.WithSummary("Prune all stopped Docker containers.")
	.WithDescription("This will delete all stopped Docker containers.")
	.WithTags("Docker")
	.WithName("PruneDockerContainers");

app.MapGet($"{baseDockerUrl}/containers/getStats", async (string? containerId) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId);
	if (requestValidation is not null) return requestValidation;

	Docker.DockerContainer targetContainer;
	try
	{
		targetContainer = Docker.DockerContainers.Containers.First(container => container.Id.StartsWith(containerId));
	}
	catch (InvalidOperationException)
	{
		return Results.NotFound($"Container with ID '{containerId}' was not found.");
	}

	return targetContainer.Stats is not null ? Results.Json(targetContainer.Stats.ToDictionary())
		: Results.BadRequest($"This container is not running. ({targetContainer.State})");
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status200OK)
	.WithSummary("Fetch the usage stats of a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("GetDockerContainerStats");

app.MapPost($"{baseDockerUrl}/containers/create", async (HttpContext context, string? containerName, bool startContainer = true, bool deleteIfFailed = true) =>
{
	if (!Docker.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
	if (!Docker.IsDockerComposeAvailable) return Results.InternalServerError("Docker-Compose is not available on this system or the Docker integration was disabled.");

	var containerNameSlug = ParsingMethods.ConvertTextToSlug(containerName);
	if (string.IsNullOrWhiteSpace(containerNameSlug)) return Results.BadRequest($"{nameof(containerName)} is required and must contain at least one ASCII character.");
	if (!context.Request.Headers.ContentEncoding.Contains("chunked") && context.Request.ContentLength is null or 0)
	{
		return Results.StatusCode(StatusCodes.Status411LengthRequired);
	}

	var defaultMetadata = Docker.DockerContainers.GetDefaultMetadata(containerName);
	var defaultComposeSidecarContent = JsonSerializer.Serialize(defaultMetadata, ApiConfig.BaytJsonSerializerOptions);

	var containerExists = Directory.EnumerateDirectories(ApiConfig.ApiConfiguration.PathToComposeFolder).Any(directory => Path.GetFileNameWithoutExtension(directory) == containerNameSlug);
	if (containerExists) return Results.Conflict($"A container with the name '{containerNameSlug}' already exists.");

	var composePath = Path.Combine(ApiConfig.ApiConfiguration.PathToComposeFolder, containerNameSlug);
	Directory.CreateDirectory(composePath);

	await File.WriteAllTextAsync(Path.Combine(composePath, ".BaytMetadata.json"), defaultComposeSidecarContent);
	string yamlFilePath = Path.Combine(composePath, "docker-compose.yml");
	await using (var fileStream = new FileStream(yamlFilePath, FileMode.Create, FileAccess.Write,
		             FileShare.None))
	{
		await context.Request.Body.CopyToAsync(fileStream);
	}

	if (!startContainer) return Results.NoContent();

	var composeShell = await ShellMethods.RunShell("docker-compose", ["-f", yamlFilePath, "up", "-d"]);

	if (!composeShell.IsSuccess && deleteIfFailed)
	{
		// In case the docker-compose file left any services running
		await ShellMethods.RunShell("docker-compose", ["-f", yamlFilePath, "down"]);
		await ShellMethods.RunShell("docker-compose", ["-f", yamlFilePath, "rm"]);

		Directory.Delete(composePath, true);
		return Results.InternalServerError($"Non-zero exit code from starting the container. Container was deleted. " +
		                                   $"Stdout: {composeShell.StandardOutput} " +
		                                   $"Stderr: {composeShell.StandardError}");
	}
	if (!composeShell.IsSuccess)
	{
		return Results.InternalServerError($"Non-zero exit code from starting the container. " +
		                                   $"Stdout: {composeShell.StandardOutput} " +
		                                   $"Stderr: {composeShell.StandardError}");
	}

	return Results.Text(yamlFilePath, "plain/text");
}).Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status409Conflict)
	.Produces(StatusCodes.Status411LengthRequired)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Create and optionally start a new Docker container from a compose file.")
	.WithDescription("containerName is required and must contain at least one ASCII character. " +
	                 "deleteIfFailed defaults to true and deletes the compose directory in case a non-zero exit code was reported by docker-compose. " +
	                 "startContainer defaults to true The compose file is expected to be in the body of the request.")
	.WithTags("Docker")
	.WithName("CreateDockerContainer");

app.MapPost($"{baseDockerUrl}/containers/setMetadata", async (string? containerId, [FromBody] Dictionary<string, string?> metadata) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(containerId, true, true);
	if (requestValidation is not null) return requestValidation;

	metadata = metadata.Where(pair => Docker.DockerContainer.SupportedLabels.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
	if (metadata.Count == 0) return Results.BadRequest("No valid properties were provided. Please include one of: PrettyName, Notes, PreferredIconUrl, or WebpageLink");

	var targetContainer = Docker.DockerContainers.Containers.First(container => container.Id.StartsWith(containerId));
	if (!targetContainer.IsManaged) return Results.BadRequest("This container is not managed by Bayt.");

	bool changesMade = await targetContainer.SetContainerMetadata(metadata);

	return changesMade ? Results.NoContent() : Results.StatusCode(StatusCodes.Status304NotModified);

}).Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Set the metadata of a Docker container, such as PrettyName, Notes, PreferredIconUrl, or WebpageLink.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID. A dictionary<string, string> object is expected in the body in the format: ({ 'metadataKey': 'metadataValue' })")
	.WithTags("Docker")
	.WithName("SetDockerContainerMetadata");



// Docker images endpoints

app.MapGet($"{baseDockerUrl}/images/getList", async () =>
{
	if (!Docker.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
	await Docker.ImagesInfo.UpdateDataIfNecessary();

	return Results.Json(Docker.ImagesInfo.ToDictionary());
}).Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status200OK)
	.WithSummary("Get list of Docker images on the system.")
	.WithTags("Docker", "Docker Images")
	.WithName("GetDockerImages");

app.MapDelete($"{baseDockerUrl}/images/delete", async (string? imageId, bool? force = false) =>
{
	var requestValidation = await RequestChecking.ValidateDockerRequest(imageId, false, updateImages:true);
	if (requestValidation is not null) return requestValidation;
	if (!imageId.StartsWith("sha256:")) imageId = "sha256:" + imageId;

	var targetImage = Docker.ImagesInfo.Images.Find(image => image.Id.StartsWith(imageId));

	return targetImage is null ? Results.NotFound($"Image with ID '{imageId}' was not found.")
		: await targetImage.Delete(force.GetValueOrDefault(false));
}).Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status409Conflict)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status200OK)
	.WithSummary("Delete a Docker image.")
	.WithDescription("imageId must contain at least the first 12 characters of the image's ID. force removes the image even if it is being used by stopped containers or has other tags, defaults to false.")
	.WithTags("Docker", "Docker Images")
	.WithName("DeleteDockerImage");



if (Docker.IsDockerAvailable)
{
	await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Info, "Docker", "Docker is available. Docker endpoints will be available."));

	if (Docker.IsDockerComposeAvailable)
	{
		await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Info, "Docker", "Docker-Compose is available. Docker-Compose endpoints will be available."));
	}
}
if (Environment.GetEnvironmentVariable("BAYT_SKIP_FIRST_FETCH") == "1")
{
	await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Info, "Initialization", "Skipping first fetch cycle. This may cause the first request to be slow."));
}
else
{
	// Do a fetch cycle to let the constructors run.
	List<Task> fetchTasks = [
		Task.Run(StatsApi.CpuData.UpdateDataIfNecessary),
		Task.Run(GpuHandling.FullGpusData.UpdateDataIfNecessary),
		Task.Run(StatsApi.MemoryData.UpdateDataIfNecessary),
		Task.Run(DiskHandling.FullDisksData.UpdateDataIfNecessary)
	];

	if (Docker.IsDockerAvailable)
	{
		fetchTasks.Add(Task.Run(Docker.DockerContainers.UpdateDataIfNecessary));
		fetchTasks.Add(Task.Run(Docker.ImagesInfo.UpdateDataIfNecessary));
	}

	await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Info, "Initialization", "Running an initial fetch cycle..."));
	await Task.WhenAll(fetchTasks);

	await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Ok, "Initialization", "Fetch cycle complete."));
}
await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Ok, "Initialization", "Bayt API is ready."));

try
{
	app.Run();
}
catch (SocketException e) when (e.Message == "Cannot assign requested address")
{
	await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Fatal, "Network Initalization",
		"Something went wrong while binding to one of the targetted IP addresses. Make sure the targetted IP address is valid."));
}
catch (SocketException e) when (e.Message == "Permission denied")
{
	await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Fatal, "Network Initalization",
		"The current user does not have permission to bind to one of the IP addresses or ports."));
}
catch (IOException e) when (e.InnerException is not null && e.InnerException.Message == "Address already in use")
{
	await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Fatal, "Network Initalization",
		$"Port {ApiConfig.NetworkPort} is already in use. Another instance of Bayt may be running."));
}
finally
{
	await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Info, "Bayt", "Bayt is shutting down."));
}