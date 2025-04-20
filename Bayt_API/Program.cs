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

app.MapGet("/getStats", (SystemDataCache cache) =>
	{
		var generalSpecs = cache.GetGeneralSpecs();

		var watchedDiskData = cache.GetDiskData();

		var cpuStats = cache.GetCpuData();
		var gpuStats = cache.GetGpuData();
		var memoryStats = cache.GetMemoryData();

		var isPrivileged = cache.IsPrivileged;

		var responseDictionary =
			new Dictionary<string, Dictionary<string, string>[]>
			{
				{
					"Meta", [
						new Dictionary<string, string>
						{
							{ "Version", Globals.Version },
							{ "ApiVersion", $"{Globals.ApiVersion}" },
							{ "PrivilegedServer", $"{isPrivileged}" },
							{ "LastUpdate", Globals.LastUpdated.ToUniversalTime().ToLongTimeString() },
							{ "NextUpdate", Globals.LastUpdated.ToUniversalTime().AddSeconds(Globals.SecondsToUpdate).ToLongTimeString() },
							{ "DelaySec", Globals.SecondsToUpdate.ToString() },
							{ "IsNew", $"{!Caching.IsDataStale()}" }
						}
					]
				},

				{
					"System", [
						new Dictionary<string, string>
						{
							{ "Hostname", generalSpecs.Hostname.TrimEnd('\n') },
							{ "KernelName", generalSpecs.KernelName.TrimEnd('\n') },
							{ "KernelVersion", generalSpecs.KernelVersion.TrimEnd('\n') }
						}
					]
				},

				{
					"CPU", [
						new Dictionary<string, string>
						{
							{ "Name", StatsApi.CpuData.Name.TrimEnd('\n') },
							{ "UtilPerc", $"{cpuStats.UtilizationPerc}" },
							{ "CoreCount", $"{cpuStats.PhysicalCoreCount}" },
							{ "ThreadCount", $"{cpuStats.ThreadCount}" }
						}
					]
				}
			};
		var gpuStatsDict = new List<Dictionary<string, string>>();

		foreach (var gpuData in gpuStats)
		{
			gpuStatsDict.Add(new Dictionary<string, string>
			{
				{"Name", gpuData.Name},
				{"Brand", gpuData.Brand},
				{"PciId", gpuData.PciId},

				{"OverallUtilPerc", $"{gpuData.GraphicsUtilPerc}"},
				{"VramUtilPerc", $"{gpuData.VramUtilPerc}"},
				{"VramTotalBytes", $"{gpuData.VramTotalBytes}"},
				{"VramUsedBytes", $"{gpuData.VramUsedBytes}"},

				{"EncoderUtilPerc", $"{gpuData.EncoderUtilPerc}"},
				{"DecoderUtilPerc", $"{gpuData.DecoderUtilPerc}"},
				{"VideoEnhanceUtilPerc", $"{gpuData.VideoEnhanceUtilPerc}"},

				{"GraphicsFrequencyMHz", $"{gpuData.GraphicsFrequency}"},
				{"EncoderDecoderFrequencyMHz", $"{gpuData.EncDecFrequency}"},

				{"PowerUseWatts", $"{gpuData.PowerUse}"},
				{"TemperatureC", $"{gpuData.TemperatureC}"}
			});
		}
		responseDictionary.Add("GPU", gpuStatsDict.ToArray());

		responseDictionary.Add("Memory", [new Dictionary<string, string>
		{
			{"AvailableMemoryBytes", $"{memoryStats.AvailableMemory}"},

			{"UsedMemoryBytes", $"{memoryStats.UsedMemory}"},

			{"TotalMemoryBytes", $"{memoryStats.TotalMemory}"},

			{"PercUsed", $"{memoryStats.UsedMemoryPercent}"}
		}]);

		var disksDictList = new List<Dictionary<string, string>>();
		foreach (var watchedDisk in watchedDiskData)
		{
			disksDictList.Add(new Dictionary<string, string>
			{
				{"DeviceName", watchedDisk.DeviceName ?? ""},
				{"MountPoint", watchedDisk.MountPoint},
				{"DevicePath", watchedDisk.DevicePath},
				{"FileSystem", watchedDisk.FileSystem},

				{"UsedDiskBytes", $"{watchedDisk.UsedSize}"},

				{"FreeDiskBytes", $"{watchedDisk.FreeSize}"},

				{"TotalDiskBytes", $"{watchedDisk.TotalSize}"},

				{"PercUsed", $"{watchedDisk.UsedSizePercent}"},

				{"TemperatureLabel", watchedDisk.TemperatureLabel ?? ""},
				{"TemperatureC", $"{watchedDisk.TemperatureC}"},
				{"TemperatureMinC", $"{watchedDisk.TemperatureMinC}"},
				{"TemperatureMaxC", $"{watchedDisk.TemperatureMaxC}"},
				{"TemperatureCritC", $"{watchedDisk.TemperatureCritC}"}
			});
		}

		responseDictionary.Add("Disks", disksDictList.ToArray());

		if (!Caching.IsDataStale())
		{
			Globals.LastUpdated = DateTime.Now;
		}
		return Results.Text(JsonSerializer.Serialize(responseDictionary), "application/json", statusCode:StatusCodes.Status200OK);
	})
	.WithName("GetStats")
	.Produces(StatusCodes.Status200OK);

app.MapPost("/changeDelay", (ushort newDelay) =>
{
	Globals.SecondsToUpdate = newDelay;

	return Results.NoContent();

}).WithName("ChangeDelay")
	.Produces(StatusCodes.Status204NoContent);

app.Run();
