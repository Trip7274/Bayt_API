using System.Text.RegularExpressions;

namespace Bayt_API;

public static partial class DiskHandling
{
	public class DiskData
	{
		public string DeviceName { get; set; } = "???";

		public required string MountPoint { get; init; }
		public required string MountName { get; init; }
		public string? DevicePath { get; init; }
		public string? FileSystem { get; init; }

		public bool IsMissing { get; init; }


		public ulong? TotalSize { get; init; }
		public ulong? FreeSize { get; init; }
		public ulong? UsedSize => TotalSize - FreeSize;
		public byte? UsedSizePercent => (byte?) ((float?) UsedSize / TotalSize * 100);

		public string? TemperatureLabel { get; set; }
		public float? TemperatureC { get; set; }
		public float? TemperatureMinC { get; set; }
		public float? TemperatureMaxC { get; set; }
		public float? TemperatureCritC { get; set; }
	}

	private static void GetDiskTemperature(ref List<DiskData> diskDataList)
	{
        ArgumentNullException.ThrowIfNull(diskDataList);

        foreach (var diskData in diskDataList)
        {
	        if (diskData.IsMissing || diskData.DevicePath is null) continue;

	        foreach (string hwmonDir in Directory.EnumerateDirectories("/sys/class/hwmon/"))
	        {
		        // Check if the current `hwmon` directory is for the same device as what we're looking for.
		        if (!Directory.Exists($"{hwmonDir}/device/{Path.GetFileNameWithoutExtension(diskData.DevicePath).Split('p')[0]}"))
		        {
					continue;
		        }

				if (File.Exists($"{hwmonDir}/device/model"))
		        {
			        diskData.DeviceName = File.ReadAllText($"{hwmonDir}/device/model").TrimEnd('\n').TrimEnd(' ');
		        }
		        else if (File.Exists($"{hwmonDir}/name"))
		        {
			        diskData.DeviceName = File.ReadAllText($"{hwmonDir}/name").TrimEnd('\n').TrimEnd(' ');
		        }

		        if (File.Exists($"{hwmonDir}/temp1_input"))
		        {
			        diskData.TemperatureC = float.Parse(File.ReadAllText($"{hwmonDir}/temp1_input")) / 1000;
		        }

		        if (File.Exists($"{hwmonDir}/temp1_min"))
		        {
			        diskData.TemperatureMinC = float.Parse(File.ReadAllText($"{hwmonDir}/temp1_min")) / 1000;
		        }

		        if (File.Exists($"{hwmonDir}/temp1_max"))
		        {
			        diskData.TemperatureMaxC = float.Parse(File.ReadAllText($"{hwmonDir}/temp1_max")) / 1000;
		        }

		        if (File.Exists($"{hwmonDir}/temp1_crit"))
		        {
			        diskData.TemperatureCritC = float.Parse(File.ReadAllText($"{hwmonDir}/temp1_crit")) / 1000;
		        }

		        if (File.Exists($"{hwmonDir}/temp1_label"))
		        {
			        diskData.TemperatureLabel = File.ReadAllText($"{hwmonDir}/temp1_label").TrimEnd('\n').TrimEnd(' ');
		        }
	        }
        }
	}

	public static List<DiskData> GetDiskDatas(Dictionary<string, string> mountPoints, List<DiskData>? oldDiskDatas = null)
	{
		if (oldDiskDatas is not null && Caching.IsDataFresh())
		{
			return oldDiskDatas;
		}

		string[] scriptSupports = ShellMethods.GetScriptSupports($"{ApiConfig.BaseExecutablePath}/scripts/getDisk.sh");

		List<DiskData> diskDataList = [];

		foreach (var mountPoint in mountPoints)
		{
			if (!Directory.Exists(mountPoint.Key))
			{
				diskDataList.Add(new DiskData
				{
					DeviceName = "???",
					MountPoint = mountPoint.Key,
					MountName = mountPoint.Value,
					IsMissing = true,
					DevicePath = "???",
					FileSystem = "???",
					TemperatureLabel = null,
					TemperatureC = null,
					TemperatureMinC = null,
					TemperatureMaxC = null,
					TemperatureCritC = null,
					TotalSize = 0,
					FreeSize = 0
				});
				continue;
			}

			diskDataList.Add(new DiskData
			{
				MountPoint = mountPoint.Key,
				MountName = mountPoint.Value,
				DevicePath = GetStat("Device.Path", scriptSupports, mountPoint.Key),
				FileSystem = GetStat("Device.Filesystem", scriptSupports, mountPoint.Key),
				IsMissing = false,
				TotalSize = ulong.Parse(GetStat("Partition.TotalSpace", scriptSupports, mountPoint.Key)),
				FreeSize = ulong.Parse(GetStat("Parition.FreeSpace", scriptSupports, mountPoint.Key))
			});
		}

		GetDiskTemperature(ref diskDataList);

		return diskDataList;
	}

	private static string GetStat(string statName, string[] scriptSupports, string devicePath)
	{
		if (scriptSupports.Contains(statName))
		{
			string scriptPath = $"{ApiConfig.BaseExecutablePath}/scripts/getDisk.sh";
			var shellProcess = ShellMethods.RunShell(scriptPath, $"{statName} {devicePath}");
			if (!shellProcess.Success)
			{
				throw new Exception($"Error while running '{scriptPath} {statName}'! (code: {shellProcess.ExitCode})");
			}
			return shellProcess.StandardOutput;
		}

		return statName switch
		{
			"Device.Path" => GetDevicePath(devicePath),
			"Device.Filesystem" => GetDeviceFileSystem(devicePath),
			"Partition.TotalSpace" => new DriveInfo(devicePath).TotalSize.ToString(),
			"Parition.FreeSpace" => new DriveInfo(devicePath).AvailableFreeSpace.ToString(),
			_ => throw new Exception($"Unsupported stat '{statName}'!")
		};
	}

	private static string GetDevicePath(string mountPoint)
	{
		var regexMatch = DevicePathAndFileSystemRegex()
			.Match(ShellMethods.RunShell("df", $"{mountPoint} -T").StandardOutput);

		if (regexMatch.Groups.Count != 3) throw new Exception($"Error while parsing device path for '{mountPoint}'!");

		// A bit of a hack, but the GetDeviceFileSystem() function wouldn't work if its input was "devtmpfs".
		return regexMatch.Groups[1].Value == "devtmpfs" ? mountPoint : regexMatch.Groups[1].Value;
	}

	private static string GetDeviceFileSystem(string mountPoint)
	{
		var regexMatch = DevicePathAndFileSystemRegex()
			.Match(ShellMethods.RunShell("df", $"{mountPoint} -T").StandardOutput);

		if (regexMatch.Groups.Count != 3) throw new Exception($"Error while parsing device path for '{mountPoint}'!");

		return regexMatch.Groups[2].Value == "" ? "???" : regexMatch.Groups[2].Value;
	}

    [GeneratedRegex(@"(/dev/\S+|devtmpfs)\s+(\S+)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex DevicePathAndFileSystemRegex();
}