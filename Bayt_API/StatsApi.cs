namespace Bayt_API;

public static class StatsApi
{

	public class GeneralSpecs
	{
		public required string Hostname { get; set; }
		public required string KernelName { get; set; }
		public required string KernelVersion { get; set; }
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
			KernelName = ShellMethods.RunShell("uname", "-s").StandardOutput,
			KernelVersion = ShellMethods.RunShell("uname", "-r").StandardOutput
		};
	}
	
	
	public class CpuData
	{
		public static readonly string Name = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getCpu.sh", "Name").StandardOutput;

		public float UtilizationPerc { get; set; }

		public ushort PhysicalCoreCount { get; init; }
		public ushort ThreadCount { get; init; }
	}
	

	public class MemoryData
	{
		public ulong TotalMemory { get; set; }
		public ulong UsedMemory { get; set; }


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
		double usedMem = double.Parse(ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getMem.sh", "Used").StandardOutput);

		return new MemoryData
		{
			TotalMemory = (ulong) Math.Round(totalMem * 1048576),
			UsedMemory = (ulong) Math.Round(usedMem * 1048576)
		};
	}
}