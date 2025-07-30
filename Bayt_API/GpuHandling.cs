using System.Globalization;

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
		/// Whether the GPU is considered an APU or dGPU. Purely cosmetic
		/// </summary>
		/// <remarks>
		///	Currently AMD-only
		/// </remarks>
		public bool? IsDedicated { get; init; }
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
		/// GTT (Graphics Translation Tables) utilization. Different from <c>VramUtilPerc</c>
		/// </summary>
		/// <remarks>
		///	Currently AMD only
		/// </remarks>
		public sbyte? VramGttUtilPerc { get; init; }

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
		/// <summary>
		/// Average temperature of the GPU die. Unit is in Celsius.
		/// </summary>
		/// <remarks>
		///	Currently NVIDIA + AMD only
		/// </remarks>
		public sbyte? TemperatureC { get; init; }
		/// <summary>
		/// Average fan speed in RPM.
		/// </summary>
		/// <remarks>
		///	AMD only.
		/// </remarks>
		public ushort? FanSpeedRpm { get; init; } // AMD-only
	}

	public static List<GpuData> GetGpuDataList()
	{
		string[] gpuIds = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getGpu.sh", "gpu_ids").StandardOutput.TrimEnd('\n').Split('\n');

		if (gpuIds.Length == 0) return [];

		var gpuDataList = new List<GpuData>();

		foreach (var gpuId in gpuIds)
		{
			// Format should be:
			// "GPU Brand|GPU Name|IsGpuDedicated?|Graphics Util Perc|Graphics Frequency|VRAM Util Perc?|VRAM Total Bytes?|VRAM Used Bytes?|VRAM GTT Usage Perc?|Encoder Util|Decoder Util?|Video Enhance Util?|Encoder/Decoder Frequency?|Power Usage|TemperatureC?|FanSpeedRPM?"

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

			if (arrayOutput[5] == "null" && arrayOutput[6] != "null" && arrayOutput[7] != "null")
			{
				// Workaround for the AMD GPU interface not providing a VRAM usage percentage
				arrayOutput[5] = $"{Math.Round(float.Parse(arrayOutput[7]) / float.Parse(arrayOutput[6]) * 100, 2)}";
			}

			try
			{
				gpuDataList.Add(new GpuData
				{
					Brand = arrayOutput[0],
					Name = arrayOutput[1].Trim('"'),
					IsDedicated = ParsingMethods.ParseTypeNullable<bool>(arrayOutput[2]),
					IsMissing = false,

					GraphicsUtilPerc = ParsingMethods.ParseTypeNullable<float>(arrayOutput[3]),
					GraphicsFrequency = ParsingMethods.ParseTypeNullable<float>(arrayOutput[4]),

					VramUtilPerc = ParsingMethods.ParseTypeNullable<float>(arrayOutput[5]),
					VramTotalBytes = ParsingMethods.ParseTypeNullable<ulong>(arrayOutput[6]),
					VramUsedBytes = ParsingMethods.ParseTypeNullable<ulong>(arrayOutput[7]),
					VramGttUtilPerc = ParsingMethods.ParseTypeNullable<sbyte>(arrayOutput[8]),

					EncoderUtilPerc = ParsingMethods.ParseTypeNullable<float>(arrayOutput[9]),
					DecoderUtilPerc = ParsingMethods.ParseTypeNullable<float>(arrayOutput[10]),
					VideoEnhanceUtilPerc = ParsingMethods.ParseTypeNullable<float>(arrayOutput[11]),
					EncDecFrequency = ParsingMethods.ParseTypeNullable<float>(arrayOutput[12]),

					PowerUse = ParsingMethods.ParseTypeNullable<float>(arrayOutput[13]),
					TemperatureC = ParsingMethods.ParseTypeNullable<sbyte>(arrayOutput[14]),
					FanSpeedRpm = ParsingMethods.ParseTypeNullable<ushort>(arrayOutput[15])
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
}