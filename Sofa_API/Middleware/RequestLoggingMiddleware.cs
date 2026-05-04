using System.Diagnostics;

namespace Sofa_API.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next)
{
	public async Task InvokeAsync(HttpContext context)
	{
		var request = context.Request;
		var stopwatch = Stopwatch.StartNew();

		var remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "(Unknown)";
		var requestTarget = string.IsNullOrEmpty(request.QueryString.Value) ? request.Path.ToString() : $"{request.Path}{request.QueryString}";
		context.Response.Headers.Append("X-Request-Id", context.TraceIdentifier);

		Logs.LogBook.Write(new LogEntry(StreamId.Request, $"Req. [{context.TraceIdentifier}]",
			$"Got a '{request.Method} {requestTarget}' request from {remoteAddress}"));

		try
		{
			await next(context);

			stopwatch.Stop();

			Logs.LogBook.Write(new LogEntry(
				StreamId.Request,
				$"Res. [{context.TraceIdentifier}]",
				$"Responded with {context.Response.StatusCode} in {stopwatch.ElapsedMilliseconds}ms"));
		}
		catch (Exception e)
		{
			stopwatch.Stop();

			Logs.LogBook.Write(new LogEntry(
				StreamId.Request,
				$"Req. [{context.TraceIdentifier}]",
				$"Request failed after {stopwatch.ElapsedMilliseconds}ms: {e.GetType().Name}: {e.Message}"));

			throw;
		}
	}
}