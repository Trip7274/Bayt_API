using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Bayt_API;

public static class ParsingMethods
{
	public static T? ParseNullable<T>(this JsonElement value) where T : struct, IParsable<T>
	{
		if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.Array or JsonValueKind.Object) return null;

		if (!T.TryParse(value.GetRawText(), CultureInfo.CurrentCulture, out var result))
		{
			return null;
		}

		return result switch
		{
			float f => (T) (object) MathF.Round(f, 2),
			double d => (T) (object) Math.Round(d, 2),
			_ => result
		};

	}

	public static T? ParseNullable<T>(this string value) where T : struct, IParsable<T>
	{
		if (value == "null" || !T.TryParse(value, CultureInfo.CurrentCulture, out var result))
		{
			return null;
		}

		return result switch
		{
			float f => (T) (object) MathF.Round(f, 2),
			double d => (T) (object) Math.Round(d, 2),
			_ => result
		};
	}

	/// <summary>
	/// Attempts to read the contents of a file at the specified path and parse it into the specified type.
	/// If the file does not exist or the contents cannot be parsed, returns null.
	/// </summary>
	/// <returns>
	///	The file's contents parsed into the specified type, or null if the file does not exist or contains a literal "null".
	/// </returns>
	/// <seealso cref="TryReadFileAsync{T}(string)"/>
	public static T? TryReadFile<T>(string filePath) where T : struct, IParsable<T>
	{
		return !File.Exists(filePath) ? null : File.ReadAllText(filePath).ParseNullable<T>();
	}
	/// <summary>
	/// Asynchronous version of <see cref="TryReadFile{T}(string)"/>.
	/// </summary>
	public static async Task<T?> TryReadFileAsync<T>(string filePath) where T : struct, IParsable<T>
	{
		return File.Exists(filePath) ? (await File.ReadAllTextAsync(filePath)).ParseNullable<T>() : null;
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
