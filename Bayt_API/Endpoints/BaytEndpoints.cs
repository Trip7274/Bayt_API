using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

namespace Bayt_API.Endpoints;

public static class BaytEndpoints
{
	private static async IAsyncEnumerable<SseItem<string>> StreamBaytLogs(byte verbosity, byte initialContext, ushort streamId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		Queue<string> bufferedLogs = [];
		if (verbosity >= ApiConfig.ApiConfiguration.LogVerbosity && initialContext > 0)
		{
			lock (Logs.LogBook.BookWriteLock)
			{
				Logs.StreamWrittenTo += EnqueueLogs;

				string[] pastLogs = File.ReadAllLines(Path.Combine(ApiConfig.ApiConfiguration.PathToLogFolder,
					$"[{DateOnly.FromDateTime(DateTime.Now).ToString("O")}] baytLog.log"));
				if (pastLogs.Length > initialContext)
				{
					pastLogs = pastLogs[^initialContext..];
				}

				foreach (var pastLogEntry in pastLogs)
				{
					bufferedLogs.Enqueue(pastLogEntry);
				}
			}
		}
		else
		{
			Logs.StreamWrittenTo += EnqueueLogs;
		}

		while (!cancellationToken.IsCancellationRequested)
		{
			if (bufferedLogs.Count > 0)
			{
				yield return new SseItem<string>(bufferedLogs.Dequeue(), "newLogEntry");
			}
			else
			{
				await Task.Delay(500, CancellationToken.None);
			}
		}
		Logs.StreamWrittenTo -= EnqueueLogs;
		Logs.LogBook.Write(new(StreamId.Verbose, $"StreamBaytLogs [{streamId:X4}]", "Log streaming has stopped."));
		yield break;

		void EnqueueLogs(object? e, LogEntry logEntry)
		{
			if (logEntry.StreamIdByte <= verbosity) bufferedLogs.Enqueue(logEntry.ToString());
		}
	}


	public static void MapBaytEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet($"{ApiConfig.BaseApiUrlPath}/logs/getStream", (CancellationToken cancellationToken, byte? verbosity, byte initialContext = 50) =>
		{
			if (ApiConfig.ApiConfiguration.LogVerbosity == 0) return Results.BadRequest("Logging is disabled.");
			initialContext = byte.Clamp(initialContext, 0, 100);
			verbosity ??= ApiConfig.ApiConfiguration.LogVerbosity;
			var streamId = (ushort) Random.Shared.Next(0, ushort.MaxValue);

			Logs.LogBook.Write(new (StreamId.Verbose, $"StreamBaytLogs [{streamId:X4}]", $"Streaming Bayt logs up to {(StreamId) verbosity} streams and an initial context of {initialContext} lines."));


			return Results.ServerSentEvents(StreamBaytLogs(verbosity.Value, initialContext, streamId, cancellationToken));
		}).Produces(StatusCodes.Status200OK)
			.Produces(StatusCodes.Status400BadRequest)
			.WithSummary("Returns a stream of Bayt's logs.")
			.WithDescription("`verbosity` is optional and specifies the level of log verbosity to follow. Defaults to Bayt's configured `LogVerbosity` setting. " +
			                 "`initialContext` is optional and referres to how many lines of past logs to include at the start of the stream. Defaults to 50 [Range: 0-100]. " +
			                 "(`initialContext` does not respect `verbosity` and will not include anything higher than Bayt's `LogVerbosity` settings)")
			.WithTags("Logs")
			.WithName("GetBaytLogsSse");
	}
}