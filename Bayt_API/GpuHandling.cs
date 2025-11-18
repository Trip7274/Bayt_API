namespace Bayt_API;

public static class GpuHandling
{
	public sealed class GpuData
	{
		public GpuData(string gpuId)
		{
			GpuId = gpuId;
			UpdateData();
		}

		public string GpuId { get; }

		/// <summary>
		/// Friendly name of the GPU (e.g. "NVIDIA RTX 5070 Ti" or "AMD Radeon RX 9070 XT")
		/// </summary>
		public string? Name { get; private set; }
		/// <summary>
		/// One of: "NVIDIA", "AMD", "Intel", or "Virtio". Used to differenciate what features should be expected from every brand.
		/// </summary>
		/// <remarks>
		///	"Virtio" is always displayed with minimal, usually generic stats (Brand, Name, Not missing)
		/// </remarks>
		public string? Brand { get; private set; }
		/// <summary>
		/// Whether the GPU is considered an APU or dGPU. Purely cosmetic
		/// </summary>
		/// <remarks>
		///	Currently AMD-only
		/// </remarks>
		public bool? IsDedicated { get; private set; }
		/// <summary>
		/// This is set to true by default if the <c>Name</c> field is (literally) "null". Used to indicate this GPU should be skipped from processing and will not contain other data.
		/// </summary>
		public bool IsMissing { get; private set; }

		/// <summary>
		/// Current utilization percentage of the graphics core in the GPU. Preferrably up to 2 decimals in percision
		/// </summary>
		public float? GraphicsUtilPerc { get; private set; }
		/// <summary>
		/// Current operating frequency of the graphics core. Unit is MHz.
		/// </summary>
		public float? GraphicsFrequency { get; private set; }

		/// <summary>
		/// Utilization percentage of VRAM space. Perferrably up to 2 decimals in percision.
		/// </summary>
		/// <remarks>
		///	 Currently NVIDIA + AMD only
		/// </remarks>
		public float? VramUtilPerc { get; private set; }
		/// <summary>
		///	Total size of VRAM space. Unit is Bytes.
		/// </summary>
		/// <remarks>
		///	Currently AMD + NVIDIA only
		/// </remarks>
		public ulong? VramTotalBytes { get; private set; }
		/// <summary>
		///	Number of bytes used in VRAM space.
		/// </summary>
		/// <remarks>
		///	Currently AMD + NVIDIA only
		/// </remarks>
		public ulong? VramUsedBytes { get; private set; }
		/// <summary>
		/// GTT (Graphics Translation Tables) utilization. Different from <c>VramUtilPerc</c>
		/// </summary>
		/// <remarks>
		///	Currently AMD only
		/// </remarks>
		public sbyte? VramGttUtilPerc { get; private set; }

		/// <summary>
		/// Utilization percentage of the encoder engine.
		/// </summary>
		/// <remarks>
		///	On NVIDIA, this is purely encode utilization, on Intel+AMD, it's the average of Encode+Decode. This is due to limitations in the current interface for both.
		/// </remarks>
		public float? EncoderUtilPerc { get; private set; }
		/// <summary>
		/// Utilization percentage of the decoding engine.
		/// </summary>
		/// <remarks>
		///	Currently NVIDIA only, where it's purely decode utilization.
		/// </remarks>
		public float? DecoderUtilPerc { get; private set; }
		/// <summary>
		/// Utilization of the "VideoEnhance" Engine on Intel GPUs.
		/// </summary>
		/// <remarks>
		///	Currently Intel only, Jellyfin docs describe it as "QSV VPP processor workload".
		/// </remarks>
		public float? VideoEnhanceUtilPerc { get; private set; }

		/// <summary>
		/// Frequency of the encoding and decoding engines. Unit is MHz.
		/// </summary>
		/// <remarks>
		///	Currently NVIDIA only.
		/// </remarks>
		public float? EncDecFrequency { get; private set; }

		/// <summary>
		/// Average power usage of the whole GPU device. Unit is Watts.
		/// </summary>
		/// <remarks>
		///	In the case of an AMD iGPU, this will be the power consumption of the entire CPU die.
		/// </remarks>
		public float? PowerUse { get; private set; }
		/// <summary>
		/// Average temperature of the GPU die. Unit is in Celsius.
		/// </summary>
		/// <remarks>
		///	Currently NVIDIA + AMD only
		/// </remarks>
		public sbyte? TemperatureC { get; private set; }
		/// <summary>
		/// Average fan speed in RPM.
		/// </summary>
		/// <remarks>
		///	AMD only.
		/// </remarks>
		public ushort? FanSpeedRpm { get; private set; }

		internal void UpdateData()
		{
			// Format should be:
			// "GPU Brand|GPU Name|IsGpuDedicated?|Graphics Util Perc|Graphics Frequency|VRAM Util Perc?|VRAM Total Bytes?|VRAM Used Bytes?|VRAM GTT Usage Perc?|Encoder Util|Decoder Util?|Video Enhance Util?|Encoder/Decoder Frequency?|Power Usage|TemperatureC?|FanSpeedRPM?"

			var shellTimeout = TimeSpan.FromMilliseconds(2500);
			if (Name == null)
			{
				shellTimeout *= 10;
			}

			var shellScriptProcess =
					ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getGpu.sh", ["All", GpuId], shellTimeout).Result;

			string[] arrayOutput = shellScriptProcess.StandardOutput.TrimEnd('|').Split('|');
			if (arrayOutput.Length < 16)
			{
				Logs.LogStream.Write(new(StreamId.Error, "GPU Fetch",
					$"Error while parsing data for GPU '{GpuId}'! (Script exit code: {shellScriptProcess.ExitCode} Log: {ApiConfig.BaseExecutablePath}/logs/GPU.log)"));
				return;
			}

			if (arrayOutput[1] == "null")
			{
				Brand = arrayOutput[0];
				IsMissing = true;

				return;
			}

			if (arrayOutput[0] == "Virtio")
			{
				Brand = arrayOutput[0];
				Name = arrayOutput[1].Trim('"');
				IsMissing = false;

				return;
			}

			if (arrayOutput[5] == "null" && arrayOutput[6] != "null" && arrayOutput[7] != "null")
			{
				// Workaround for the AMD GPU interface not providing a VRAM usage percentage
				arrayOutput[5] =
					$"{MathF.Round(float.Parse(arrayOutput[7]) / float.Parse(arrayOutput[6]) * 100, 2)}";
			}

			Brand = arrayOutput[0];
			Name = arrayOutput[1].Trim('"');
			IsDedicated = arrayOutput[2].ParseNullable<bool>();
			IsMissing = false;

			GraphicsUtilPerc = arrayOutput[3].ParseNullable<float>();
			GraphicsFrequency = arrayOutput[4].ParseNullable<float>();

			VramUtilPerc = arrayOutput[5].ParseNullable<float>();
			VramTotalBytes = arrayOutput[6].ParseNullable<ulong>();
			VramUsedBytes = arrayOutput[7].ParseNullable<ulong>();
			VramGttUtilPerc = arrayOutput[8].ParseNullable<sbyte>();

			EncoderUtilPerc = arrayOutput[9].ParseNullable<float>();
			DecoderUtilPerc = arrayOutput[10].ParseNullable<float>();
			VideoEnhanceUtilPerc = arrayOutput[11].ParseNullable<float>();
			EncDecFrequency = arrayOutput[12].ParseNullable<float>();

			PowerUse = arrayOutput[13].ParseNullable<float>();
			TemperatureC = arrayOutput[14].ParseNullable<sbyte>();
			FanSpeedRpm = arrayOutput[15].ParseNullable<ushort>();
		}
	}

	/// <summary>
	/// Contains data about all the system's current GPUs. Make sure this is updated using <see cref="FullGpusData.UpdateData"/>
	/// </summary>
	public static class FullGpusData
	{
		static FullGpusData()
		{
			var gpuIdsProcess = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getGpu.sh", ["gpu_ids"]).Result;

			GpuIdList = gpuIdsProcess.StandardOutput.TrimEnd('\n').Split('\n');

			if (GpuIdList.Length == 0) return;

			foreach (var gpuId in GpuIdList)
			{
				GpuDataList.Add(new GpuData(gpuId));
			}
		}

		public static List<GpuData> GpuDataList { get; } = [];
		private static string[] GpuIdList { get; }
		/// <summary>
		/// The last time this object was updated.
		/// </summary>
		public static DateTime LastUpdate { get; private set; } = DateTime.MinValue;
		/// <summary>
		/// Returns whether the current data is too stale and should be updated.
		/// </summary>
		public static bool ShouldUpdate =>
			LastUpdate.AddSeconds(ApiConfig.ApiConfiguration.SecondsToUpdate) < DateTime.Now;

		private static Task? UpdatingTask { get; set; }
		private static readonly Lock UpdatingLock = new();

		/// <summary>
		/// Force-updates each entry in the <see cref="GpuDataList"/> property with the respective GPU's latest metrics.
		/// </summary>
		/// <remarks>
		///	It's recommended to use <see cref="UpdateDataIfNecessary"/> instead, to honor the user's cache preferences.
		/// </remarks>
		public static async Task UpdateData()
		{
			if (GpuIdList.Length == 0) return;

			List<Task> gpuTasks = [];
			gpuTasks.AddRange(GpuDataList.Select(gpuData => Task.Run(gpuData.UpdateData)));

			await Task.WhenAll(gpuTasks);
			LastUpdate = DateTime.Now;
		}

		/// <summary>
		/// Checks if all the <see cref="GpuData"/> objects are fresh, and if not, updates them with the latest metrics.
		/// Make sure to invoke this as to not serve stale data.
		/// </summary>
		public static async Task UpdateDataIfNecessary()
		{
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "GPU Fetch", "Checking for GPU data update..."));
			if (!ShouldUpdate) return;
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "GPU Fetch", "Updating GPU data..."));

			var localTask = UpdatingTask;
			if (localTask is null)
			{
				lock (UpdatingLock)
				{
					UpdatingTask ??= UpdateData();
					localTask = UpdatingTask;
				}
			}

			await localTask;
			lock (UpdatingLock)
			{
				UpdatingTask = null;
			}
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "GPU Fetch", "GPU data updated."));
		}

		public static Dictionary<string, dynamic?>[] ToDictionary()
		{
			if (GpuDataList.Count == 0) return [];

			List<Dictionary<string, dynamic?>> gpuDataDicts = [];

			foreach (var gpuData in GpuDataList)
			{
				if (gpuData.IsMissing)
				{
					gpuDataDicts.Add(new()
					{
						{ nameof(gpuData.Brand), gpuData.Brand },
						{ nameof(gpuData.IsMissing), gpuData.IsMissing },

						{ nameof(LastUpdate), LastUpdate.ToUniversalTime() }
					});
					continue;
				}


				gpuDataDicts.Add(new()
				{
					{ nameof(gpuData.Name), gpuData.Name },
					{ nameof(gpuData.Brand), gpuData.Brand },
					{ nameof(gpuData.IsDedicated), gpuData.IsDedicated },
					{ nameof(gpuData.IsMissing), gpuData.IsMissing },

					{ nameof(gpuData.GraphicsUtilPerc), gpuData.GraphicsUtilPerc },
					{ nameof(gpuData.GraphicsFrequency), gpuData.GraphicsFrequency },

					{ nameof(gpuData.VramUtilPerc), gpuData.VramUtilPerc },
					{ nameof(gpuData.VramTotalBytes), gpuData.VramTotalBytes },
					{ nameof(gpuData.VramUsedBytes), gpuData.VramUsedBytes },
					{ nameof(gpuData.VramGttUtilPerc), gpuData.VramGttUtilPerc },

					{ nameof(gpuData.EncoderUtilPerc), gpuData.EncoderUtilPerc },
					{ nameof(gpuData.DecoderUtilPerc), gpuData.DecoderUtilPerc },
					{ nameof(gpuData.VideoEnhanceUtilPerc), gpuData.VideoEnhanceUtilPerc },
					{ nameof(gpuData.EncDecFrequency), gpuData.EncDecFrequency },

					{ nameof(gpuData.PowerUse), gpuData.PowerUse },
					{ nameof(gpuData.TemperatureC), gpuData.TemperatureC },
					{ nameof(gpuData.FanSpeedRpm), gpuData.FanSpeedRpm },

					{ nameof(LastUpdate), LastUpdate.ToUniversalTime() }
				});
			}

			return gpuDataDicts.ToArray();
		}
	}
}