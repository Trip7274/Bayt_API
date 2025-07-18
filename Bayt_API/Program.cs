using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Bayt_API;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<SystemDataCache>();

Console.WriteLine($"[INFO] Adding URL '{IPAddress.Loopback}:{ApiConfig.NetworkPort}' to listen list");
builder.WebHost.ConfigureKestrel(opts => opts.Listen(IPAddress.Loopback, ApiConfig.NetworkPort));

if (Environment.GetEnvironmentVariable("BAYT_LOCALHOST_ONLY") != "1")
{
	var localIp = StatsApi.GetLocalIpAddress();
	if (Environment.GetEnvironmentVariable("BAYT_LOCALIP") != null)
	{
		if (IPAddress.TryParse(Environment.GetEnvironmentVariable("BAYT_LOCALIP"), out var localIpParsed))
		{
			localIp = localIpParsed;
			Console.WriteLine($"[INFO] Using BAYT_LOCALIP environment variable to override detected IP address: '{localIp}'");
		}
		else
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"[WARNING] BAYT_LOCALIP environment variable is set to '{Environment.GetEnvironmentVariable("BAYT_LOCALIP")}', but it doesn't appear to be a valid IP address. Falling back to default selection.");
			Console.ResetColor();
		}
	}

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



app.MapGet($"{ApiConfig.BaseApiUrlPath}/getStats", async (SystemDataCache cache, HttpContext context) =>
	{
		string[] possibleStats = ["Meta", "System", "CPU", "GPU", "Memory", "Mounts"];

		List<string> requestedStats;
		try
		{
			requestedStats =
				(await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, List<string>>>(context, false) ?? [])
				.Values.First();
		}
		catch (BadHttpRequestException e)
		{
			return Results.BadRequest(e.Message);
		}
		catch (JsonException)
		{
			Debug.WriteLine("Got a request with malformed JSON, returning all stats.");
			requestedStats = ["All"];
		}


		if (requestedStats.Count == 0)
		{
			Debug.WriteLine($"Got a request asking for '{string.Join(", ", requestedStats)}', but none matched so we're returning a BadRequest.");
			return Results.BadRequest("Stat list must contain at least 1 element.");
		}

		if (requestedStats.Contains("All"))
		{
			requestedStats = possibleStats.ToList();
		}

		// Request checks done

		Debug.WriteLine($"Got a request asking for: {string.Join(", ", requestedStats)}");

		Dictionary<string, Dictionary<string, dynamic>[]> responseDictionary = [];

		foreach (var requestedStat in requestedStats)
		{
			switch (requestedStat)
			{
				case "Meta":
				{
					responseDictionary.Add("Meta", [
						new Dictionary<string, dynamic>
						{
							{ "Version", ApiConfig.Version },
							{ "ApiVersion", ApiConfig.ApiVersion },
							{ "LastUpdate", ApiConfig.LastUpdated.ToUniversalTime() },
							{ "NextUpdate", ApiConfig.LastUpdated.ToUniversalTime().AddSeconds(ApiConfig.MainConfigs.ConfigProps.SecondsToUpdate) },
							{ "DelaySec", ApiConfig.MainConfigs.ConfigProps.SecondsToUpdate }
						}
					]);
					break;
				}

				case "System":
				{
					var generalSpecs = cache.GetGeneralSpecs();
					responseDictionary.Add("System", [
						JsonSerializer.Deserialize<Dictionary<string, dynamic>>(JsonSerializer.Serialize(generalSpecs)) ?? []
					]);
					break;
				}

				case "CPU":
				{
					var cpuStats = cache.GetCpuData();
					responseDictionary.Add("CPU", [
						new Dictionary<string, dynamic>
						{
							{ "Name", StatsApi.CpuData.Name.TrimEnd('\n') },
							{ "UtilPerc", cpuStats.UtilizationPerc },
							{ "CoreCount", cpuStats.PhysicalCoreCount },
							{ "ThreadCount", cpuStats.ThreadCount }
						}
					]);
					break;
				}

				case "GPU":
				{
					var gpuStats = cache.GetGpuData();
					var gpuStatsDict = new List<Dictionary<string, dynamic?>>();

					foreach (var gpuData in gpuStats)
					{
						if (gpuData.IsMissing)
						{
							gpuStatsDict.Add(new Dictionary<string, dynamic?>
							{
								{ "IsMissing", gpuData.IsMissing },
								{ "Brand", gpuData.Brand }
							});
							continue;
						}

						gpuStatsDict.Add(
							JsonSerializer.Deserialize<Dictionary<string, dynamic?>>(JsonSerializer.Serialize(gpuData)) ?? []
							);
					}
					responseDictionary.Add("GPU", gpuStatsDict.ToArray()!);

					break;
				}

				case "Memory":
				{
					var memoryStats = cache.GetMemoryData();

					responseDictionary.Add("Memory", [
						JsonSerializer.Deserialize<Dictionary<string, dynamic>>(JsonSerializer.Serialize(memoryStats)) ?? []
					]);

					break;
				}

				case "Mounts":
				{
					var watchedDiskData = cache.GetDiskData();

					var disksDictList = new List<Dictionary<string, dynamic?>>();
					foreach (var watchedDisk in watchedDiskData)
					{
						if (watchedDisk.IsMissing)
						{
							disksDictList.Add(new Dictionary<string, dynamic?>
							{
								{ "MountPoint", watchedDisk.MountPoint },
								{ "MountName", watchedDisk.MountName },
								{ "IsMissing", watchedDisk.IsMissing }
							});
							continue;
						}
						disksDictList.Add(
							JsonSerializer.Deserialize<Dictionary<string, dynamic?>>(JsonSerializer.Serialize(watchedDisk)) ?? []
							);
					}

					responseDictionary.Add("Mounts", disksDictList.ToArray()!);

					break;
				}

				default:
				{
					responseDictionary.Add(requestedStat, [[]]);
					break;
				}
			}
		}


		if (!Caching.IsDataFresh())
		{
			ApiConfig.LastUpdated = DateTime.Now;
		}
		return Results.Text(JsonSerializer.Serialize(responseDictionary), "application/json", statusCode:StatusCodes.Status200OK);
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
	return Results.Text(JsonSerializer.Serialize(ApiConfig.MainConfigs.ConfigProps), "application/json", statusCode:StatusCodes.Status200OK);
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
	return Results.Text(JsonSerializer.Serialize(ApiConfig.MainConfigs.ConfigProps.WatchedMounts), "application/json", statusCode:StatusCodes.Status200OK);
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

	if (mountPoints.Count == 0)
	{
		return Results.BadRequest("Mountpoints list must contain at least 1 element.");
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
	return Results.Text(JsonSerializer.Serialize(ApiConfig.MainConfigs.ConfigProps.WolClients), "application/json", statusCode:StatusCodes.Status200OK);
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

app.MapGet($"{ApiConfig.BaseApiUrlPath}/getData", async (HttpContext context) =>
{
	/* Format:
	 *
	 * {
	 *		"clientName": "jsonFileNameWithExtension"
	 * }
	 *
	 * Sorta like "clientData/{key}/{value}"
	 * Must authenticate the clientName to be under the jurisdiction of the requesting client in the future.
	 */

	KeyValuePair<string, string> dataFile;
	try
	{
		dataFile = (await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, string>>(context) ?? throw new BadHttpRequestException("Request body must be a JSON object.")).First();
	}
	catch (BadHttpRequestException e)
	{
		return Results.BadRequest(e.Message);
	}

	try
	{
		return Results.Text(DataEndpointManagement.GetDataFile(dataFile.Key, dataFile.Value), dataFile.Value.EndsWith(".json") ? "application/json" : "text/plain");
	}
	catch (FileNotFoundException e)
	{
		return Results.NotFound(e.Message);
	}
}).Produces(StatusCodes.Status200OK)
	.Produces(StatusCodes.Status400BadRequest)
	.Produces(StatusCodes.Status404NotFound)
	.WithName("GetClientData");

app.MapPost($"{ApiConfig.BaseApiUrlPath}/setData", async (HttpContext context) =>
{
	/*
	 * The syntax of the request should be:
	 *	{
	 * 		"format": "json",
	 * 		"folder": "Test",
	 * 		"fileName": "configs.json",
	 * 		"data": {
	 * 			"Version": 1,
	 *  		"Working": true
	 * 		}
	 *	}
	 *
	 * The value of "data" can be a string containing Base64-encoded data if "format" is not "json"
	 */

	List<Dictionary<string, dynamic>> piecesOfData;
	try
	{
		piecesOfData = (await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, List<Dictionary<string, dynamic>>>>(context) ?? throw new BadHttpRequestException("Request body must be a JSON object.")).Values.First();
	}
	catch (BadHttpRequestException e)
	{
		return Results.BadRequest(e.Message);
	}

	if (piecesOfData.Count == 0)
	{
		return Results.BadRequest("Request body must contain at least 1 element.");
	}

	foreach (var pieceOfData in piecesOfData)
	{
		if (pieceOfData.Count != 4)
		{
			throw new BadHttpRequestException("Invalid format of data object.");
		}

		DataEndpointManagement.DataFileMetadata metadataObject;
		if (pieceOfData["format"].ToString() == "json")
		{
			metadataObject = new DataEndpointManagement.DataFileMetadata(
				pieceOfData["format"].ToString(),
				pieceOfData["folder"].ToString(),
				pieceOfData["fileName"].ToString(),
				null,
				JsonDocument.Parse(JsonSerializer.Serialize(pieceOfData["data"]))
			);
		}
		else
		{
			metadataObject = new DataEndpointManagement.DataFileMetadata(
				pieceOfData["format"].ToString(),
				pieceOfData["folder"].ToString(),
				pieceOfData["fileName"].ToString(),
				pieceOfData["data"].ToString(),
				null

			);
		}

		await DataEndpointManagement.SetDataFile(metadataObject);
	}

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.WithName("SetClientData");

app.MapDelete($"{ApiConfig.BaseApiUrlPath}/deleteData", async (HttpContext context) =>
{
	/*
	 * {
	 *		"folder": "file"
	 * }
	 */

	Dictionary<string, string> dataFiles;
	try
	{
		dataFiles = await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, string>>(context) ?? throw new BadHttpRequestException("Request body must be a JSON object.");
	}
	catch (BadHttpRequestException e)
	{
		return Results.BadRequest(e.Message);
	}

	if (dataFiles.Count == 0)
	{
		return Results.BadRequest("Request body must contain at least 1 element.");
	}

	DataEndpointManagement.DeleteDataFiles(dataFiles);

	return Results.NoContent();
}).Produces(StatusCodes.Status204NoContent)
	.Produces(StatusCodes.Status400BadRequest)
	.WithName("DeleteClientData");

app.MapDelete($"{ApiConfig.BaseApiUrlPath}/deletefolder", async (HttpContext context) =>
{
	/*
	 * {
	 *		"folderName": "folder1"
	 * }
	 */

	string folderName;
	try
	{
		folderName = (await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, string>>(context) ?? throw new BadHttpRequestException("Request body must be a JSON object."))["folderName"] ;
	}
	catch (BadHttpRequestException e)
	{
		return Results.BadRequest(e.Message);
	}

	if (folderName.Length == 0)
	{
		return Results.BadRequest("Requested folder name must not be empty.");
	}

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


Console.WriteLine("[INFO] Starting API...\n");

app.Run();
