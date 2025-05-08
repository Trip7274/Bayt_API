namespace Bayt_API;

public static class GpuHandling
{
	public class GpuData
	{
		public required string Brand { get; init; }
		public string Name { get; init; } = "Unidentified GPU";
		public required string PciId { get; init; }
		public bool IsMissing { get; init; }

		public float? GraphicsUtilPerc { get; init; }
		public float? VramUtilPerc { get; init; } // NVIDIA + AMD only
		public float? VramTotalBytes { get; init; } // AMD-only
		public float? VramUsedBytes { get; init; } // AMD-only

		public float? EncoderUtilPerc { get; init; }
		public float? DecoderUtilPerc { get; init; } // NVIDIA-only
		public float? VideoEnhanceUtilPerc { get; init; } // Intel-only

		public float? GraphicsFrequency { get; init; }
		public float? EncDecFrequency { get; init; } // NVIDIA-only

		public float? PowerUse { get; init; }
		public sbyte? TemperatureC { get; init; } // NVIDIA + AMD only
	}

	public static List<GpuData> GetGpuDataList(List<GpuData>? oldGpuData = null)
	{
		if (oldGpuData is not null && Caching.IsDataStale())
		{
			return oldGpuData;
		}

		string[] gpuIds = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getGpu.sh", "gpu_ids").StandardOutput.TrimEnd('\n').Split('\n');

		if (gpuIds.Length == 0) return [];

		var gpuDataList = new List<GpuData>();

		foreach (var gpuId in gpuIds)
		{
			// Format should be:
			// "GPU Brand|GPU Name|Graphics Util Perc|VRAM Util Perc?|VRAM Total Bytes?|VRAM Used Bytes?|Encoder Util|Decoder Util?|Video Enhance Util?|Graphics Frequency|Encoder/Decoder Frequency?|Power Usage|TemperatureC?"

			var shellScriptProcess = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getGpu.sh", $"All {gpuId}");
			string rawOutput = shellScriptProcess.StandardOutput.TrimEnd('|');
			string[] arrayOutput = rawOutput.Split('|');

			if (arrayOutput[1] == "null")
			{
				gpuDataList.Add(new GpuData
				{
					Brand = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getGpu.sh", $"gpu_brand {gpuId}").StandardOutput.TrimEnd('\n'),
					PciId = gpuId,
					IsMissing = true
				});
				continue;
			}

			if (arrayOutput[0] == "AMD")
			{ // Only runs for AMD, I'd do this in the bash script, but I'd rather eat a fork than do more arithmetic in bash
				arrayOutput[3] = $"{float.Parse(arrayOutput[5]) / float.Parse(arrayOutput[4]) * 100}";
			}

			try
			{
				gpuDataList.Add(new GpuData
				{
					Brand = arrayOutput[0],
					Name = arrayOutput[1].Trim('"'),
					PciId = gpuId,
					IsMissing = false,

					GraphicsUtilPerc = ParseFloatNullable(arrayOutput[2]),

					VramUtilPerc = ParseFloatNullable(arrayOutput[3]),
					VramTotalBytes = ParseFloatNullable(arrayOutput[4]),
					VramUsedBytes = ParseFloatNullable(arrayOutput[5]),

					EncoderUtilPerc = ParseFloatNullable(arrayOutput[6]),
					DecoderUtilPerc = ParseFloatNullable(arrayOutput[7]),
					VideoEnhanceUtilPerc = ParseFloatNullable(arrayOutput[8]),

					GraphicsFrequency = ParseFloatNullable(arrayOutput[9]),
					EncDecFrequency = ParseFloatNullable(arrayOutput[10]),

					PowerUse = ParseFloatNullable(arrayOutput[11]),
					TemperatureC = ParseSByteNullable(arrayOutput[12])
				});
			}
			catch (IndexOutOfRangeException e)
			{
				Console.WriteLine($"""
				                   IndexOutOfRange error while parsing data for GPU '{gpuId}'!
				                   {e.Message}
				                   Stack Trace: {e.StackTrace}
				                   Shell exit code: {shellScriptProcess.ExitCode}
				                   Full Shell log was saved in "{ApiConfig.BaseExecutablePath}/logs/GPU.log"
				                   
				                   For now, skipping this GPU...
				                   """);
			}
		}

		return gpuDataList;
	}

	private static float? ParseFloatNullable(string value)
	{
		return float.TryParse(value, out var result) ? result : null;
	}

	private static sbyte? ParseSByteNullable(string value)
	{
		return sbyte.TryParse(value, out var result) ? result : null;
	}

}