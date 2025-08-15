using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bayt_API;

/// <summary>
/// Contains everything related to user configuration, versioning, and the filesystem environment.
/// </summary>
public static class ApiConfig
{
	/// <summary>
	/// String containing the semver-aligned version of the current Bayt instance.
	/// </summary>
	public const string Version = "0.11.12";
	/// <summary>
	/// Represents the current Bayt instance's MAJOR version in semver.
	/// </summary>
	public const byte ApiVersion = 0;
	public static readonly string BaseApiUrlPath = $"/api/v{ApiVersion}";
	public const ushort NetworkPort = 5899;

	/// <summary>
	/// Contains the contents of the "XDG_CONFIG_HOME" env var if it exists. Used to set <see cref="BaseConfigPath"/>
	/// </summary>
	private static readonly string? XdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
	/// <summary>
	/// Contains the contents of the "XDG_DATA_HOME" env var if it exists. Used to set the default clientData folder.
	/// </summary>
	private static readonly string? XdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
	/// <summary>
	/// Contains the contents of the "XDG_STATE_HOME" env var if it exists. Used to set <see cref="UnixSocketPath"/>
	/// </summary>
	private static readonly string? XdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");

	/// <summary>
	/// Abs. path to the Bayt binary's directory
	/// </summary>
	public static readonly string BaseExecutablePath = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
	/// <summary>
	/// Abs. path to the Bayt SOCK interface. Will be non-existent if the interface is inactive.
	/// </summary>
	public static readonly string UnixSocketPath = XdgStateHome is not null && XdgStateHome.Length != 0 ?
		Path.Combine(XdgStateHome, "BaytApi.sock") : Path.Combine(BaseExecutablePath, "BaytApi.sock");
	/// <summary>
	/// Abs. path to the configuration directory
	/// </summary>
	private static readonly string BaseConfigPath = XdgConfigHome is not null && XdgConfigHome.Length != 0 ?
		Path.Combine(XdgConfigHome, "Bayt") : Path.Combine(BaseExecutablePath, "config");
	/// <summary>
	/// Abs. path to the specific configuration loaded currently.
	/// </summary>
	public static readonly string ConfigFilePath = Path.Combine(BaseConfigPath, "ApiConfiguration.json");

	public static readonly string[] PossibleStats = ["Meta", "System", "CPU", "GPU", "Memory", "Mounts"];

	// Config management

	/// <summary>
	/// A unified class to access and modify all the API's configuration properties.
	/// </summary>
	public sealed class ApiConfiguration
	{
		internal ApiConfiguration()
		{
			if (!Directory.Exists(BaseConfigPath))
			{
				Directory.CreateDirectory(BaseConfigPath);
				File.WriteAllText(Path.Combine(BaseConfigPath, "README"), "This folder is for the Bayt API project.\n" +
				                                                          "More info: https://github.com/Trip7274/Bayt_API");
			}

			ConfigProps = GetConfig();
		}


		/// <summary>
		/// Live Configuration object with all the appropriate properties.
		/// Use its parent class's methods to alter and update it.
		/// </summary>
		public ConfigProperties ConfigProps { get; private set; }

		/// <summary>
		/// Class that contains and specifies all the configuration properties.
		/// </summary>
		/// <seealso cref="ApiConfiguration.UpdateConfig"/>
		/// <remarks>
		///	This could probably be refactored into normal properties for the ApiConfiguration class (its parent),
		/// but that can be done later.
		/// </remarks>
		public sealed class ConfigProperties
		{
			/// <summary>
			/// The major API version associated with the current config.
			/// </summary>
			/// <remarks>
			///	This is required in the saved config.
			/// </remarks>
			public byte ConfigVersion { get; init; } = ApiVersion;

			/// <summary>
			/// The user-set name for this instance of Bayt.
			/// </summary>
			/// <remarks>
			///	Defaults to "Bayt API Host"
			/// </remarks>
			public string BackendName { get; init; } = "Bayt API Host";

			/// <summary>
			/// Lifetime of the cache. Set to 0 to effectively disable it.
			/// </summary>
			/// <remarks>
			///	Defaults to 5 seconds.
			/// </remarks>
			public ushort SecondsToUpdate { get; init; } = 5;
			/// <summary>
			///	Abs. path to the client data folder.
			/// </summary>
			/// <remarks>
			///	Defaults to either: <c>$XDG_DATA_HOME/Bayt/clientData</c>, or <c>BaytExecutablePath/clientData</c> depending on whether the env var <c>$XDG_DATA_HOME</c> is set.
			/// </remarks>
			public string PathToDataFolder { get; init; } = XdgDataHome is not null && XdgDataHome.Length != 0 ?
				Path.Combine(XdgDataHome, "Bayt", "clientData") : Path.Combine(BaseExecutablePath, "clientData");
			/// <summary>
			/// Relative (to the Bayt binary) path to the folder containing all the docker compose folders.
			/// </summary>
			/// <remarks>
			///	Defaults to either: <c>$XDG_DATA_HOME/Bayt/containers</c>, or <c>BaytExecutablePath/containers</c> depending on whether the env var <c>$XDG_DATA_HOME</c> is set.
			/// Each container will be inside a folder named with the slug of its name
			/// </remarks>
			public string PathToComposeFolder { get; init; } = XdgDataHome is not null && XdgDataHome.Length != 0 ?
				Path.Combine(XdgDataHome, "Bayt", "containers") : Path.Combine(BaseExecutablePath, "containers");
			/// <summary>
			/// Dictionary of watched mounts. Format is { "Path": "Name" }. For example, { "/home": "Home Partition" }
			/// </summary>
			/// <remarks>
			///	Defaults to { "/": "Root Partition" }. This is required in the saved config.
			/// </remarks>
			public Dictionary<string, string> WatchedMounts { get; init; } = new() { { "/", "Root Partition" } };

			/// <summary>
			/// JSON form of the <see cref="WolClientsClass"/> property. It's recommended to use that instead.
			/// </summary>
			/// <remarks>
			///	This is required in the saved config.
			/// </remarks>
			public Dictionary<string, Dictionary<string, string?>> WolClients { get; init; } = [];
			/// <summary>
			/// List of <see cref="WolHandling.WolClient"/>s saved by the user.
			/// </summary>
			/// <remarks>
			///	Defaults to empty. Generated from <see cref="WolClients"/> during startup.
			/// </remarks>
			[JsonIgnore]
			public List<WolHandling.WolClient>? WolClientsClass { get; set; }
		}
		private static readonly List<string> RequiredProperties = ["ConfigVersion", "WatchedMounts", "WolClients"];


		/// <summary>
		/// Checks the corresponding in-disk configuration file for corruption or incompleteness and returns the ConfigProperties of it.
		/// </summary>
		/// <returns>
		/// ConfigProperties of the in-disk configuration file
		/// </returns>
		private static ConfigProperties GetConfig()
		{
			CheckConfig();

			var configProperties = JsonSerializer.Deserialize<ConfigProperties>(File.ReadAllText(ConfigFilePath, Encoding.UTF8))
			       ?? throw new Exception("Failed to deserialize config file");

			LoadWolClientsList(ref configProperties);

			return configProperties;
		}

		// Config file maintainence

		/// <summary>
		/// Checks the in-disk configuration file. If it's missing, corrupt, or incomplete, it regenerates it with some defaults
		/// </summary>
		/// <remarks>
		/// The old (potentially corrupted) file is saved as a ".old" file alongside the current one, in case this function misdetects a valid file as invalid.
		/// </remarks>
		private static void CheckConfig()
		{
			if (File.Exists(ConfigFilePath))
			{
				if (ValidateConfigSyntax())
				{
					return;
				}

				if (File.Exists($"{ConfigFilePath}.old"))
				{
					File.Delete($"{ConfigFilePath}.old");
				}
				File.Move(ConfigFilePath, $"{ConfigFilePath}.old");
			}
			Console.WriteLine("[INFO] Configuration file seems to be invalid or non-existent, regenerating...");

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(new ConfigProperties()), Encoding.UTF8);
		}

		/// <summary>
		/// Ensures that the live and in-disk configurations are synced.
		/// </summary>
		public void UpdateConfig()
		{
			ConfigProps = GetConfig();
		}

		private static bool ValidateConfigSyntax()
		{
			try
			{
				var jsonDocument = JsonDocument.Parse(File.ReadAllText(ConfigFilePath)).RootElement;
				var requiredProperties = RequiredProperties.ToList();

				foreach (var jsonProp in jsonDocument.EnumerateObject())
				{
					if (jsonProp.Value.ValueKind == JsonValueKind.Null)
					{
						continue;
					}
                    requiredProperties.Remove(jsonProp.Name);
                }


				if (jsonDocument.TryGetProperty("ConfigVersion", out var configVersion) && configVersion.ValueKind == JsonValueKind.Number && configVersion.GetByte() > ApiVersion)
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[WARNING] Loaded configuration file is version {configVersion.GetByte()}, but the current version is {ApiVersion}. Here be dragons.");
					Console.ResetColor();
				}

				return requiredProperties.Count == 0;
			}
			catch (JsonException exception)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"[ERROR] Failed processing JSON File, error message: {exception.Message} at {exception.LineNumber}:{exception.BytePositionInLine}.\nWill regenerate.");
				Console.ResetColor();
				return false;
			}
		}

		// Altering/Accessing config files

		/// <summary>
		/// Provides edit access to the configuration, both live and in-disk.
		/// </summary>
		/// <remarks>
		///	Changing <c>WatchedMounts</c> or the <c>ConfigVersion</c> properties are blocked from this method. Use the appropriate methods for that.
		/// </remarks>
		/// <param name="newProps">
		///	Has to have more than one element to edit.
		/// </param>
		/// <param name="addNew">
		///	Whether to allow the addition of new properties.
		/// </param>
		/// <seealso cref="AddMountpoint"/>
		/// <seealso cref="RemoveMountpoint"/>
		public void EditConfig(Dictionary<string, dynamic> newProps, bool addNew = false)
		{
			if (newProps.Count == 0)
			{
				return;
			}


			var newConfig = JsonSerializer.Deserialize<Dictionary<string, dynamic>>(File.ReadAllText(ConfigFilePath))
			                ?? throw new Exception("Failed to deserialize config file");

			foreach (var newPropsKvp in newProps)
			{
				if ((!addNew && !newConfig.ContainsKey(newPropsKvp.Key)) || newPropsKvp.Key == "Mountpoints" || newPropsKvp.Key == "ConfigVersion")
				{
					continue;
				}

				newConfig[newPropsKvp.Key] = newPropsKvp.Value;
			}

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig), Encoding.UTF8);
			UpdateConfig();
		}

		// Mountpoint management

		/// <summary>
		/// Adds however many mountpoints you'd like to the in-disk configuration and updates the live configuration.
		/// </summary>
		/// <param name="mountPoints">
		///	Dictionary of mountpoints to add.
		/// Keys being the mountpoint path, and values being the user's label for each.
		/// The label (value) can be null, but it'll default to the name of "Mount".
		/// </param>
		public void AddMountpoint(Dictionary<string, string?> mountPoints)
		{
			if (mountPoints.Count == 0) return;

			var newConfig = GetConfig();

			bool configChanged = false;
			foreach (var mountPointToAdd in mountPoints)
			{
				if (newConfig.WatchedMounts.ContainsKey(mountPointToAdd.Key))
				{
					continue;
				}

				newConfig.WatchedMounts.Add(mountPointToAdd.Key, mountPointToAdd.Value ?? "Mount");
				DiskHandling.FullDisksData.AddMount(mountPointToAdd.Key, mountPointToAdd.Value ?? "Mount");
				configChanged = true;
			}

			if (!configChanged) return;
			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig), Encoding.UTF8);
			UpdateConfig();
		}


		/// <summary>
		/// Remove a list of mountpoints from the configuration, both live and in-disk.
		/// </summary>
		/// <param name="mountPoints">The list of mountpoint's paths (Dict keys) to remove</param>
		public void RemoveMountpoint(List<string> mountPoints)
		{
			if (mountPoints.Count == 0)
			{
				return;
			}

			var newConfig = GetConfig();

			foreach (var mountPoint in mountPoints)
			{
				newConfig.WatchedMounts.Remove(mountPoint);
				DiskHandling.FullDisksData.RemoveMount(mountPoint);
			}

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig), Encoding.UTF8);
			UpdateConfig();
		}

		// WOL management

		/// <summary>
		/// "Fills in" the appropriate <see cref="ConfigProperties.WolClientsClass"/> derived from the given parameter's <see cref="ConfigProperties.WolClients"/> property.
		/// </summary>
		/// <param name="configProps">A reference to a <see cref="ConfigProperties"/> object to fill its <see cref="ConfigProperties.WolClientsClass"/> property.</param>
		private static void LoadWolClientsList(ref ConfigProperties configProps)
		{
			if (configProps.WolClientsClass is not null)
			{
				return;
			}

			List<WolHandling.WolClient> wolClientsList = [];

			foreach (var wolClientDict in configProps.WolClients)
			{
				try
				{
					IPAddress? broadcastAddress = null;
					if (wolClientDict.Value.TryGetValue("BroadcastAddress", out var rawBroadcastAddress) && rawBroadcastAddress != "null" && rawBroadcastAddress != null)
					{
						broadcastAddress = IPAddress.Parse(rawBroadcastAddress);
					}

					wolClientsList.Add(new WolHandling.WolClient
					{
						Name = wolClientDict.Value.GetValueOrDefault("Name"),
						PhysicalAddress = PhysicalAddress.Parse(wolClientDict.Key),
						IpAddress = IPAddress.Parse(wolClientDict.Value["IpAddress"]!),
						SubnetMask = IPAddress.Parse(wolClientDict.Value["SubnetMask"]!),
						BroadcastAddress = broadcastAddress
					});
				}
				catch (Exception e)
				{
					wolClientDict.Value.TryGetValue("Name", out var name);
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"[ERROR] Failed to load a WoL client from the configuration file. Detected name: '{name ?? "(unable to fetch name)"}' Skipping.\nError: {e.Message}\nStack trace: {e.StackTrace}");
					Console.ResetColor();
				}
			}

			configProps.WolClientsClass = wolClientsList;
		}

		/// <summary>
		/// Append a WoL client to the configuration. Updates the live and in-disk configuration.
		/// </summary>
		/// <param name="clients">Dictionary with the format <c>{ "IPv4 Address": "Label" }</c></param>
		public void AddWolClient(Dictionary<string, string> clients)
		{
			var newConfig = GetConfig();

			foreach (var clientsToAdd in clients)
			{
				PhysicalAddress physicalAddress;
				IPAddress subnetMask;
				try
				{
					physicalAddress = PhysicalAddress.Parse(ShellMethods.RunShell($"{BaseExecutablePath}/scripts/getNet.sh", $"PhysicalAddress {clientsToAdd.Key}").StandardOutput);
					subnetMask = IPAddress.Parse(ShellMethods.RunShell($"{BaseExecutablePath}/scripts/getNet.sh", "Netmask").StandardOutput);
				}
				catch (FormatException)
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"[WARNING] Failed to get physical address for {clientsToAdd.Key} ('{clientsToAdd.Value}'), skipping.");
					Console.ResetColor();
					continue;
				}

				newConfig.WolClients.TryAdd(physicalAddress.ToString(), new()
				{
					{ "Name", clientsToAdd.Value },
					{ "IpAddress", clientsToAdd.Key },
					{ "SubnetMask", subnetMask.ToString() },
					{ "BroadcastAddress", null }
				});
			}

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig), Encoding.UTF8);
			UpdateConfig();
		}

		/// <summary>
		/// Remove a specific client or list of clients from the current configuration. Updates the live and in-disk configuration.
		/// </summary>
		/// <param name="clients">List of local IP Addresses to remove.</param>
		public void RemoveWolClient(List<string> clients)
		{
			var newConfig = GetConfig();

			foreach (var configKvp in from clientIpAddr in clients from configKvp in newConfig.WolClients where configKvp.Value["IpAddress"] == clientIpAddr select configKvp)
			{
				newConfig.WolClients.Remove(configKvp.Key);
			}

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig), Encoding.UTF8);
			UpdateConfig();
		}

		/// <summary>
		/// Update or "fill in" the broadcast address of a specific WolClient and reload the live and in-disk configuration.
		/// </summary>
		/// <param name="wolClient">WolClient objket</param>
		/// <param name="newBroadcastAddress"></param>
		internal void UpdateBroadcastAddress(WolHandling.WolClient wolClient, string newBroadcastAddress)
		{
			var newConfig = GetConfig();

			newConfig.WolClients[wolClient.PhysicalAddress.ToString()]["BroadcastAddress"] = newBroadcastAddress;

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig), Encoding.UTF8);
			UpdateConfig();
		}
	}

	public static ApiConfiguration MainConfigs { get; } = new();
}