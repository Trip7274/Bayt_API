using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bayt_API;

public static class ApiConfig
{
	public const string Version = "0.8.10";
	public const byte ApiVersion = 0;
	public static readonly string BaseApiUrlPath = $"/api/v{ApiVersion}";
	public const ushort NetworkPort = 5899;
	public static DateTime LastUpdated { get; set; }

	public static readonly string BaseExecutablePath = Environment.CurrentDirectory;
	public static readonly string UnixSocketPath = Path.Combine(BaseExecutablePath, "bayt.sock");
	private static readonly string BaseConfigPath = Path.Combine(BaseExecutablePath, "config");
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
			public byte ConfigVersion { get; init; }

			public required string BackendName { get; init; }
			public ushort SecondsToUpdate { get; set; }
			public string PathToDataFolder { get; set; } = "clientData";
			public required Dictionary<string, string> WatchedMounts { get; init; } // e.g. "/": "Root Partition"
			public required Dictionary<string, Dictionary<string, string?>> WolClients { get; init; }
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

		public void AddWolClient(Dictionary<string, string> clients)
		{
			// Input format: { "IPv4 Address": "Label" }

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