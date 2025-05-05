namespace Bayt_API;

public static class RequestChecking
{
	public static string? CheckContType(HttpContext context)
	{
		if (context.Request.ContentLength == 0)
		{
			return "ContentLength is zero, please include a JSON array of mount points in the request body, e.g. [\"/mnt/hdd\", \"/mnt/ssd\"].";
		}
		if (context.Request.ContentType != "application/json")
		{
			return $"Content-Type is not application/json, current Content-Type header: '{context.Request.ContentType}'.";
		}

		return null;
	}
}