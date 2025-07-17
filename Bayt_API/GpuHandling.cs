namespace Bayt_API;

public static class GpuHandling
{
	public class GpuData
	{
		/// <summary>
		/// Friendly name of the GPU (e.g. "NVIDIA RTX 5070 Ti" or "AMD Radeon RX 9070 XT")
		/// </summary>
		public string? Name { get; init; }
		/// <summary>
		/// One of: "NVIDIA", "AMD", "Intel", or "Virtio". Used to differenciate what features should be expected from every brand.
		/// </summary>
		/// <remarks>
		///	"Virtio" is always displayed with minimal, usually generic stats (Brand, Name, Not missing)
		/// </remarks>
		public required string Brand { get; init; }
		/// <summary>
		/// This is set to true by default if the <c>Name</c> field is (literally) "null". Used to indicate this GPU should be skipped from processing and will not contain other data.
		/// </summary>
		public bool IsMissing { get; init; }

		/// <summary>
		/// Current utilization percentage of the graphics core in the GPU. Preferrably up to 2 decimals in percision
		/// </summary>
		public float? GraphicsUtilPerc { get; init; }
		/// <summary>
		/// Current operating frequency of the graphics core. Unit is MHz.
		/// </summary>
		public float? GraphicsFrequency { get; init; }

		/// <summary>
		/// Utilization percentage of VRAM space. Perferrably up to 2 decimals in percision.
		/// </summary>
		/// <remarks>
		///	 Currently NVIDIA + AMD only
		/// </remarks>
		public float? VramUtilPerc { get; init; }
		/// <summary>
		///	Total size of VRAM space. Unit is Bytes.
		/// </summary>
		/// <remarks>
		///	Currently AMD + NVIDIA only
		/// </remarks>
		public ulong? VramTotalBytes { get; init; }
		/// <summary>
		///	Number of bytes used in VRAM space.
		/// </summary>
		/// <remarks>
		///	Currently AMD + NVIDIA only
		/// </remarks>
		public ulong? VramUsedBytes { get; init; }

		/// <summary>
		/// Utilization percentage of the encoder engine.
		/// </summary>
		/// <remarks>
		///	On NVIDIA, this is purely encode utilization, on Intel+AMD, it's the average of Encode+Decode. This is due to limitations in the current interface for both.
		/// </remarks>
		public float? EncoderUtilPerc { get; init; }
		/// <summary>
		/// Utilization percentage of the decoding engine.
		/// </summary>
		/// <remarks>
		///	Currently NVIDIA only, where it's purely decode utilization.
		/// </remarks>
		public float? DecoderUtilPerc { get; init; }
		/// <summary>
		/// Utilization of the "VideoEnhance" Engine on Intel GPUs.
		/// </summary>
		/// <remarks>
		///	Currently Intel only, Jellyfin docs describe it as "QSV VPP processor workload".
		/// </remarks>
		public float? VideoEnhanceUtilPerc { get; init; }

		/// <summary>
		/// Frequency of the encoding and decoding engines. Unit is MHz.
		/// </summary>
		/// <remarks>
		///	Currently NVIDIA only.
		/// </remarks>
		public float? EncDecFrequency { get; init; }

		/// <summary>
		/// Average power usage of the whole GPU device. Unit is Watts.
		/// </summary>
		public float? PowerUse { get; init; }
		public sbyte? TemperatureC { get; init; } // NVIDIA + AMD only
		/// <summary>
		/// Average temperature of the GPU die. Unit is in Celsius.
		/// </summary>
		/// <remarks>
		///	Currently NVIDIA + AMD only
		/// </remarks>
		public sbyte? TemperatureC { get; init; }
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