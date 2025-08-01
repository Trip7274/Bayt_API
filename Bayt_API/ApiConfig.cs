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
	///
	/// </summary>
	public const string Version = "0.9.10";
	public const byte ApiVersion = 0;
	public static readonly string BaseApiUrlPath = $"/api/v{ApiVersion}";
	public const ushort NetworkPort = 5899;
	public static DateTime LastUpdated { get; set; }

	/// <summary>
	/// Abs. path to the Bayt binary's directory
	/// </summary>
	public static readonly string BaseExecutablePath = Environment.CurrentDirectory;
	/// <summary>
	/// Abs. path to the Bayt SOCK interface. Will be non-existent if the interface is inactive.
	/// </summary>
	public static readonly string UnixSocketPath = Path.Combine(BaseExecutablePath, "bayt.sock");
	/// <summary>
	/// Abs. path to the configuration directory
	/// </summary>
	private static readonly string BaseConfigPath = Path.Combine(BaseExecutablePath, "config");
	/// <summary>
	/// Abs. path to the specific configuration loaded currently.
	/// </summary>
	private static readonly string ConfigFilePath = Path.Combine(BaseConfigPath, "ApiConfiguration.json");

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
			public byte ConfigVersion { get; init; }

			/// <summary>
			/// The user-set name for this instance of Bayt.
			/// </summary>
			/// <remarks>
			///	Defaults to "Bayt API Host"
			/// </remarks>
			public required string BackendName { get; init; }
			/// <summary>
			/// Lifetime of the cache. Set to 0 to effectively disable it.
			/// </summary>
			/// <remarks>
			///	Defaults to 5 seconds.
			/// </remarks>
			public ushort SecondsToUpdate { get; set; }
			/// <summary>
			///	Relative (to the Bayt binary) path to the client data folder.
			/// </summary>
			/// <remarks>
			///	Defaults to "clientData"
			/// </remarks>
			public string PathToDataFolder { get; set; } = "clientData";
			/// <summary>
			/// Dictionary of watched mounts. Format is { "Path": "Name" }. For example, { "/home": "Home Partition" }
			/// </summary>
			/// <remarks>
			///	Defaults to { "/": "Root Partition" }. This is required in the saved config.
			/// </remarks>
			public required Dictionary<string, string> WatchedMounts { get; init; }
			/// <summary>
			/// JSON form of the <see cref="WolClientsClass"/> property. It's recommended to use that instead.
			/// </summary>
			/// <remarks>
			///	This is required in the saved config.
			/// </remarks>
			public required Dictionary<string, Dictionary<string, string?>> WolClients { get; init; }
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
			Console.WriteLine($"[{Path.GetFileNameWithoutExtension(ConfigFilePath)}] " +
			                  $"Configuration file seems to be invalid or non-existent, regenerating at {ConfigFilePath}...");

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(new ConfigProperties
			{
				ConfigVersion = ApiVersion,
				BackendName = "Bayt API Host",
				SecondsToUpdate = 5,
				PathToDataFolder = "clientData",
				WatchedMounts = new() { {"/", "Root Partition"} },
				WolClients = []
			}), Encoding.UTF8);
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
					Console.WriteLine($"[WARNING] Loaded configuration file is version {configVersion.GetByte()}, but the current version is {ApiVersion}. Here be dragons.");
				}

				return requiredProperties.Count == 0;
			}
			catch (JsonException exception)
			{
				Console.WriteLine($"[ERROR] Failed processing JSON File, error message: {exception.Message} at {exception.LineNumber}:{exception.BytePositionInLine}.\nWill regenerate.");
				return false;
			}
		}

		// Altering/Accessing config files

		/// <summary>
		/// Provides edit access to the configuration, both live and in-disk.
		/// </summary>
		/// <remarks>
		///	Please make sure to not use this for adding or removing mountpoints. You *can*, but that doesn't mean you should.
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
			if (mountPoints.Count == 0)
			{
				return;
			}

			var newConfig = GetConfig();

			foreach (var mountPointToAdd in mountPoints)
			{
				if (newConfig.WatchedMounts.ContainsKey(mountPointToAdd.Key))
				{
					continue;
				}

				newConfig.WatchedMounts.Add(mountPointToAdd.Key, mountPointToAdd.Value ?? "Mount");
			}

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
					Console.WriteLine($"[ERROR] Failed to load a WoL client from the configuration file. Detected name: '{name ?? "(unable to fetch name)"}' Skipping.\nError: {e.Message}\nStack trace: {e.StackTrace}");
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
					Console.WriteLine($"[WARNING] Failed to get physical address for {clientsToAdd.Key} ('{clientsToAdd.Value}'), skipping.");
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