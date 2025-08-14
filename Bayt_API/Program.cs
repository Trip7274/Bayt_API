using System.Diagnostics;
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

if (Environment.GetEnvironmentVariable("BAYT_USE_SOCK") == "1")
{
	Console.WriteLine($"[INFO] Adding URL 'unix://{ApiConfig.UnixSocketPath}' to listen list");
	builder.WebHost.ConfigureKestrel(opts => opts.ListenUnixSocket(ApiConfig.UnixSocketPath));
}


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
		Dictionary<string, bool?> requestedStatsRaw = new() {
			{ "Meta", meta },
			{ "System", system },
			{ "CPU", cpu },
			{ "GPU", gpu },
			{ "Memory", memory },
			{ "Mounts", mounts }
		};
		List<string> requestedStats = [];
		if (requestedStatsRaw.All(stat => !stat.Value.HasValue))
		{
			requestedStats = ApiConfig.PossibleStats.ToList();
		}
		else
		{
			requestedStats.AddRange(from statKvp in requestedStatsRaw where statKvp.Value.HasValue && statKvp.Value.Value select statKvp.Key);
		}

		if (requestedStats.Count == 0)
		{
			return Results.BadRequest("No stats were requested.");
		}

		// Request checks done

		Debug.WriteLine($"Got a request asking for: {string.Join(", ", requestedStats)}");

		Dictionary<string, Dictionary<string, dynamic>[]> responseDictionary = [];

		// Queue and update all the requested stats up asynchronously
		List<Task> fetchTasks = [];
		foreach (var stat in requestedStats)
		{
			switch (stat)
			{
				case "CPU":
				{
					fetchTasks.Add(Task.Run(StatsApi.CpuData.UpdateDataIfNecessary));
					break;
				}

				case "GPU":
				{
					fetchTasks.Add(Task.Run(GpuHandling.FullGpusData.UpdateDataIfNecessary));
					break;
				}

				case "Memory":
				{
					fetchTasks.Add(Task.Run(StatsApi.MemoryData.UpdateDataIfNecessary));
					break;
				}

				case "Mounts":
				{
					fetchTasks.Add(Task.Run(DiskHandling.FullDisksData.UpdateDataIfNecessary));
					break;
				}
			}
		}
		await Task.WhenAll(fetchTasks);

		foreach (var requestedStat in requestedStats)
		{
			switch (requestedStat)
			{
				case "Meta":
				{
					responseDictionary.Add("Meta", [
						new Dictionary<string, dynamic>
						{
							{ nameof(ApiConfig.Version), ApiConfig.Version },
							{ nameof(ApiConfig.ApiVersion), ApiConfig.ApiVersion },
							{ nameof(ApiConfig.MainConfigs.ConfigProps.SecondsToUpdate),
								ApiConfig.MainConfigs.ConfigProps.SecondsToUpdate },
							{ nameof(Docker.IsDockerAvailable), Docker.IsDockerAvailable }
						}
					]);
					break;
				}

				case "System":
				{
					responseDictionary.Add("System", [ StatsApi.GeneralSpecs.ToDictionary() ]);
					break;
				}

				case "CPU":
				{
					responseDictionary.Add("CPU", [ StatsApi.CpuData.ToDictionary()! ]);
					break;
				}

				case "GPU":
				{
					responseDictionary.Add("GPU", GpuHandling.FullGpusData.ToDictionary()!);
					break;
				}

				case "Memory":
				{
					responseDictionary.Add("Memory", [ StatsApi.MemoryData.ToDictionary() ]);
					break;
				}

				case "Mounts":
				{
					responseDictionary.Add("Mounts", DiskHandling.FullDisksData.ToDictionary()!);
					break;
				}
			}
		}
		return Results.Json(responseDictionary);
	})
	.Produces(StatusCodes.Status200OK)
	.Produces(StatusCodes.Status400BadRequest)
	.WithName("GetStats");



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

	ApiConfig.MainConfigs.EditConfig(newConfigs);

	return Results.NoContent();

}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.WithName("EditConfig");


app.MapGet($"{ApiConfig.BaseApiUrlPath}/getApiConfigs", () =>
{
	return Results.Json(ApiConfig.MainConfigs.ConfigProps);
}).Produces(StatusCodes.Status200OK)
	.WithName("GetActiveApiConfigs");


app.MapPost($"{ApiConfig.BaseApiUrlPath}/updateLiveConfigs", () =>
{
	ApiConfig.MainConfigs.UpdateConfig();

	return Results.NoContent();

}).Produces(StatusCodes.Status200OK)
	.WithName("UpdateLiveConfigs");



// Mount management endpoints

app.MapGet($"{ApiConfig.BaseApiUrlPath}/getMountsList", () =>
{
	return Results.Json(ApiConfig.MainConfigs.ConfigProps.WatchedMounts);
}).Produces(StatusCodes.Status200OK)
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

	ApiConfig.MainConfigs.AddMountpoint(mountPoints);

	return Results.NoContent();
}).WithName("AddMounts").Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
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

	ApiConfig.MainConfigs.RemoveMountpoint(mountPoints);

	return Results.NoContent();
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status204NoContent)
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

	ApiConfig.MainConfigs.AddWolClient(clients);

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
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

	ApiConfig.MainConfigs.RemoveWolClient(ipAddrs);

	return Results.NoContent();
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status204NoContent)
	.WithName("RemoveWolClients");

app.MapGet($"{ApiConfig.BaseApiUrlPath}/GetWolClients", () =>
{
	return Results.Json(ApiConfig.MainConfigs.ConfigProps.WolClients);
}).Produces(StatusCodes.Status200OK)
	.WithName("GetWolClients");

app.MapPost($"{ApiConfig.BaseApiUrlPath}/WakeWolClient", (string ipAddress) =>
{
	if (ApiConfig.MainConfigs.ConfigProps.WolClientsClass is null)
	{
		ApiConfig.MainConfigs.UpdateConfig();
	}

	var clientToWake =
		ApiConfig.MainConfigs.ConfigProps.WolClientsClass!.Find(client =>
			client.IpAddress.ToString() == ipAddress);
	if (clientToWake is null)
	{
		return Results.BadRequest($"No WoL client with IP '{ipAddress}' was found.");
	}

	WolHandling.WakeClient(clientToWake);

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
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
	.WithName("GetClientData");

app.MapPut($"{ApiConfig.BaseApiUrlPath}/setData", async (HttpContext context, string? folderName, string? fileName) =>
{
	if (folderName is null || fileName is null)
	{
		return Results.BadRequest("Both the folder and file name must be specified.");
	}

	var memoryStream = new MemoryStream();
	await context.Request.Body.CopyToAsync(memoryStream);

	DataEndpointManagement.DataFileMetadata metadataObject = new(folderName, fileName, memoryStream.ToArray());
	await DataEndpointManagement.SetDataFile(metadataObject);

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
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
	catch (FileNotFoundException)
	{
		return Results.NotFound($"File '{fileName}' was not found.");
	}

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.WithName("DeleteClientData");

app.MapDelete($"{ApiConfig.BaseApiUrlPath}/deletefolder", (string? folderName) =>
{
	if (folderName is null) return Results.BadRequest("Folder name must be specified.");

	try
	{
		DataEndpointManagement.DeleteDataFolder(folderName);
	}
	catch (DirectoryNotFoundException)
	{
		return Results.NotFound($"Folder '{folderName}' was not found.");
	}

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.WithName("DeleteClientDataFolder");


// Power endpoint

app.MapPost($"{ApiConfig.BaseApiUrlPath}/powerOperation", async (HttpContext context) =>
{
	string operation;
	try
	{
		operation = (await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, string>>(context) ?? new Dictionary<string, string> {{"Operation", ""}})["Operation"];
	}
	catch (BadHttpRequestException e)
	{
		return Results.BadRequest(e.Message);
	}

	ShellResult? operationShell;
	switch (operation)
	{
		case "poweroff" or "shutdown": {
			Console.WriteLine("[INFO] Recieved poweroff request, attempting to shut down...");
			operationShell = ShellMethods.RunShell("sudo", "-n /sbin/poweroff");
			break;
		}
		case "reboot" or "restart":
		{
			Console.WriteLine("[INFO] Recieved reboot request, attempting to reboot...");
			operationShell = ShellMethods.RunShell("sudo", "-n /sbin/reboot");
			break;
		}

		default:
		{
			return Results.BadRequest("Operation must be either ('poweroff'|'shutdown') or ('reboot'|'restart').");
		}
	}

	if (!operationShell.Success)
	{
		Dictionary<string, string> errorMessage = new()
		{
			{"Message", "Seems like the power operation failed. Did you run SetupBayt.sh on this user?"},
			{"stdout", operationShell.StandardOutput},
			{"stderr", operationShell.StandardError}
		};
		return Results.InternalServerError(errorMessage);
	}

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.WithName("PowerOperation");

// Docker endpoints

string baseDockerUrl = $"{ApiConfig.BaseApiUrlPath}/docker";

app.MapGet($"{baseDockerUrl}/getActiveContainers", async () =>
{
	if (!Docker.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system.");
	await Docker.DockerContainers.UpdateDataIfNecessary();

	Dictionary<string, Dictionary<string, dynamic?>[]> containerDict = new()
	{
		{ "Containers", Docker.DockerContainers.ToDictionary() }
	};


	return Results.Json(containerDict);

}).WithName("GetDockerContainers");

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
		HttpStatusCode.NoContent or HttpStatusCode.NotModified => Results.NoContent(),
		HttpStatusCode.NotFound => Results.NotFound($"Container with ID '{containerId}' was not found."),
		HttpStatusCode.InternalServerError => Results.InternalServerError(
			$"Docker returned an error while starting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		_ => Results.InternalServerError(
			$"Docker returned an unknown error while starting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
	};
}).WithName("StartDockerContainer");

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
		HttpStatusCode.NoContent or HttpStatusCode.NotModified => Results.NoContent(),
		HttpStatusCode.NotFound => Results.NotFound($"Container with ID '{containerId}' was not found."),
		HttpStatusCode.InternalServerError => Results.InternalServerError(
			$"Docker returned an error while stopping container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		_ => Results.InternalServerError(
			$"Docker returned an unknown error while stopping container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
	};
}).WithName("StopDockerContainer");

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
}).WithName("RestartDockerContainer");

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
}).WithName("KillDockerContainer");

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
}).WithName("DeleteDockerContainer");

app.MapGet($"{baseDockerUrl}/getContainerLogs", Docker.StreamDockerLogs).WithName("GetDockerContainerLogs");

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
	if (targetContainer.ComposePath is null || !File.Exists(targetContainer.ComposePath))
		return Results.NotFound($"Container with ID '{containerId}' does not have a compose file.");

	var stream = new FileStream(targetContainer.ComposePath, FileMode.Open, FileAccess.Read, FileShare.Read);

	return Results.File(stream, "application/ocetet-stream", Path.GetFileName(targetContainer.ComposePath),
		File.GetLastWriteTime(targetContainer.ComposePath));

}).WithName("GetDockerContainerCompose");

app.MapPut($"{baseDockerUrl}/setContainerCompose", async (HttpContext context, string? containerId, bool? restartContainer) =>
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
	if (context.Request.ContentLength is null or 0) return Results.BadRequest("The request body must be specified and not empty.");

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
	if (targetContainer.ComposePath is null || !File.Exists(targetContainer.ComposePath))
		return Results.NotFound("This container does not have a compose file.");
	if (!targetContainer.IsManaged) return Results.BadRequest("This container is not managed by Bayt.");

	// Actual logic
	await using (var fileStream = new FileStream(targetContainer.ComposePath, FileMode.Truncate, FileAccess.Write,
		             FileShare.None))
	{
		await context.Request.Body.CopyToAsync(fileStream);
	}

	if (!(restartContainer ?? false)) return Results.NoContent();


	var dockerRequest = await Docker.SendRequest($"containers/{containerId}/restart", "POST");
	return dockerRequest.Status switch
	{
		HttpStatusCode.NoContent => Results.NoContent(),
		HttpStatusCode.NotFound => Results.NotFound($"[Reboot] Container with ID '{containerId}' was not found."),
		HttpStatusCode.InternalServerError => Results.InternalServerError(
			$"[Reboot] Docker returned an error while restarting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}"),
		_ => Results.InternalServerError(
			$"[Reboot] Docker returned an unknown error while restarting container with ID '{containerId}'. ({dockerRequest.Status})\nBody: {dockerRequest.Body}")
	};
	}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status500InternalServerError)
	.WithName("SetDockerContainerCompose");

app.MapPost($"{baseDockerUrl}/ownContainer", async (string? containerId) =>
{
	const string defaultFlagContents = "This file indicates that this container is managed by Bayt. " +
	                                   "It is safe to delete it to manually un-manage this container.";

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
	if (targetContainer.ComposePath is null || !File.Exists(targetContainer.ComposePath))
		return Results.NotFound($"Container with ID '{containerId}' does not have a compose file.");
	if (targetContainer.IsManaged) return Results.Text("This container is already managed by Bayt.",
		"text/plain", Encoding.UTF8, StatusCodes.Status304NotModified);

	var composeDir = Path.GetDirectoryName(targetContainer.ComposePath) ?? "/";
	File.WriteAllText(Path.Combine(composeDir, ".BaytManaged"), defaultFlagContents);

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status500InternalServerError)
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
	if (targetContainer.ComposePath is null || !File.Exists(targetContainer.ComposePath))
		return Results.NotFound($"Container with ID '{containerId}' does not have a compose file.");
	if (!targetContainer.IsManaged) return Results.Text("This container is already not managed by Bayt.",
		"text/plain", Encoding.UTF8, StatusCodes.Status304NotModified);

	var composeDir = Path.GetDirectoryName(targetContainer.ComposePath) ?? "/";
	File.Delete(Path.Combine(composeDir, ".BaytManaged"));

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status304NotModified)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status500InternalServerError)
	.WithName("DisownDockerContainer");

app.MapDelete($"{baseDockerUrl}/pruneContainers", async () =>
{
	if (!Docker.IsDockerAvailable) return Results.InternalServerError("Docker is not available on this system.");

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
	if (targetContainer.ComposePath is null || !File.Exists(targetContainer.ComposePath))
		return Results.NotFound($"Container with ID '{containerId}' does not have a compose file.");

	return Results.Json(targetContainer.Stats.ToDictionary());
}).Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.Produces(StatusCodes.Status500InternalServerError)
	.Produces(StatusCodes.Status200OK)
	.WithName("GetDockerContainerStats");


if (Environment.GetEnvironmentVariable("BAYT_SKIP_FIRST_FETCH") == "1")
{
	if (Docker.IsDockerAvailable) Console.WriteLine("[INFO] Docker is available. Docker endpoints will be available.");
	Console.WriteLine("[INFO] Skipping first fetch cycle. This may cause the first request to be slow.");
}
else
{
	// Do a fetch cycle to let the constructors run.
	List<Task> fetchTasks = [
		Task.Run(StatsApi.CpuData.UpdateData),
		Task.Run(GpuHandling.FullGpusData.UpdateData),
		Task.Run(StatsApi.MemoryData.UpdateData),
		Task.Run(DiskHandling.FullDisksData.UpdateData)
	];

	if (Docker.IsDockerAvailable)
	{
		Console.WriteLine("[INFO] Docker is available. Docker endpoints will be available.");
		fetchTasks.Add(Task.Run(Docker.DockerContainers.UpdateData));
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