namespace Bayt_API;

public static class GpuHandling
{
	public class GpuData
	{
		public string? Name { get; init; }
		public required string Brand { get; init; }
		public bool IsMissing { get; init; }

		public float? GraphicsUtilPerc { get; init; }
		public float? VramUtilPerc { get; init; } // NVIDIA + AMD only
		public ulong? VramTotalBytes { get; init; } // AMD + NVIDIA only
		public ulong? VramUsedBytes { get; init; } // AMD + NVIDIA only

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
		if (oldGpuData is not null && Caching.IsDataFresh())
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
			string[] arrayOutput = shellScriptProcess.StandardOutput.TrimEnd('|').Split('|');

			if (arrayOutput[1] == "null")
			{
				gpuDataList.Add(new GpuData
				{
					Brand = arrayOutput[0],
					IsMissing = true
				});
				continue;
			}

			if (arrayOutput[0] == "Virtio")
			{
				gpuDataList.Add(new GpuData
				{
					Brand = arrayOutput[0],
					Name = arrayOutput[1].Trim('"'),
					IsMissing = false
				});
				continue;
			}

			if (arrayOutput[3] == "null" && arrayOutput[4] != "null" && arrayOutput[5] != "null")
			{
				arrayOutput[3] = $"{float.Parse(arrayOutput[5]) / float.Parse(arrayOutput[4]) * 100}";
			}

			try
			{
				gpuDataList.Add(new GpuData
				{
					Name = arrayOutput[1].Trim('"'),
					Brand = arrayOutput[0],
					IsMissing = false,

					GraphicsUtilPerc = ParseFloatNullable(arrayOutput[2]),

					VramUtilPerc = ParseFloatNullable(arrayOutput[3]),
					VramTotalBytes = ParseUlongNullable(arrayOutput[4]),
					VramUsedBytes = ParseUlongNullable(arrayOutput[5]),

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

	private static ulong? ParseUlongNullable(string value)
	{
		return ulong.TryParse(value, out var result) ? result : null;
	}

	private static sbyte? ParseSByteNullable(string value)
	{
		return sbyte.TryParse(value, out var result) ? result : null;
	}

}