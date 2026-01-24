namespace Bayt_API.Endpoints;

public static class PowerEndpoints
{
	public static void MapPowerEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapPost($"{ApiConfig.BaseApiUrlPath}/power/shutdown", async () =>
		{
			Logs.LogBook.Write(new (StreamId.Info, "Server Power", "Recieved poweroff request, attempting to shut down..."));
			var operationShell = await ShellMethods.RunShell("sudo", ["-n", "/sbin/poweroff"]);

			// Realistically, execution shouldn't get this far.

			if (operationShell.IsSuccess) return Results.NoContent();

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

		app.MapPost($"{ApiConfig.BaseApiUrlPath}/power/restart", async () =>
		{
			Logs.LogBook.Write(new (StreamId.Info, "Server Power", "Recieved restart request, attempting to restart..."));
			var operationShell = await ShellMethods.RunShell("sudo", ["-n", "/sbin/reboot"]);

			if (operationShell.IsSuccess) return Results.NoContent();

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
	}
}