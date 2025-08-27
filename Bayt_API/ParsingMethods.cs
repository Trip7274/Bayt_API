using System.Globalization;
using System.Text;

namespace Bayt_API;

public static class ParsingMethods
{
	public static T? ParseTypeNullable<T>(string value) where T : struct, IParsable<T>
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
	/// Creates a "slug" from text that can be used as part of a valid URL.
	///
	/// Invalid characters are converted to hyphens. Punctuation that is
	/// perfect valid in a URL is also converted to hyphens to keep the
	/// result mostly text. Steps are taken to prevent leading, trailing,
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
}
