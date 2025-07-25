using System.Net;
using System.Net.Sockets;

namespace Bayt_API;

public static class StatsApi
{

	public class GeneralSpecs
	{
		public required string HostName { get; init; }
		public required string DistroName { get; init; }
		public required string KernelName { get; init; }
		public required string KernelVersion { get; init; }
		public required string KernelArch { get; init; }
	}

	public static GeneralSpecs GetGeneralSpecs(GeneralSpecs? oldGeneralSpecs = null)
	{
		if (oldGeneralSpecs is not null)
		{
			return oldGeneralSpecs;
		}

		return new GeneralSpecs
		{
			HostName = ShellMethods.RunShell("uname", "-n").StandardOutput,
			DistroName = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getSys.sh", "Distro.Name").StandardOutput,
			KernelName = ShellMethods.RunShell("uname", "-s").StandardOutput,
			KernelVersion = ShellMethods.RunShell("uname", "-r").StandardOutput,
			KernelArch = ShellMethods.RunShell("uname", "-m").StandardOutput
		};
	}
	
	
	public class CpuData
	{
		public static readonly string Name = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getCpu.sh", "Name").StandardOutput;

		public float UtilizationPerc { get; init; }

		public ushort PhysicalCoreCount { get; init; }
		public ushort ThreadCount { get; init; }
	}

	public static CpuData GetCpuData(CpuData? olCpuData = null)
	{
		if (olCpuData is not null && Caching.IsDataFresh())
		{
			return olCpuData;
		}

		var rawOutput = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getCpu.sh", "AllUtil");
		if (!rawOutput.Success)
		{
			throw new Exception($"Failed to get cpu data from getCpu.sh ({rawOutput.ExitCode})");
		}

		string[] outputArray = rawOutput.StandardOutput.Split('|');

		if (outputArray.Length != 3)
		{
			throw new Exception($"Invalid output from getCpu.sh (Expected 3 entries, got {outputArray.Length}.) ");
		}

		return new CpuData
		{
			UtilizationPerc = (float) Math.Round(float.Parse(outputArray[0]), 2),
			PhysicalCoreCount = ushort.Parse(outputArray[1]),
			ThreadCount = ushort.Parse(outputArray[2])
		};
	}
	

	public class MemoryData
	{
		public ulong TotalMemory { get; init; }
		public ulong UsedMemory { get; init; }
		public ulong AvailableMemory { get; init; }

		public byte UsedMemoryPercent => (byte) ((float) UsedMemory / TotalMemory * 100);
	}

	public static MemoryData GetMemoryData(MemoryData? oldMemoryData = null)
	{
		if (oldMemoryData is not null && Caching.IsDataFresh())
		{
			return oldMemoryData;
		}

		var rawOutput = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getMem.sh", "All").StandardOutput.Split('|');

		if (rawOutput.Length != 3)
		{
			throw new Exception("Invalid output from getMem.sh");
		}

		return new MemoryData
		{
			TotalMemory = ulong.Parse(rawOutput[0]),
			UsedMemory = ulong.Parse(rawOutput[1]),
			AvailableMemory = ulong.Parse(rawOutput[2])
		};
	}

	public static IPAddress GetLocalIpAddress()
	{
		using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
		socket.Connect("1.1.1.1", 65530);
		var endPoint = socket.LocalEndPoint as IPEndPoint ?? IPEndPoint.Parse(ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getNet.sh", "LocalAddress").StandardOutput);
		var localIp = endPoint.Address;

		return localIp;
	}
}