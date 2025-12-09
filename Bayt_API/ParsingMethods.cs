using System.Globalization;
using System.Text;

namespace Bayt_API;

public static class ParsingMethods
{
	public static T? ParseNullable<T>(this string value) where T : struct, IParsable<T>
	{
		if (value == "null" || !T.TryParse(value, CultureInfo.CurrentCulture, out var result))
		{
			return null;
		}

		if (result is float f)
		{
			// This is a bit of a mess
			result = (T) (object) MathF.Round(f, 2);
		}

		return result;
	}
	/// <summary>
	/// Creates a "slug" from a string that can be used as part of a valid URL.
	///
	/// Invalid characters are converted to hyphens. Punctuation that is
	/// perfectly valid in a URL is also converted to hyphens to keep the
	/// result alphanumeric (ASCII). Steps are taken to prevent leading, trailing,
	/// and consecutive hyphens.
	/// </summary>
	/// <param name="input">String to convert to a slug</param>
	/// <returns>The slug-ified string</returns>
	/// <remarks>This method was originally taken from https://www.codeproject.com/Articles/80882/Converting-Text-to-a-URL-Slug</remarks>
	public static string ConvertTextToSlug(string? input)
	{
		if (string.IsNullOrWhiteSpace(input)) return "";
		input = input.Trim();
		if (input.Length > 32) input = input[..32];

		var outpuStringBuilder = new StringBuilder();
		bool wasHyphen = true;
		foreach (var character in input.Where(c => char.IsAsciiLetterOrDigit(c) || char.IsWhiteSpace(c)))
		{
			if (!char.IsWhiteSpace(character))
			{
				outpuStringBuilder.Append(char.ToLower(character));
				wasHyphen = false;
				continue;
			}
			if (wasHyphen) continue;

			outpuStringBuilder.Append('-');
			wasHyphen = true;
		}
		// Avoid trailing hyphens
		if (wasHyphen && outpuStringBuilder.Length > 0)
			outpuStringBuilder.Length--;
		return outpuStringBuilder.ToString();
	}

	public static bool IsEnvVarTrue(string varName)
	{
		var envVar = Environment.GetEnvironmentVariable(varName);
		return envVar?.ToLowerInvariant() switch
		{
			null => false,
			"1" or "yes" or "on" => true,
			"0" or "no" or "off" => false,
			_ => bool.TryParse(envVar, out var boolResult) && boolResult
		};
	}
}
