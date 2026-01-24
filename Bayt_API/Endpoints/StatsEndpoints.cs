using System.Text.Json;

namespace Bayt_API.Endpoints;

public static class StatsEndpoints
{
	public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet($"{ApiConfig.BaseApiUrlPath}/stats/getCurrent", async (HttpResponse response, bool? meta, bool? system, bool? cpu, bool? gpu, bool? memory, bool? mounts, bool? batteries) =>
		{
			Dictionary<ApiConfig.SystemStats, bool?> requestedStatsRaw = new() {
				{ ApiConfig.SystemStats.Meta, meta },
				{ ApiConfig.SystemStats.System, system },
				{ ApiConfig.SystemStats.Cpu, cpu },
				{ ApiConfig.SystemStats.Gpu, gpu },
				{ ApiConfig.SystemStats.Memory, memory },
				{ ApiConfig.SystemStats.Mounts, mounts },
				{ ApiConfig.SystemStats.Batteries, batteries },
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
			Logs.LogBook.Write(new (StreamId.Verbose, "GetStats", $"Got a request for: {string.Join(", ", requestedStats.Select(stat => stat.ToString()))}"));

			// Request checks done

			Dictionary<string, dynamic> responseDictionary = [];

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
					case ApiConfig.SystemStats.Batteries:
					{
						fetchTasks.Add(Task.Run(StatsApi.BatteryList.UpdateDataIfNecessary));
						break;
					}
				}
			}
			await Task.WhenAll(fetchTasks);

			// Request assembly
			List <DateTime> lastUpdateStamps = [];
			foreach (var requestedStat in requestedStats)
			{
				switch (requestedStat)
				{
					case ApiConfig.SystemStats.Meta:
					{
						responseDictionary.Add("Meta",
							new Dictionary<string, dynamic?>
							{
								{ nameof(ApiConfig.Version), ApiConfig.Version },
								{ nameof(ApiConfig.ApiVersion), ApiConfig.ApiVersion },
								{ nameof(ApiConfig.ApiConfiguration.CacheLifetime),
									ApiConfig.ApiConfiguration.CacheLifetime },
								{ "BaytUptime", ApiConfig.BaytStartStopwatch.Elapsed }
							});
						break;
					}

					case ApiConfig.SystemStats.System:
					{
						responseDictionary.Add("System", StatsApi.GeneralSpecs.ToDictionary());
						break;
					}

					case ApiConfig.SystemStats.Cpu:
					{
						responseDictionary.Add("CPU", StatsApi.CpuData.ToDictionary());
						lastUpdateStamps.Add(StatsApi.CpuData.LastUpdate);
						break;
					}

					case ApiConfig.SystemStats.Gpu:
					{
						responseDictionary.Add("GPU", GpuHandling.FullGpusData.ToDictionary());
						lastUpdateStamps.Add(GpuHandling.FullGpusData.LastUpdate);
						break;
					}

					case ApiConfig.SystemStats.Memory:
					{
						responseDictionary.Add("Memory", StatsApi.MemoryData.ToDictionary());
						lastUpdateStamps.Add(StatsApi.MemoryData.LastUpdate);
						break;
					}

					case ApiConfig.SystemStats.Mounts:
					{
						responseDictionary.Add("Mounts", DiskHandling.FullDisksData.ToDictionary());
						lastUpdateStamps.Add(DiskHandling.FullDisksData.LastUpdate);
						break;
					}

					case ApiConfig.SystemStats.Batteries:
					{
						responseDictionary.Add("Batteries", StatsApi.BatteryList.ToDictionary());
						lastUpdateStamps.Add(StatsApi.BatteryList.LastUpdate);
						break;
					}
					default:
					{
						throw new ArgumentOutOfRangeException(requestedStat.ToString());
					}
				}
			}

			var latestUpdate = lastUpdateStamps.Max();
			response.Headers.Append("Expires", (latestUpdate + ApiConfig.ApiConfiguration.CacheLifetime)
				.ToString("R"));
			await response.Body.WriteAsync( JsonSerializer.SerializeToUtf8Bytes(responseDictionary));
			await response.CompleteAsync();

			Logs.LogBook.Write(new (StreamId.Verbose, "GetStats", $"Sent off the response with {responseDictionary.Count} fields."));
			return null;
		}).Produces(StatusCodes.Status200OK)
			.Produces(StatusCodes.Status400BadRequest)
			.WithSummary("Returns the stats/metrics of the server according to what was requested. Defaults to all in case none were specified.")
			.WithTags("Stats")
			.WithName("GetSystemMetrics");
	}
}