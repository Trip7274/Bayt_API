namespace Bayt_API;

public static class StatsApi
{

	public class GeneralSpecs
	{
		public required string Hostname { get; init; }
		public required string Distroname { get; init; }
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
			Hostname = ShellMethods.RunShell("uname", "-n").StandardOutput,
			Distroname = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getSys.sh", "Distro.Name").StandardOutput,
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
	

	public class MemoryData
	{
		public ulong TotalMemory { get; init; }
		public ulong UsedMemory { get; init; }


		public ulong AvailableMemory => TotalMemory - UsedMemory;
		public byte UsedMemoryPercent => (byte) ((float) UsedMemory / TotalMemory * 100);
	}
	
	public static CpuData GetCpuData(CpuData? olCpuData = null)
	{
		if (olCpuData is not null && Caching.IsDataStale())
		{
			return olCpuData;
		}

		return new CpuData
		{
			UtilizationPerc = float.Parse(ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getCpu.sh", "UtilPerc").StandardOutput),
			PhysicalCoreCount = ushort.Parse(ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getCpu.sh", "PhysicalCores").StandardOutput),
			ThreadCount = ushort.Parse(ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getCpu.sh", "ThreadCount").StandardOutput)
		};
	}

	public static MemoryData GetMemoryData(MemoryData? oldMemoryData = null)
	{
		if (oldMemoryData is not null && Caching.IsDataStale())
		{
			return oldMemoryData;
		}

		double totalMem = double.Parse(ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getMem.sh", "Total").StandardOutput);
		double usedMem = double.Parse(ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getMem.sh", "Used").StandardOutput); // TODO: Extract using `free`
                                                                                                                                    // for fewer bash executions

		return new MemoryData
		{
			TotalMemory = (ulong) Math.Round(totalMem * 1048576),
			UsedMemory = (ulong) Math.Round(usedMem * 1048576)
		};
	}
}