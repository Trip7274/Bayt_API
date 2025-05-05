namespace Bayt_API;

public static class DiskHandling
{
	public class DiskData
	{
		public string? DeviceName { get; set; }

		public required string MountPoint { get; init; }
		public required string MountName { get; init; }
		public required string DevicePath { get; init; }
		public required string FileSystem { get; init; }

		public bool IsRemovable { get; init; }
		public bool IsMissing { get; init; }


		public ulong TotalSize { get; init; }
		public ulong FreeSize { get; init; }
		public ulong UsedSize => TotalSize - FreeSize;
		public byte UsedSizePercent => (byte) ((float) UsedSize / TotalSize * 100);

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
	        if (diskData.IsMissing) continue;

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
		if (oldDiskDatas is not null && Caching.IsDataStale())
		{
			return oldDiskDatas;
		}

		List<DiskData> diskDataList = [];

		foreach (var mountPoint in mountPoints)
		{
			if (!Directory.Exists(mountPoint.Key))
			{
				diskDataList.Add(new DiskData
				{
					MountPoint = mountPoint.Key,
					MountName = mountPoint.Value,
					DevicePath = "???",
					IsMissing = true,
					FileSystem = "???",
					DeviceName = null,
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

			string devicePath = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getDisk.sh", $"{mountPoint.Key} Device.Path").StandardOutput.TrimEnd('\n');
			string fileSystem = ShellMethods.RunShell($"{ApiConfig.BaseExecutablePath}/scripts/getDisk.sh", $"{devicePath} Device.Filesystem").StandardOutput.TrimEnd('\n');

			var newDriveInfo = new DriveInfo(mountPoint.Key);

			diskDataList.Add(new DiskData
			{
				MountPoint = mountPoint.Key,
				MountName = mountPoint.Value,
				DevicePath = devicePath,
				FileSystem = fileSystem,
				IsRemovable = false, // TODO: Actually check for this
				IsMissing = false,
				TotalSize = (ulong) newDriveInfo.TotalSize,
				FreeSize = (ulong) newDriveInfo.AvailableFreeSpace
			});
		}

		GetDiskTemperature(ref diskDataList);

		return diskDataList;
	}
}