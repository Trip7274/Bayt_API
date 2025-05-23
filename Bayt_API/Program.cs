using System.Diagnostics;
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

		var checkedInput = await RequestChecking.CheckContType(context);
		if (checkedInput.ErrorMessage is not null)
		{
			if (checkedInput.RequestBody != "")
			{
				return Results.BadRequest(checkedInput.ErrorMessage);
			}
		}

		List<string> requestedStats;
		try
		{
			requestedStats = (JsonSerializer.Deserialize<Dictionary<string, List<string>>>(checkedInput.RequestBody) ?? []).Values.First();
		}
		catch (JsonException)
		{
			Debug.WriteLine("Got a request with malformed JSON, returning all stats.");
			requestedStats = ["All"];
		}

		if (requestedStats.Count == 0)
		{
			Debug.WriteLine($"Got a request asking for '{string.Join(", ", requestedStats)}', but none matched so we're returning a BadRequest.");
			return Results.BadRequest("List must contain more than 0 elements.");
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



// General Api Config management

app.MapPost($"{ApiConfig.BaseApiUrlPath}/editConfig", async (HttpContext context) => {
	// TODO: Implement Auth and Rate Limiting before blindly trusting the request.

	var checkedInput = await RequestChecking.CheckContType(context);
	if (checkedInput.ErrorMessage is null)
	{
		return Results.BadRequest(checkedInput.ErrorMessage);
	}

	var newConfigs = JsonSerializer.Deserialize<Dictionary<string, dynamic>>(checkedInput.RequestBody) ?? [];
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


// Mounts management

app.MapGet($"{ApiConfig.BaseApiUrlPath}/getMountsList", () =>
{
	return Results.Text(JsonSerializer.Serialize(ApiConfig.MainConfigs.ConfigProps.WatchedMounts), "application/json", statusCode:StatusCodes.Status200OK);
}).WithName("GetMountsList").Produces(StatusCodes.Status200OK);


app.MapPost($"{ApiConfig.BaseApiUrlPath}/addMounts", async (HttpContext context) =>
{
	var checkedInput = await RequestChecking.CheckContType(context);
	if (checkedInput.ErrorMessage is not null)
	{
		return Results.BadRequest(checkedInput.ErrorMessage);
	}

	var mountPoints = JsonSerializer.Deserialize<Dictionary<string, string?>>(checkedInput.RequestBody) ?? [];
	if (mountPoints.Count == 0)
	{
		return Results.BadRequest("List must contain more than 0 elements.");
	}

	ApiConfig.MainConfigs.AddMountpoint(mountPoints);

	return Results.NoContent();
}).WithName("AddMounts").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest);


app.MapDelete($"{ApiConfig.BaseApiUrlPath}/removeMounts", async (HttpContext context) =>
{
	var checkedInput = await RequestChecking.CheckContType(context);
	if (checkedInput.ErrorMessage is not null)
	{
		return Results.BadRequest(checkedInput.ErrorMessage);
	}

	// This is a bit of a mess, but it's shorthand for:
	// get the list of mount points from the request, if it's null, get an empty list.
	var mountPoints = (JsonSerializer.Deserialize<Dictionary<string, List<string>>>(checkedInput.RequestBody) ?? new() {{"Mounts", []}})["Mounts"];
	if (mountPoints.Count == 0)
	{
		return Results.BadRequest("List must contain more than 0 elements.");
	}

	ApiConfig.MainConfigs.RemoveMountpoint(mountPoints);

	return Results.NoContent();
}).WithName("RemoveMounts");

app.Run();
