using System.Net;
using System.Net.Sockets;

namespace Bayt_API;

/// <summary>
/// Contains methods and classes for storing and fetching general system info, along with CPU and RAM stats.
/// </summary>
public static class StatsApi
{

	/// <summary>
	/// General specs include generally static and general info about the system, or info about the system that's not inherent to Bayt.
	/// Some of these *are* dynamic, but keep in mind that the constructor only runs on start-up.
	/// </summary>
	public static class GeneralSpecs
	{
		static GeneralSpecs()
		{
			HostName = Environment.MachineName;
			DistroName = ShellMethods
				.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getSys.sh", ["Distro.Name"]).Result.StandardOutput;
			KernelName = ShellMethods.RunShell("uname", ["-s"]).Result.StandardOutput;
			KernelVersion = ShellMethods.RunShell("uname", ["-r"]).Result.StandardOutput;
			KernelArch = ShellMethods.RunShell("uname", ["-m"]).Result.StandardOutput;
		}

		/// <summary>
		/// The system's hostname.
		/// </summary>
		public static string HostName { get; }
		/// <summary>
		/// The system's friendly distro name.
		/// </summary>
		public static string DistroName { get; }
		/// <summary>
		/// Name of the current kernel. Usually "Linux"
		/// </summary>
		public static string KernelName { get; }
		/// <summary>
		/// Version of the current kernel.
		/// </summary>
		public static string KernelVersion { get; }
		/// <summary>
		/// Architecture of the current kernel.
		/// </summary>
		public static string KernelArch { get; }

		/// <summary>
		/// The current system's uptime.
		/// </summary>
		public static TimeSpan SystemUptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

		public static Dictionary<string, dynamic> ToDictionary()
		{
			return new Dictionary<string, dynamic>
			{
				{ nameof(HostName), HostName },
				{ nameof(DistroName), DistroName },
				{ nameof(KernelName), KernelName },
				{ nameof(KernelVersion), KernelVersion },
				{ nameof(KernelArch), KernelArch },
				{ nameof(Docker.IsDockerAvailable), Docker.IsDockerAvailable },
				{ nameof(Docker.IsDockerComposeAvailable), Docker.IsDockerComposeAvailable },
				{ nameof(SystemUptime), SystemUptime }
			};
		}
	}

	/// <summary>
	/// Contains data about the system's current CPU. Make sure this is updated using <see cref="CpuData.UpdateData"/>
	/// </summary>
	public static class CpuData
	{
		static CpuData()
		{
			var rawOutput = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getCpu.sh", ["Name"]).Result;
			if (!rawOutput.IsSuccess)
			{
				throw new Exception($"Failed to get CPU name from getCpu.sh ({rawOutput.ExitCode}).");
			}
			Name = rawOutput.StandardOutput;
		}

		/// <summary>
		/// The CPU's friendly name. E.g., "AMD Ryzen 5 7600X 6-Core Processor"
		/// </summary>
		public static string? Name { get; }
		/// <summary>
		/// The average percentage of CPU utilization across all cores.
		/// </summary>
		public static float UtilizationPerc { get; private set; }

		/// <summary>
		/// The number of *physical* cores on the CPU
		/// </summary>
		public static ushort PhysicalCoreCount { get; private set; }
		/// <summary>
		/// The number of logical cores on the CPU. AKA, CPU threads
		/// </summary>
		public static ushort ThreadCount { get; private set; }

		/// <summary>
		/// The average CPU's temperature. Either the "Package id 0" on Intel or "Tctl" on AMD.
		/// </summary>
		public static float? TemperatureC { get; private set; }
		/// <summary>
		/// The source of the temperature reading. Used only internally to set <see cref="TemperatureC"/> more percisely.
		/// </summary>
		private static string? TemperatureType { get; set; }

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
		/// Check if the object's data is stale, if so, update it using <see cref="UpdateData"/>.
		/// </summary>
		public static async Task UpdateDataIfNecessary()
		{
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "CPU Fetch", "Checking for CPU data update..."));
			if (!ShouldUpdate) return;
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "CPU Fetch", "Updating CPU data..."));

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
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "CPU Fetch", "CPU data updated."));
		}

		/// <summary>
		/// Updates the cached CPU data stored in this CpuData class with the latest metrics.
		/// Make sure to invoke this as to not serve stale data.
		/// </summary>
		/// <exception cref="Exception">Non-zero shell script exit code, or the output was of invalid length.</exception>
		public static async Task UpdateData()
		{
			int shellTimeout = 2500;
			if (LastUpdate == DateTime.MinValue)
			{
				shellTimeout *= 10;
			}
			var rawOutput = await ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getCpu.sh", ["AllUtil"], shellTimeout);
			if (!rawOutput.IsSuccess)
			{
				throw new Exception($"Failed to get CPU data from getCpu.sh ({rawOutput.ExitCode}).");
			}

			string[] outputArray = rawOutput.StandardOutput.Split('|');

			if (outputArray.Length != 5)
			{
				throw new Exception($"Invalid output from getCpu.sh (Expected 5 entries, got {outputArray.Length}).");
			}

			var temp = ParsingMethods.ParseTypeNullable<float>(outputArray[4]);
			if (outputArray[3] == "thermalZone")
			{
				temp /= 1000;
			}
			if (outputArray[3] != "null")
			{
				temp = MathF.Round(temp ?? 0, 2);
			}

			UtilizationPerc = MathF.Round(float.Parse(outputArray[0]), 2);
			PhysicalCoreCount = ushort.Parse(outputArray[1]);
			ThreadCount = ushort.Parse(outputArray[2]);
			TemperatureType = outputArray[3] == "null" ? null : outputArray[3];
			TemperatureC = temp;
			LastUpdate = DateTime.Now;
		}

		/// <summary>
		/// Get this object in the form of a Dictionary. Used for forming responses.
		/// </summary>
		/// <returns>This object represented as a Dictionary.</returns>
		public static Dictionary<string, dynamic?> ToDictionary()
		{
			return new Dictionary<string, dynamic?>
			{
				{ nameof(Name), Name },
				{ nameof(UtilizationPerc), UtilizationPerc },
				{ nameof(PhysicalCoreCount), PhysicalCoreCount },
				{ nameof(ThreadCount), ThreadCount },
				{ nameof(TemperatureC), TemperatureC },
				{ nameof(LastUpdate), LastUpdate.ToUniversalTime() }
			};
		}
	}

	/// <summary>
	/// Contains data about the system's current RAM, such as free/used bytes. Make sure this is updated using <see cref="MemoryData.UpdateData"/>
	/// </summary>
	public static class MemoryData
	{
		/// <summary>
		/// Total system memory (RAM) in bytes.
		/// </summary>
		public static ulong TotalMemory { get; private set; }
		/// <summary>
		/// Used system memory (RAM) in bytes.
		/// </summary>
		public static ulong UsedMemory { get; private set; }
		/// <summary>
		/// Available system memory (RAM) in bytes.
		/// </summary>
		public static ulong AvailableMemory { get; private set; }

		/// <summary>
		/// Gets the percentage of used system memory (RAM).
		/// </summary>
		public static float UsedMemoryPercent => TotalMemory == 0 ? 0 : MathF.Round((float) UsedMemory / TotalMemory * 100, 2);

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
		/// Check if the object's data is stale, if so, update it using <see cref="UpdateData"/>.
		/// </summary>
		public static async Task UpdateDataIfNecessary()
		{
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "RAM Fetch", "Checking for RAM data update..."));
			if (!ShouldUpdate) return;
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "RAM Fetch", "Updating RAM data..."));

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
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "RAM Fetch", "RAM data updated."));
		}

		/// <summary>
		/// Updates the RAM data stored in this MemoryData class with the latest metrics.
		/// Make sure to invoke this as to not serve stale data.
		/// </summary>
		/// <exception cref="Exception">Non-zero shell script exit code, or the output was of invalid length.</exception>
		public static async Task UpdateData()
		{
			int shellTimeout = 2500;
			if (LastUpdate == DateTime.MinValue)
			{
				shellTimeout *= 10;
			}

			var rawOutput = await ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getMem.sh", ["All"], shellTimeout);
			if (!rawOutput.IsSuccess)
			{
				throw new Exception($"Failed to get RAM data from getCpu.sh ({rawOutput.ExitCode})");
			}

			string[] outputArray = rawOutput.StandardOutput.Split('|');
			if (outputArray.Length != 3)
			{
				throw new Exception($"Invalid output length from getMem.sh (Expected 3 entries, got {outputArray.Length}).");
			}

			TotalMemory = ulong.Parse(outputArray[0]);
			UsedMemory = ulong.Parse(outputArray[1]);
			AvailableMemory = ulong.Parse(outputArray[2]);
			LastUpdate = DateTime.Now;
		}

		/// <summary>
		/// Fetch this MemoryData in the form of a Dictionary. Useful for outputting.
		/// </summary>
		/// <returns>This MemoryData as a Dictionary.</returns>
		public static Dictionary<string, dynamic> ToDictionary()
		{
			return new Dictionary<string, dynamic>
			{
				{ nameof(TotalMemory), TotalMemory },
				{ nameof(UsedMemory), UsedMemory },
				{ nameof(AvailableMemory), AvailableMemory },
				{ nameof(UsedMemoryPercent), UsedMemoryPercent },
				{ nameof(LastUpdate), LastUpdate.ToUniversalTime() }
			};
		}
	}

	public static IPAddress GetLocalIpAddress()
	{
		IPAddress localIp;

		if (Environment.GetEnvironmentVariable("BAYT_LOCALIP") != null)
		{
			if (IPAddress.TryParse(Environment.GetEnvironmentVariable("BAYT_LOCALIP"), out var localIpParsed))
			{
				localIp = localIpParsed;
				Console.WriteLine($"[INFO] Using BAYT_LOCALIP environment variable to override detected IP address: '{localIp}'");
				return localIp;
			}

			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"[WARNING] BAYT_LOCALIP environment variable is set to '{Environment.GetEnvironmentVariable("BAYT_LOCALIP")}', but it doesn't appear to be a valid IP address. Falling back to default selection.");
			Console.ResetColor();
		}

		using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
		socket.Connect("1.1.1.1", 65530);
		var endPoint = socket.LocalEndPoint as IPEndPoint ?? IPEndPoint.Parse(ShellMethods.RunShell(
			$"{ApiConfig.BaseExecutablePath}/scripts/getNet.sh", ["LocalAddress"]).Result.StandardOutput);
		localIp = endPoint.Address;

		return localIp;
	}
}