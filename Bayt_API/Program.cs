using System.Net;
using System.Net.Sockets;
using System.Text;
using Bayt_API;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

Console.WriteLine($"[INFO] Adding URL '{IPAddress.Loopback}:{ApiConfig.NetworkPort}' to listen list");
builder.WebHost.ConfigureKestrel(opts => opts.Listen(IPAddress.Loopback, ApiConfig.NetworkPort));

if (Environment.GetEnvironmentVariable("BAYT_LOCALHOST_ONLY") != "1")
{
	var localIp = StatsApi.GetLocalIpAddress();

	Console.WriteLine($"[INFO] Adding URL '{localIp}:{ApiConfig.NetworkPort}' to listen list");
	builder.WebHost.ConfigureKestrel(opts => opts.Listen(localIp, ApiConfig.NetworkPort));
}

if (Environment.GetEnvironmentVariable("BAYT_DISABLE_SOCK") != "1")
{
	if (File.Exists(ApiConfig.UnixSocketPath)) File.Delete(ApiConfig.UnixSocketPath);
	Console.WriteLine($"[INFO] Adding URL 'unix:{ApiConfig.UnixSocketPath}' to listen list");
	builder.WebHost.ConfigureKestrel(opts => opts.ListenUnixSocket(ApiConfig.UnixSocketPath));
}

Console.ForegroundColor = ConsoleColor.Gray;
Console.WriteLine($"""
                   [INFO] Loaded configuration from: '{ApiConfig.ConfigFilePath}'
                   [INFO] Loaded clientData folder: '{ApiConfig.ApiConfiguration.PathToDataFolder}'
                   [INFO] Loaded containers folder: '{ApiConfig.ApiConfiguration.PathToComposeFolder}'
                   """);
Console.ResetColor();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();

if (Environment.OSVersion.Platform != PlatformID.Unix)
{
	Console.ForegroundColor = ConsoleColor.Yellow;
	Console.WriteLine($"[WARNING] Detected OS is '{Environment.OSVersion.Platform}', which doesn't appear to be Unix-like.\n" +
	                  "Here be dragons, as this implementation is only targeted and supported for Unix-like systems.");
	Console.ResetColor();
}



app.MapGet($"{ApiConfig.BaseApiUrlPath}/getStats", async (bool? meta, bool? system, bool? cpu, bool? gpu, bool? memory, bool? mounts) =>
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

		// Request checks done

		Dictionary<string, Dictionary<string, dynamic>[]> responseDictionary = [];

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
				default:
				{
					throw new ArgumentOutOfRangeException(stat.ToString());
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
						new Dictionary<string, dynamic>
						{
							{ nameof(ApiConfig.Version), ApiConfig.Version },
							{ nameof(ApiConfig.ApiVersion), ApiConfig.ApiVersion },
							{ nameof(ApiConfig.ApiConfiguration.SecondsToUpdate),
								ApiConfig.ApiConfiguration.SecondsToUpdate },
							{ nameof(Docker.IsDockerAvailable), Docker.IsDockerAvailable }
						}
					]);
					break;
				}

				case ApiConfig.SystemStats.System:
				{
					responseDictionary.Add("System", [ StatsApi.GeneralSpecs.ToDictionary() ]);
					break;
				}

				case ApiConfig.SystemStats.Cpu:
				{
					responseDictionary.Add("CPU", [ StatsApi.CpuData.ToDictionary()! ]);
					break;
				}

				case ApiConfig.SystemStats.Gpu:
				{
					responseDictionary.Add("GPU", GpuHandling.FullGpusData.ToDictionary()!);
					break;
				}

				case ApiConfig.SystemStats.Memory:
				{
					responseDictionary.Add("Memory", [ StatsApi.MemoryData.ToDictionary() ]);
					break;
				}

				case ApiConfig.SystemStats.Mounts:
				{
					responseDictionary.Add("Mounts", DiskHandling.FullDisksData.ToDictionary()!);
					break;
				}
				default:
				{
					throw new ArgumentOutOfRangeException(requestedStat.ToString());
				}
			}
		}
		return Results.Json(responseDictionary);
	}).Produces(StatusCodes.Status200OK)
	.Produces(StatusCodes.Status400BadRequest)
	.WithSummary("Returns the stats/metrics of the server according to what was requested. Defaults to all in case none were specified.")
	.WithTags("Stats")
	.WithName("GetSystemMetrics");



// API Config management endpoints

app.MapPost($"{ApiConfig.BaseApiUrlPath}/editConfig", async (HttpContext context) =>
{
	// TODO: Implement Auth and Rate Limiting before blindly trusting the request.

	Dictionary<string, dynamic> newConfigs;
	try
	{
		newConfigs = await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, dynamic>>(context) ?? [];
	}
	catch (BadHttpRequestException e)
	{
		return Results.BadRequest(e.Message);
	}

	ApiConfig.ApiConfiguration.EditConfig(newConfigs);

	return Results.NoContent();

}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.WithSummary("Change one or a few configs of the API. Follows the names and types of the ApiConfiguration.json file")
	.WithDescription("The property cannot be 'WatchedMounts', nor 'WolClients'. Those two have their own endpoints. " +
	                 "Format: { '${PropertyName}': '${PropertyValue}' }. Expected to be in the body of the request.")
	.WithTags("Configuration")
	.WithName("EditConfig");


app.MapGet($"{ApiConfig.BaseApiUrlPath}/getApiConfigs", () =>
		Results.Json(ApiConfig.ApiConfiguration.ToDictionary()))
	.Produces(StatusCodes.Status200OK)
	.WithSummary("Fetch the API's live configs in the form of JSON.")
	.WithTags("Configuration")
	.WithName("GetActiveApiConfigs");


app.MapPost($"{ApiConfig.BaseApiUrlPath}/updateLiveConfigs", () =>
{
	ApiConfig.ApiConfiguration.LoadConfig();

	return Results.NoContent();
}).Produces(StatusCodes.Status200OK)
	.WithSummary("Refresh and sync the API's live configs with the file on-disk.")
	.WithTags("Configuration")
	.WithName("UpdateLiveConfigs");



// Mount management endpoints

app.MapGet($"{ApiConfig.BaseApiUrlPath}/getMountsList", () =>
		Results.Json(ApiConfig.ApiConfiguration.WatchedMounts))
	.Produces(StatusCodes.Status200OK)
	.WithSummary("Fetch the list of the currently watched mounts.")
	.WithTags("Mounts")
	.WithName("GetMountsList");


app.MapPost($"{ApiConfig.BaseApiUrlPath}/addMounts", async (HttpContext context) =>
{
	Dictionary<string, string?> mountPoints;
	try
	{
		mountPoints = await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, string?>>(context) ?? [];
	}
	catch (BadHttpRequestException e)
	{
		return Results.BadRequest(e.Message);
	}

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


app.MapDelete($"{ApiConfig.BaseApiUrlPath}/removeMounts", async (HttpContext context) =>
{
	List<string> mountPoints;
	try
	{
		mountPoints = (await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, List<string>>>(context) ?? new() {{"Mounts", []}})["Mounts"];
	}
	catch (BadHttpRequestException e)
	{
		return Results.BadRequest(e.Message);
	}

	if (mountPoints.Count == 0)
	{
		return Results.BadRequest("List must contain more than 0 elements.");
	}

	ApiConfig.ApiConfiguration.RemoveMountpoint(mountPoints);

	return Results.NoContent();
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Remove one or more mounts from the list of watched mounts.")
	.WithDescription("Format: { 'Mounts': ['${Mountpoint1}', '${Mountpoint2}', '...'] }. Expected to be in the body of the request.")
	.WithTags("Mounts")
	.WithName("RemoveMounts");



// WoL endpoints

app.MapPost($"{ApiConfig.BaseApiUrlPath}/AddWolClient", async (HttpContext context) =>
{
	Dictionary<string, string> clientsRaw;
	try
	{
		clientsRaw = await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, string>>(context) ?? [];
	}
	catch (BadHttpRequestException e)
	{
		return Results.BadRequest(e.Message);
	}

	Dictionary<string, string> clients = [];
	if (clientsRaw.Count == 0)
	{
		return Results.BadRequest("List must contain more than 0 elements.");
	}

	foreach (var clientKvp in clientsRaw.Where(clientKvp => IPAddress.TryParse(clientKvp.Key, out _) && clientKvp.Value != ""))
	{
		clients.TryAdd(clientKvp.Key, clientKvp.Value);
	}

	if (clients.Count == 0)
	{
		return Results.BadRequest("List evaluated down to 0 valid elements.");
	}

	ApiConfig.ApiConfiguration.AddWolClient(clients);

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.WithSummary("Save one or more WoL clients to this Bayt instance. Both fields are required, and cannot be empty.")
	.WithDescription("Format: { '${IPv4Address}': '${Label}' }. Expected to be in the body of the request.")
	.WithTags("Wake-on-LAN")
	.WithName("AddWolClients");

app.MapDelete($"{ApiConfig.BaseApiUrlPath}/RemoveWolClients", async (HttpContext context) =>
{
	List<string> ipAddrs;
	try
	{
		ipAddrs = (await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, List<string>>>(context) ?? new() {{"IPs", []}})["IPs"];
	}
	catch (BadHttpRequestException e)
	{
		return Results.BadRequest(e.Message);
	}
	if (ipAddrs.Count == 0)
	{
		return Results.BadRequest("IP address list must contain at least 1 element.");
	}

	ApiConfig.ApiConfiguration.RemoveWolClient(ipAddrs);

	return Results.NoContent();
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Remove one or more saved WoL clients from this Bayt instance.")
	.WithDescription("Format: { 'IPs': ['${IPv4Address1}', '${IPv4Address2}', '...'] }. Expected to be in the body of the request.")
	.WithTags("Wake-on-LAN")
	.WithName("RemoveWolClients");

app.MapGet($"{ApiConfig.BaseApiUrlPath}/GetWolClients", () =>
		Results.Json(ApiConfig.ApiConfiguration.WolClients))
	.Produces(StatusCodes.Status200OK)
	.WithSummary("Fetch the list of the currently saved WoL clients.")
	.WithTags("Wake-on-LAN")
	.WithName("GetWolClients");

app.MapPost($"{ApiConfig.BaseApiUrlPath}/WakeWolClient", (string? ipAddress) =>
{
	if (ipAddress is null || !IPAddress.TryParse(ipAddress, out _))
	{
		return Results.BadRequest("ipAddress must be a valid IPv4 address.");
	}
	var clientToWake =
		ApiConfig.ApiConfiguration.WolClientsClass!.Find(client =>
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

app.MapGet($"{ApiConfig.BaseApiUrlPath}/getData", async (string? folderName, string? fileName) =>
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

app.MapPut($"{ApiConfig.BaseApiUrlPath}/setData", async (HttpContext context, string? folderName, string? fileName) =>
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
	.WithDescription("Both parameters are required and must be valid, non-empty file/folder names.")
	.WithTags("clientData")
	.WithName("SetClientData");

app.MapDelete($"{ApiConfig.BaseApiUrlPath}/deleteData", (string? folderName, string? fileName) =>
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

app.MapDelete($"{ApiConfig.BaseApiUrlPath}/deletefolder", (string? folderName) =>
{
	if (folderName is null) return Results.BadRequest("Folder name must be specified.");

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
app.MapPost($"{ApiConfig.BaseApiUrlPath}/shutdownServer", () =>
{
	Console.WriteLine("[INFO] Recieved poweroff request, attempting to shut down...");
	var operationShell = ShellMethods.RunShell("sudo", "-n /sbin/poweroff");

	if (operationShell.Success) return Results.NoContent();

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

app.MapPost($"{ApiConfig.BaseApiUrlPath}/restartServer", () =>
{
	Console.WriteLine("[INFO] Recieved restart request, attempting to restart...");
	var operationShell = ShellMethods.RunShell("sudo", "-n /sbin/reboot");

	if (operationShell.Success) return Results.NoContent();

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

app.MapGet($"{baseDockerUrl}/getContainers", async (bool all = true) =>
{
	if (!Docker.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
	await Docker.DockerContainers.UpdateDataIfNecessary();


	return Results.Json(Docker.DockerContainers.ToDictionary(all));

}).Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status200OK)
	.WithSummary("Fetch all or only the currently active containers in the system.")
	.WithTags("Docker")
	.WithName("GetDockerContainers");

app.MapPost($"{baseDockerUrl}/startContainer", async (string? containerId) =>
{
	try
	{
		await RequestChecking.ValidateDockerRequest(containerId, false);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException e)
	{
		return Results.InternalServerError(e.Message);
	}

	var dockerRequest = await Docker.SendRequest($"containers/{containerId}/start", "POST");

	return dockerRequest.Status switch
	{
		HttpStatusCode.NoContent => Results.NoContent(),
		HttpStatusCode.NotModified => Results.StatusCode(StatusCodes.Status304NotModified),
		HttpStatusCode.NotFound => Results.NotFound($"Container with ID '{containerId}' was not found."),
		HttpStatusCode.InternalServerError => Results.InternalServerError(
			$"Docker returned an error while starting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		_ => Results.InternalServerError(
			$"Docker returned an unknown error while starting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
	};
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to start a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("StartDockerContainer");

app.MapPost($"{baseDockerUrl}/stopContainer", async (string? containerId) =>
{
	try
	{
		await RequestChecking.ValidateDockerRequest(containerId, false);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException e)
	{
		return Results.InternalServerError(e.Message);
	}

	var dockerRequest = await Docker.SendRequest($"containers/{containerId}/stop", "POST");

	return dockerRequest.Status switch
	{
		HttpStatusCode.NoContent => Results.NoContent(),
		HttpStatusCode.NotModified => Results.StatusCode(StatusCodes.Status304NotModified),
		HttpStatusCode.NotFound => Results.NotFound($"Container with ID '{containerId}' was not found."),
		HttpStatusCode.InternalServerError => Results.InternalServerError(
			$"Docker returned an error while stopping container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		_ => Results.InternalServerError(
			$"Docker returned an unknown error while stopping container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
	};
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to stop a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("StopDockerContainer");

app.MapPost($"{baseDockerUrl}/restartContainer", async (string? containerId) =>
{
	try
	{
		await RequestChecking.ValidateDockerRequest(containerId, false);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException e)
	{
		return Results.InternalServerError(e.Message);
	}

	var dockerRequest = await Docker.SendRequest($"containers/{containerId}/restart", "POST");

	return dockerRequest.Status switch
	{
		HttpStatusCode.NoContent => Results.NoContent(),
		HttpStatusCode.NotFound => Results.NotFound($"Container with ID '{containerId}' was not found."),
		HttpStatusCode.InternalServerError => Results.InternalServerError(
			$"Docker returned an error while restarting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		_ => Results.InternalServerError(
			$"Docker returned an unknown error while restarting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
	};
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to restart a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("RestartDockerContainer");

app.MapPost($"{baseDockerUrl}/killContainer", async (string? containerId) =>
{
	try
	{
		await RequestChecking.ValidateDockerRequest(containerId, false);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException e)
	{
		return Results.InternalServerError(e.Message);
	}

	var dockerRequest = await Docker.SendRequest($"containers/{containerId}/kill", "POST");

	return dockerRequest.Status switch
	{
		HttpStatusCode.NoContent => Results.NoContent(),
		HttpStatusCode.NotFound => Results.NotFound($"Container with ID '{containerId}' was not found."),
		HttpStatusCode.Conflict => Results.Conflict($"Container with ID '{containerId}' was not running."),
		HttpStatusCode.InternalServerError => Results.InternalServerError(
			$"Docker returned an error while killing container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		_ => Results.InternalServerError(
			$"Docker returned an unknown error while killing container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
	};
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status409Conflict)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to kill a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("KillDockerContainer");

app.MapDelete($"{baseDockerUrl}/deleteContainer", async (string? containerId) =>
{
	try
	{
		await RequestChecking.ValidateDockerRequest(containerId, false);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException e)
	{
		return Results.InternalServerError(e.Message);
	}

	var dockerRequest = await Docker.SendRequest($"containers/{containerId}", "DELETE");

	return dockerRequest.Status switch
	{
		HttpStatusCode.NoContent => Results.NoContent(),
		HttpStatusCode.BadRequest => Results.BadRequest($"Docker returned a bad parameter error while deleting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		HttpStatusCode.NotFound => Results.NotFound($"Container with ID '{containerId}' was not found."),
		HttpStatusCode.Conflict => Results.Conflict($"There was a conflict deleting container with ID '{containerId}'. Make sure it's off."),
		HttpStatusCode.InternalServerError => Results.InternalServerError(
			$"Docker returned an error while killing container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		_ => Results.InternalServerError(
			$"Docker returned an unknown error while killing container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
	};
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status409Conflict)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Issues a command to delete a specific Docker container. Does not include the compose file, if applicable.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("DeleteDockerContainer");

app.MapGet($"{baseDockerUrl}/getContainerLogs", Docker.StreamDockerLogs)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.WithSummary("Stream the Docker container's live logs.")
	.WithDescription("containerId is required. stdout, stderr, and timestamps are optional to specify which streams to follow, and whether to prefix each line with a timestamp. Will default to stdout=true, stderr=true, and timestamps=false if not specified.")
	.WithTags("Docker")
	.WithName("GetDockerContainerLogs");

// Docker Compose endpoints

app.MapGet($"{baseDockerUrl}/getContainerCompose", async (string? containerId) =>
{
	try
	{
		await RequestChecking.ValidateDockerRequest(containerId);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException e)
	{
		return Results.InternalServerError(e.Message);
	}

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

app.MapPut($"{baseDockerUrl}/setContainerCompose", async (HttpContext context, string? containerId, bool restartContainer = false) =>
{
	// Request validation
	try
	{
		await RequestChecking.ValidateDockerRequest(containerId);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException e)
	{
		return Results.InternalServerError(e.Message);
	}

	if (!context.Request.Headers.ContentEncoding.Contains("chunked") && context.Request.ContentLength is null or 0)
	{
		return Results.StatusCode(StatusCodes.Status411LengthRequired);
	}

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

app.MapPost($"{baseDockerUrl}/ownContainer", async (string? containerId) =>
	{
		const string defaultFlagContents = """
		                                   This file indicates that this container is managed by Bayt. You are free to edit or delete it.
		                                   This header is used by Bayt to determine a few details about the container. It's okay for some to be empty.

		                                   Pretty name: ""
		                                   Notes: ""
		                                   Icon URL: ""
		                                   Preferred Service URL: ""
		                                   """;

	try
	{
		await RequestChecking.ValidateDockerRequest(containerId);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException e)
	{
		return Results.InternalServerError(e.Message);
	}

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
	File.WriteAllText(Path.Combine(composeDir, ".BaytMetadata"), defaultFlagContents);

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

app.MapDelete($"{baseDockerUrl}/disownContainer", async (string? containerId) =>
{
	try
	{
		await RequestChecking.ValidateDockerRequest(containerId);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException e)
	{
		return Results.InternalServerError(e.Message);
	}

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
	File.Delete(Path.Combine(composeDir, ".BaytMetadata"));

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

app.MapDelete($"{baseDockerUrl}/pruneContainers", async () =>
{
	if (!Docker.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");

	var dockerRequest = await Docker.SendRequest("containers/prune", "POST");

	Console.WriteLine(dockerRequest.Body);

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

app.MapGet($"{baseDockerUrl}/getContainerStats", async (string? containerId) =>
{
	try
	{
		await RequestChecking.ValidateDockerRequest(containerId);
	}
	catch (ArgumentException e)
	{
		return Results.BadRequest(e.Message);
	}
	catch (FileNotFoundException e)
	{
		return Results.InternalServerError(e.Message);
	}

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

	return Results.Json(targetContainer.Stats.ToDictionary());
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status200OK)
	.WithSummary("Fetch the usage stats of a specific Docker container.")
	.WithDescription("containerId must contain at least the first 12 characters of the container's ID.")
	.WithTags("Docker")
	.WithName("GetDockerContainerStats");

app.MapPost($"{baseDockerUrl}/createContainer", async (HttpContext context, string? containerName, bool startContainer = false) =>
{
	if (!Docker.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system or the integration was disabled.");
	var containerNameSlug = ParsingMethods.ConvertTextToSlug(containerName);
	if (string.IsNullOrWhiteSpace(containerNameSlug)) return Results.BadRequest($"{nameof(containerName)} is required and must contain at least one ASCII character.");
	if (!context.Request.Headers.ContentEncoding.Contains("chunked") && context.Request.ContentLength is null or 0)
	{
		return Results.StatusCode(StatusCodes.Status411LengthRequired);
	}

	var defaultComposeSidecarContent = $"""
	                            This file indicates that this container is managed by Bayt. You are free to edit or delete it.
	                            This header is used by Bayt to determine a few details about the container. It's okay for some to be empty.

	                            Pretty name: "{containerName}"
	                            Notes: ""
	                            Icon URL: ""
	                            Preferred Service URL: ""
	                            """;

	var containerExists = Directory.EnumerateDirectories(ApiConfig.ApiConfiguration.PathToComposeFolder).Any(directory => Path.GetFileNameWithoutExtension(directory) == containerNameSlug);
	if (containerExists) return Results.Conflict($"A container with the name '{containerNameSlug}' already exists.");

	var composePath = Path.Combine(ApiConfig.ApiConfiguration.PathToComposeFolder, containerNameSlug);
	Directory.CreateDirectory(composePath);

	await File.WriteAllTextAsync(Path.Combine(composePath, ".BaytMetadata"), defaultComposeSidecarContent);
	await using (var fileStream = new FileStream(Path.Combine(composePath, "docker-compose.yml"), FileMode.Create, FileAccess.Write,
		             FileShare.None))
	{
		await context.Request.Body.CopyToAsync(fileStream);
	}

	if (!startContainer) return Results.NoContent();


	var composeShell = ShellMethods.RunShell("docker-compose", $"-f {composePath}/docker-compose.yml up -d");

	return composeShell.Success ? Results.NoContent() : Results.InternalServerError($"Non-zero exit code from starting the container. " +
		$"Stdout: {composeShell.StandardOutput} " +
		$"Stderr: {composeShell.StandardError}");
}).Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status409Conflict)
	.Produces(StatusCodes.Status411LengthRequired)
	.Produces(StatusCodes.Status204NoContent)
	.WithSummary("Create a new Docker container from a compose file. Optionally also start it.")
	.WithDescription("containerName is required and must contain at least one ASCII character. startContainer defaults to false. The compose file is expected to be in the body of the request.")
	.WithTags("Docker")
	.WithName("CreateDockerContainer");

if (Environment.GetEnvironmentVariable("BAYT_SKIP_FIRST_FETCH") == "1")
{
	if (Docker.IsDockerAvailable) Console.WriteLine("[INFO] Docker is available. Docker endpoints will be available.");
	Console.WriteLine("[INFO] Skipping first fetch cycle. This may cause the first request to be slow.");
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
		Console.WriteLine("[INFO] Docker is available. Docker endpoints will be available.");
		fetchTasks.Add(Task.Run(Docker.DockerContainers.UpdateDataIfNecessary));
	}

	Console.WriteLine("[INFO] Preparing a few things...");
	await Task.WhenAll(fetchTasks);

	Console.ForegroundColor = ConsoleColor.Green;
	Console.WriteLine("[OK] Fetch cycle complete. Starting API...");
	Console.ResetColor();
}

try
{
	app.Run();
}
catch (SocketException e) when (e.Message == "Cannot assign requested address")
{
	Console.ForegroundColor = ConsoleColor.Red;
	Console.WriteLine(
		"[FATAL] Something went wrong while binding to one of the targetted IP addresses. " +
		"Make sure the targetted IP address is valid.");
	Console.ResetColor();
}
catch (SocketException e) when (e.Message == "Permission denied")
{
	Console.ForegroundColor = ConsoleColor.Red;
	Console.WriteLine("[FATAL] The current user does not have permission to bind to one of the IP addresses or ports.");
	Console.ResetColor();
}
catch (IOException e) when (e.InnerException is not null && e.InnerException.Message == "Address already in use")
{
	Console.ForegroundColor = ConsoleColor.Red;
	Console.WriteLine(
		$"[FATAL] Port {ApiConfig.NetworkPort} is already in use. Another instance of Bayt may be running.");
	Console.ResetColor();
}