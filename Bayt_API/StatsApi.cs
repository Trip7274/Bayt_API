using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace Bayt_API;

/// <summary>
/// Contains methods and classes for storing and fetching general system info, along with CPU and RAM stats.
/// </summary>
public static class StatsApi
{

	/// <summary>
	/// General specs include generally very static and general info about the system.
	/// Essentially, if it takes at least a (system) restart to fully change it, and it's not inherent to Bayt, it's probably here.
	/// </summary>
	public static class GeneralSpecs
	{
		static GeneralSpecs()
		{
			HostName = ShellMethods.RunShell("uname", "-n").StandardOutput;
			DistroName = ShellMethods
				.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getSys.sh", "Distro.Name").StandardOutput;
			KernelName = ShellMethods.RunShell("uname", "-s").StandardOutput;
			KernelVersion = ShellMethods.RunShell("uname", "-r").StandardOutput;
			KernelArch = ShellMethods.RunShell("uname", "-m").StandardOutput;
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

		public static Dictionary<string, dynamic> ToDictionary()
		{
			return new Dictionary<string, dynamic>
			{
				{ "HostName", HostName },
				{ "DistroName", DistroName },
				{ "KernelName", KernelName },
				{ "KernelVersion", KernelVersion },
				{ "KernelArch", KernelArch }
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
			var rawOutput = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getCpu.sh", "Name");
			if (!rawOutput.Success)
			{
				throw new Exception($"Failed to get CPU name from getCpu.sh ({rawOutput.ExitCode}).");
			}
			CpuName = rawOutput.StandardOutput;

			UpdateData();
		}

		public static string? CpuName { get; }
		public static float UtilizationPerc { get; private set; }

		public static ushort PhysicalCoreCount { get; private set; }
		public static ushort ThreadCount { get; private set; }

		public static float? TemperatureC { get; private set; }
		public static string? TemperatureType { get; private set; }


		/// <summary>
		/// Updates the cached CPU data stored in this CpuData class with the latest metrics.
		/// Make sure to invoke this as to not serve stale data.
		/// </summary>
		/// <exception cref="Exception">Non-zero shell script exit code, or the output was of invalid length.</exception>
		public static void UpdateData()
		{
			int shellTimeout = 2500;
			if (CpuName == null)
			{
				shellTimeout *= 10;
			}
			var rawOutput = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getCpu.sh", "AllUtil", shellTimeout);
			if (!rawOutput.Success)
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
				temp = (float) Math.Round((decimal) (temp ?? 0), 2);
			}

			UtilizationPerc = (float) Math.Round(float.Parse(outputArray[0]), 2);
			PhysicalCoreCount = ushort.Parse(outputArray[1]);
			ThreadCount = ushort.Parse(outputArray[2]);
			TemperatureType = outputArray[3] == "null" ? null : outputArray[3];
			TemperatureC = temp;
		}

		public static Dictionary<string, dynamic?> ToDictionary()
		{
			return new Dictionary<string, dynamic?>
			{
				{ "Name", CpuName },
				{ "UtilPerc", UtilizationPerc },
				{ "CoreCount", PhysicalCoreCount },
				{ "ThreadCount", ThreadCount },
				{ "TemperatureC", TemperatureC },
				{ "TemperatureType", TemperatureType }
			};
		}
	}

	/// <summary>
	/// Contains data about the system's current RAM, such as free/used bytes. Make sure this is updated using <see cref="MemoryData.UpdateData"/>
	/// </summary>
	public static class MemoryData
	{
		static MemoryData()
		{
			UpdateData();
		}

		public static ulong TotalMemory { get; private set; }
		public static ulong UsedMemory { get; private set; }
		public static ulong AvailableMemory { get; private set; }

		public static byte UsedMemoryPercent => (byte) ((float) UsedMemory / TotalMemory * 100);
		private static bool _initialized = false;


		/// <summary>
		/// Updates the RAM data stored in this MemoryData class with the latest metrics.
		/// Make sure to invoke this as to not serve stale data.
		/// </summary>
		/// <exception cref="Exception">Non-zero shell script exit code, or the output was of invalid length.</exception>
		public static void UpdateData()
		{
			int shellTimeout = 2500;
			if (!_initialized)
			{
				shellTimeout *= 10;
			}

			var rawOutput = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getMem.sh", "All", shellTimeout);
			if (!rawOutput.Success)
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
			_initialized = true;
		}

		/// <summary>
		/// Fetch this MemoryData in the form of a Dictionary. Useful for outputting.
		/// </summary>
		/// <returns>This MemoryData as a Dictionary.</returns>
		public static Dictionary<string, dynamic> ToDictionary()
		{
			return new Dictionary<string, dynamic>
			{
				{ "TotalMemory", TotalMemory },
				{ "UsedMemory", UsedMemory },
				{ "AvailableMemory", AvailableMemory },
				{ "UsedMemoryPercent", UsedMemoryPercent }
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
		var endPoint = socket.LocalEndPoint as IPEndPoint ?? IPEndPoint.Parse(ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getNet.sh", "LocalAddress").StandardOutput);
		localIp = endPoint.Address;

		return localIp;
	}
}
