using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Bayt_API;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<SystemDataCache>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();
Console.WriteLine($"Starting API at: http://localhost:{ApiConfig.NetworkPort}");
app.Urls.Add($"http://localhost:{ApiConfig.NetworkPort}");



app.MapGet($"{ApiConfig.BaseApiUrlPath}/getStats", async (SystemDataCache cache, HttpContext context) =>
	{
		string[] possibleStats = ["Meta", "System", "CPU", "GPU", "Memory", "Mounts"];

		List<string> requestedStats;
		try
		{
			requestedStats =
				(await RequestChecking.ValidateAndDeserializeJsonBody<Dictionary<string, List<string>>>(context) ?? [])
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
	.WithName("GetStats")
	.Produces(StatusCodes.Status200OK);



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

}).WithName("ChangeDelay").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest);


app.MapGet($"{ApiConfig.BaseApiUrlPath}/getApiConfigs", () =>
{
	return Results.Text(JsonSerializer.Serialize(ApiConfig.MainConfigs.ConfigProps), "application/json", statusCode:StatusCodes.Status200OK);
}).WithName("GetActiveApiConfigs").Produces(StatusCodes.Status200OK);


app.MapPost($"{ApiConfig.BaseApiUrlPath}/updateLiveConfigs", () =>
{
	ApiConfig.MainConfigs.UpdateConfig();

	return Results.NoContent();

}).WithName("UpdateLiveConfigs").Produces(StatusCodes.Status200OK);



// Mount management endpoints

app.MapGet($"{ApiConfig.BaseApiUrlPath}/getMountsList", () =>
{
	return Results.Text(JsonSerializer.Serialize(ApiConfig.MainConfigs.ConfigProps.WatchedMounts), "application/json", statusCode:StatusCodes.Status200OK);
}).WithName("GetMountsList").Produces(StatusCodes.Status200OK);


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
}).WithName("AddMounts").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest);


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
}).WithName("RemoveMounts");



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
});

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
});

app.MapGet($"{ApiConfig.BaseApiUrlPath}/GetWolClients", () =>
{
	return Results.Text(JsonSerializer.Serialize(ApiConfig.MainConfigs.ConfigProps.WolClients), "application/json", statusCode:StatusCodes.Status200OK);
}).WithName("GetWolClients").Produces(StatusCodes.Status200OK);

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
});
app.Run();
