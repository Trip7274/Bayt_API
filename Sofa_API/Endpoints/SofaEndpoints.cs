using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

namespace Sofa_API.Endpoints;

public static class SofaEndpoints
{
	private static async IAsyncEnumerable<SseItem<string>> StreamSofaLogs(StreamId verbosity, byte initialContext, ushort streamId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		Queue<string> bufferedLogs = [];
		if (verbosity >= ApiConfig.ApiConfiguration.LogVerbosity && initialContext > 0)
		{
			lock (Logs.LogBook.BookWriteLock)
			{
				Logs.StreamWrittenTo += EnqueueLogs;

				string[] pastLogs = File.ReadAllLines(Path.Combine(ApiConfig.ApiConfiguration.PathToLogFolder,
					$"[{DateOnly.FromDateTime(DateTime.Now).ToString("O")}] sofaLog.log"));
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
		Logs.LogBook.Write(new (StreamId.Verbose, $"StreamSofaLogs [{streamId:X4}]", $"Streaming Sofa logs up to {verbosity} streams and an initial context of {initialContext} lines."));

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
		Logs.LogBook.Write(new(StreamId.Verbose, $"StreamSofaLogs [{streamId:X4}]", "Log streaming has stopped."));
		yield break;

		void EnqueueLogs(object? e, LogEntry logEntry)
		{
			// Only enqueue logs following the verbosity level, but also force any logs regarding this specific stream to be included.
			if (logEntry.StreamId <= verbosity || logEntry.ModuleName == $"StreamSofaLogs [{streamId:X4}]")
				bufferedLogs.Enqueue(logEntry.ToString());
		}
	}


	public static void MapSofaEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapGet($"{ApiConfig.BaseApiUrlPath}/logs/getStream", (HttpContext context, CancellationToken cancellationToken, int? verbosity, byte initialContext = 50) =>
		{
			if (verbosity == 0) return Results.BadRequest(new { message = "Verbosity cannot be 0."});

			initialContext = byte.Clamp(initialContext, 0, 100);
			if (verbosity is not null) verbosity = int.Clamp(verbosity.Value, 1, byte.MaxValue);
			var verbosityEnum = ParsingMethods.ClampToMaxStreamIdValue((byte?) verbosity) ?? ApiConfig.ApiConfiguration.LogVerbosity;
			var streamId = (ushort) Random.Shared.Next(0, ushort.MaxValue);
			context.Response.Headers.Append("X-Stream-Id", streamId.ToString());
			context.Response.Headers.Append("X-Verbosity", $"{verbosityEnum}/{(byte) verbosityEnum}");
			context.Response.Headers.Append("X-Context-Included", $"{verbosityEnum >= ApiConfig.ApiConfiguration.LogVerbosity && initialContext > 0}");


			return Results.ServerSentEvents(StreamSofaLogs(verbosityEnum, initialContext, streamId, cancellationToken));
		}).Produces(StatusCodes.Status200OK)
			.Produces(StatusCodes.Status400BadRequest)
			.WithSummary("Returns a stream of Sofa's logs.")
			.WithDescription("`verbosity` is optional and specifies the level of log verbosity to follow. Defaults to Sofa's configured `LogVerbosity` setting. " +
			                 "`initialContext` is optional and referres to how many lines of past logs to include at the start of the stream. Defaults to 50 [Range: 0-100]. " +
			                 "(`initialContext` does not respect `verbosity` and will not include anything higher than Sofa's `LogVerbosity` settings)")
			.WithTags("Logs")
			.WithName("GetSofaLogsSse")
			.RequireAuthorization("MultiAuth", "logs:view");
	}
}