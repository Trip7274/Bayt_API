using System.Globalization;

namespace Bayt_API;

internal static class ParsingMethods
{
	internal static T? ParseTypeNullable<T>(string value) where T : struct, IParsable<T>
	{
		if (value == "null" || !T.TryParse(value, CultureInfo.CurrentCulture, out var result))
		{
			return null;
		}

		if (result is float f)
		{
			// This is a bit of a mess
			result = (T) (object) (float) Math.Round((decimal)f, 2);
		}

		return result;
	}
}
