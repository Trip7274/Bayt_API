using System.Text.RegularExpressions;

namespace Bayt_API;
/// <summary>
/// Contains all the methods and classes to fetch stats about anything disk or filesystem related.
/// </summary>
public static partial class DiskHandling
{
	/// <summary>
	/// Represents disk-related data and properties for a specific mount point.
	/// </summary>
	public sealed class DiskData
	{
		/// <summary>
		/// Represents the retail device name (e.g., "Lexar SSD NM790 2TB").
		/// </summary>
		/// <remarks>
		///	Some disks may have incorrect names, but this only fetches what the manufacterer titled it.
		/// This defaults to null if the name was not found.
		/// </remarks>
		public string? DeviceName { get; set; }

		/// <summary>
		/// The user-provided mountpoint of this mount.
		/// </summary>
		/// <remarks>
		///	This'll be available even if the device is invalid/unavailable, as it's user-inputted.
		/// </remarks>
		public required string MountPoint { get; init; }
		/// <summary>
		/// The user-provided mount name. Defaults to "Mount" if none was provided.
		/// </summary>
		/// <remarks>
		///	This'll be available even if the device is invalid/unavailable, as it's user-inputted.
		/// </remarks>
		public required string MountName { get; init; }

		/// <summary>
		/// Represents the physical path to the associated device (e.g., "/dev/sda").
		/// </summary>
		/// <remarks>
		/// This can be null if the device is unavailable (unplugged, etc.).
		/// If the mountpoint is in a virtual filesystem ("/dev", "/sys", etc.), it'll be the same as the <see cref="MountPoint"/> property.
		/// </remarks>
		public string? DevicePath { get; init; }
		/// <summary>
		/// The filesystem of the associated mount. (e.g., "btrfs", "ext4")
		/// </summary>
		/// <remarks>
		///	Can be null if the mount was missing/invalid.
		/// </remarks>
		public string? FileSystem { get; init; }

		/// <summary>
		/// Whether the mount seems to be missing or invalid.
		/// </summary>
		/// <remarks>
		///	If this is true, then all the properties other than <see cref="MountPoint"/> and <see cref="MountName"/> will be null.
		/// </remarks>
		public bool IsMissing { get; init; }


		/// <summary>
		/// Total size of the mount. In bytes.
		/// </summary>
		public ulong? TotalSize { get; init; }
		/// <summary>
		/// The amount of available/free space in the mount. In bytes.
		/// </summary>
		public ulong? FreeSize { get; init; }
		/// <summary>
		/// Number of bytes used up in the mount.
		/// </summary>
		public ulong? UsedSize => TotalSize - FreeSize;
		/// <summary>
		/// Percentage of used space in the mount.
		/// </summary>
		public byte? UsedSizePercent => (byte?) ((float?) UsedSize / TotalSize * 100);

		/// <summary>
		/// Represents the label set on the specific temperature sensor by the manufacterer.
		/// </summary>
		/// <remarks>
		///	Temperature reporting is not as reliable as other metrics.
		/// If this is null, then expect the rest of the temperature data to also be null.
		/// </remarks>
		public string? TemperatureLabel { get; set; }
		/// <summary>
		/// Current temperature as reported by the sensor. In Celsius.
		/// </summary>
		public float? TemperatureC { get; set; }
		/// <summary>
		/// The minimum operating temperature as reported by the sensor. In Celsius.
		/// </summary>
		public float? TemperatureMinC { get; set; }
		/// <summary>
		/// The maximum operating temperature as reported by the sensor. In Celsius.
		/// </summary>
		public float? TemperatureMaxC { get; set; }
		/// <summary>
		/// The critical temperature threshold as reported by the sensor. In Celsius.
		/// </summary>
		public float? TemperatureCritC { get; set; }
	}

	/// <summary>
	/// "Fill in" the temperature and name data of a list of <see cref="DiskData"/> objects.
	/// </summary>
	/// <param name="diskDataList">A reference to the list of <see cref="DiskData"/> objects.</param>
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

	/// <summary>
	/// Fetch a list of all watched mounts' respective <see cref="DiskData"/>s. Loaded from the user's configuration.
	/// </summary>
	/// <returns>The list of watched mounts' <see cref="DiskData"/>s</returns>
	public static List<DiskData> GetDiskDatas()
	{

		string[] scriptSupports = ShellMethods.GetScriptSupports($"{ApiConfig.BaseExecutablePath}/scripts/getDisk.sh");

		List<DiskData> diskDataList = [];

		foreach (var mountPoint in ApiConfig.MainConfigs.ConfigProps.WatchedMounts)
		{
			if (!Directory.Exists(mountPoint.Key))
			{
				diskDataList.Add(new DiskData
				{
					MountPoint = mountPoint.Key,
					MountName = mountPoint.Value,
					IsMissing = true
				});
				continue;
			}

			diskDataList.Add(new DiskData
			{
				MountPoint = mountPoint.Key,
				MountName = mountPoint.Value,
				DevicePath = GetStat("Device.Path", mountPoint.Key, scriptSupports),
				FileSystem = GetStat("Device.Filesystem", mountPoint.Key, scriptSupports),
				IsMissing = false,
				TotalSize = ulong.Parse(GetStat("Partition.TotalSpace", mountPoint.Key, scriptSupports)),
				FreeSize = ulong.Parse(GetStat("Parition.FreeSpace", mountPoint.Key, scriptSupports))
			});
		}

		GetDiskTemperature(ref diskDataList);

		return diskDataList;
	}

	/// <summary>
	/// Internal helper method to check if the associated disk-fetching script can fetch a specific stat and defaulting to the C# implementation if not.
	/// </summary>
	/// <param name="statName">The specific stat you'd like to fetch. Check remarks for the full list and expected types.</param>
	/// <param name="devicePath">The mountpoint/physical path of the mount you'd like to target.</param>
	/// <param name="scriptSupports">An array containing the supported stats that the disk-fetching script can fetch. Keep it null if you'd like the method to auto-fetch it for you.</param>
	/// <returns>A string containing the requested stat's output. Do note that it can be a non-string (e.g., long) inside a string. Refer to the remarks for types.</returns>
	/// <exception cref="Exception">The requested stat is not supported by this method.</exception>
	/// <remarks>
	///	Supported stats and expected types:
	/// <list type="bullet">
	///		<item>
	///			<term>"Device.Path" [<c>string</c>] - </term>
	///			<description>Returns the physical device path from a specific path on the filesystem. Example: "/home" results in "/dev/nvme0n1p3".</description>
	///		</item>
	///		<item>
	///			<term>"Device.Filesystem" [<c>string</c>] - </term>
	///			<description>Returns the type of filesystem the parititon uses. Example: "/dev/nvme0n1p3" results in "btrfs".</description>
	///		</item>
	///		<item>
	///			<term>"Partition.TotalSpace" [<c>long</c>] - </term>
	///			<description>Returns the total size of the partition. In bytes.</description>
	///		</item>
	///		<item>
	///			<term>"Parition.FreeSpace" [<c>long</c>] - </term>
	///			<description>Returns the free space in the partition. In bytes.</description>
	///		</item>
	/// </list>
	/// <c>devicePath</c> is expected to be a physical path ("/dev/sda") in all the stats, except for "Device.Path", where it should be a specific path inside the filesystem ("/home")
	/// </remarks>
	/// <seealso cref="GetDevicePath"/>
	/// <seealso cref="GetDeviceFileSystem"/>
	private static string GetStat(string statName, string devicePath, string[]? scriptSupports = null)
	{
		scriptSupports ??= ShellMethods.GetScriptSupports($"{ApiConfig.BaseExecutablePath}/scripts/getDisk.sh");

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

	/// <summary>
	/// Method to fetch the physical device path from a specific path in the filesystem. Example: "/home" results in "/dev/nvme0n1p3".
	/// </summary>
	/// <param name="mountPoint">A path in the filesystem</param>
	/// <returns>The physical path of the device associated with the path on the FS.</returns>
	/// <exception cref="Exception">Something went wrong parsing the <c>df</c> output.</exception>
	private static string GetDevicePath(string mountPoint)
	{
		var regexMatch = DevicePathAndFileSystemRegex()
			.Match(ShellMethods.RunShell("df", $"{mountPoint} -T").StandardOutput);

		if (regexMatch.Groups.Count != 3) throw new Exception($"Error while parsing device path for '{mountPoint}'!");

		// A bit of a hack, but the GetDeviceFileSystem() function wouldn't work if its input was "devtmpfs".
		return regexMatch.Groups[1].Value == "devtmpfs" ? mountPoint : regexMatch.Groups[1].Value;
	}

	/// <summary>
	/// Method to fetch the type of filesystem the parititon uses. Example: "/dev/nvme0n1p3" results in "btrfs".
	/// </summary>
	/// <param name="mountPoint">A physical path to the partition's device file.</param>
	/// <returns>The partition's filesystem.</returns>
	/// <exception cref="Exception">Something went wrong parsing the <c>df</c> output.</exception>
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