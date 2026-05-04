using Sofa_API.Security;

namespace Sofa_API.Middleware;

public sealed class ClientAlertMiddleware(RequestDelegate next)
{
	public async Task InvokeAsync(HttpContext context)
	{
		var currentClient = SecurityMethods.GetConnectedClient(context);
		if (currentClient is null) return;

		await next(context);

		if (currentClient.CanRegister && Clients.PendingClients.Count > 0)
		{
			var pendingClientIds = Clients.PendingClients.Values.Select(client => client.Guid.ToString());
			context.Response.Headers.Append("X-Pending-Clients", string.Join(" ", pendingClientIds));
		}

		if (Certificates.IsCloseToExpiration && !currentClient.HasAcknowledgedFutureCert)
		{
			context.Response.Headers.Append("X-Sofa-Cert-Update", Certificates.NotAfter.ToString("R"));
		}



		// TODO: Use HTTP Headers for alerts such as Pending Clients (ONLY IF THE CLIENT CAN REGISTER), and Sofa cert updates.
		// TODO: Write them all out onto every response a Client gets.
		// Examples:
		// X-Pending-Clients: {ID} {ID} {ID}
		// X-Pending-Client-Permission-Updates: {ID} {ID} {ID}
		// X-Sofa-Cert-Update: {DATE OF CURRENT CERT'S EXPIRY}
		// ^^ Optionally, allow the Client to "acknowledge" this by sending a specific request to some endpoint
	}
}