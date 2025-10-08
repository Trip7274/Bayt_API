using System.Diagnostics;
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
	public const string Version = "0.12.16";
	/// <summary>
	/// Represents the current Bayt instance's MAJOR version in semver.
	/// </summary>
	public const byte ApiVersion = 0;

	/// <summary>
	/// The base path for API endpoints, including the API version. Prefixed before all endpoints
	/// </summary>
	public static readonly string BaseApiUrlPath = $"/api/v{ApiVersion}";
	/// <summary>
	/// Network port to expose the API on. Tries to use the env var <c>BAYT_NETWORK_PORT</c> first, then falls back to 5899.
	/// </summary>
	public static readonly ushort NetworkPort = (ushort) (ushort.TryParse(Environment.GetEnvironmentVariable("BAYT_NETWORK_PORT"), out var port) ? port : 5899);

	/// <summary>
	/// A stopwatch used to track how long Bayt has been running for. Started on API startup
	/// </summary>
	public static readonly Stopwatch BaytStartStopwatch = Stopwatch.StartNew();

	/// <summary>
	/// Indicates how verbose the API should be. 0-7, with 7 being the most verbose. Tries to use the env var <c>BAYT_VERBOSITY</c> first, then falls back to 6.
	/// </summary>
	public static readonly byte VerbosityLevel = (byte) (byte.TryParse(Environment.GetEnvironmentVariable("BAYT_VERBOSITY"), out var verbosityLevel) ? verbosityLevel : 6);

	// Paths and config-specific stuff from here on out

	/// <summary>
	/// Abs. path to the Bayt binary's directory
	/// </summary>
	public static readonly string BaseExecutablePath = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;

	/// <summary>
	/// Abs. path to the configuration directory. E.g. <c>/home/{user}/.config/baytConfig/</c>.
	/// </summary>
	/// <remarks>
	///	Tries to fetch the env var <c>BAYT_CONFIG_DIRECTORY</c> first, then <c>XDG_CONFIG_HOME</c>. Falls back to "<see cref="BaseExecutablePath"/>/baytConfig" if neither are set.
	/// </remarks>
	private static readonly string BaseConfigPath = Path.Combine(Environment.GetEnvironmentVariable("BAYT_CONFIG_DIRECTORY") ??
	                                                             Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ??
	                                                             BaseExecutablePath, "baytConfig");
	/// <summary>
	/// Abs. path to the Bayt SOCK interface file. The file will be non-existent if the interface is inactive. E.g. <c>/home/{user}/.local/state/baytApi.sock</c>.
	/// </summary>
	/// <remarks>
	///	Tries to fetch the env var <c>BAYT_SOCKET_DIRECTORY</c> first, then <c>XDG_STATE_HOME</c>. Falls back to "<see cref="BaseExecutablePath"/>/BaytApi.sock" if neither are set.
	/// </remarks>
	public static readonly string UnixSocketPath = Path.Combine(Environment.GetEnvironmentVariable("BAYT_SOCKET_DIRECTORY") ??
																Environment.GetEnvironmentVariable("XDG_STATE_HOME") ??
																BaseExecutablePath, "BaytApi.sock");
	/// <summary>
	/// Abs. path to the baytData directory. Used for <see cref="ApiConfiguration.PathToDataFolder"/> and <see cref="ApiConfiguration.PathToComposeFolder"/>. E.g. <c>/home/{user}/.local/share/baytData/</c>.
	/// </summary>
	/// <remarks>
	///	Tries to fetch the env var <c>BAYT_DATA_DIRECTORY</c> first, then <c>XDG_DATA_HOME</c>. Falls back to "<see cref="BaseExecutablePath"/>/baytData" if neither are set.
	/// </remarks>
	private static readonly string BaseDataPath = Path.Combine(Environment.GetEnvironmentVariable("BAYT_DATA_DIRECTORY") ??
	                                                           Environment.GetEnvironmentVariable("XDG_DATA_HOME")??
	                                                           BaseExecutablePath, "baytData");

	/// <summary>
	/// Abs. path to the specific configuration loaded currently.
	/// </summary>
	public static readonly string ConfigFilePath = Path.Combine(BaseConfigPath, "ApiConfiguration.json");


	/// <summary>
	/// All the supported stats one can request from the <c>getStats</c> endpoint
	/// </summary>
	public static readonly SystemStats[] PossibleStats = [SystemStats.Meta, SystemStats.System, SystemStats.Cpu, SystemStats.Gpu, SystemStats.Memory, SystemStats.Mounts];

	/// <summary>
	/// Enum containing all the stats this API can fetch.
	/// </summary>
	/// <seealso cref="ApiConfig.PossibleStats"/>
	public enum SystemStats : byte
	{
		Meta,
		System,
		Cpu,
		Gpu,
		Memory,
		Mounts
	}

	// Config management

	public static readonly JsonSerializerOptions BaytJsonSerializerOptions = new()
	{
		WriteIndented = true,
		IndentCharacter = '\t',
		IndentSize = 1
	};

	/// <summary>
	/// A unified class to access and modify all the API's configuration properties.
	/// </summary>
	public static class ApiConfiguration
	{
		static ApiConfiguration()
		{
			Logs.StreamWrittenTo += Logs.EchoLogs;
			Logs.LogStream.Write(new LogEntry(StreamId.Verbose, "Logging", "Registered logging callback."));

			if (!Directory.Exists(BaseConfigPath))
			{
				Logs.LogStream.Write(new LogEntry(StreamId.Verbose, "Config", "Base config directory does not exist, creating..."));
				Directory.CreateDirectory(BaseConfigPath);
				File.WriteAllText(Path.Combine(BaseConfigPath, "README"), "This folder is where the configs for the Bayt API are stored.\n" +
				                                                          "More info: https://github.com/Trip7274/Bayt_API");
			}
			LoadConfig();
		}

		/// <summary>
		/// The major API version associated with the current config.
		/// </summary>
		/// <remarks>
		///	This is required in the saved config.
		/// </remarks>
		public static byte ConfigVersion { get; private set; } = ApiVersion;
		/// <summary>
		/// The user-set name for this instance of Bayt.
		/// </summary>
		/// <remarks>
		///	Defaults to "Bayt API Host"
		/// </remarks>
		public static string BackendName { get; private set; } = "Bayt API Host";

		/// <summary>
		/// Lifetime of the cache. Set to 0 to effectively disable it.
		/// </summary>
		/// <remarks>
		///	Defaults to 5 seconds.
		/// </remarks>
		public static ushort SecondsToUpdate { get; private set; } = 5;
		/// <summary>
		/// Reflects the user's <see cref="SecondsToUpdate"/> clamped to a minimum of 3 seconds. Used for the initial fetch cycle as to not repeat it a few times.
		/// </summary>
		public static ushort ClampedSecondsToUpdate => ushort.Clamp(SecondsToUpdate, 3, ushort.MaxValue);

		/// <summary>
		///	Abs. path to the client data folder.
		/// </summary>
		/// <remarks>
		///	Defaults to "<see cref="BaseDataPath"/>/clientData".
		/// </remarks>
		public static string PathToDataFolder { get; private set; } = Path.Combine(BaseDataPath, "clientData");

		/// <summary>
		/// Abs. path to the folder containing all the docker compose folders.
		/// </summary>
		/// <remarks>
		///	Defaults to "<see cref="BaseDataPath"/>/containers".
		/// </remarks>
		public static string PathToComposeFolder { get; private set; } = Path.Combine(BaseDataPath, "containers");

		/// <summary>
		/// Whether the Docker integration is enabled.
		/// </summary>
		/// <remarks>
		///	Defaults to true.
		/// </remarks>
		public static bool DockerIntegrationEnabled { get; private set; } = true;

		/// <summary>
		/// Whether to keep 65,535 bytes of logs, or 6,553,500 (100x) bytes. Useful for debugging but will use more RAM (~20MBs+)
		/// </summary>
		/// <remarks>
		/// Defaults to false.
		/// </remarks>
		public static bool KeepMoreLogs { get; private set; }

		/// <summary>
		/// Whether to prepend every log with a timestamp.
		/// </summary>
		public static bool ShowTimestampsInLogs { get; private set; }

		/// <summary>
		/// Dictionary of watched mounts. Format is { "Path": "Name" }. For example, { "/home": "Home Partition" }
		/// </summary>
		/// <remarks>
		///	Defaults to { "/": "Root Partition" }. This is required in the saved config.
		/// </remarks>
		public static Dictionary<string, string> WatchedMounts { get; private set; } = new() { { "/", "Root Partition" } };

		/// <summary>
		/// JSON form of the <see cref="WolClientsClass"/> property. It's recommended to use that instead.
		/// </summary>
		/// <remarks>
		///	This is required in the saved config.
		/// </remarks>
		public static Dictionary<string, Dictionary<string, string?>> WolClients { get; private set; } = [];
		/// <summary>
		/// List of <see cref="WolHandling.WolClient"/>s saved by the user.
		/// </summary>
		/// <remarks>
		///	Defaults to empty. Generated from <see cref="WolClients"/> during startup.
		/// </remarks>
		[JsonIgnore]
		public static List<WolHandling.WolClient>? WolClientsClass { get; private set; }

		// Methods

		/// <summary>
		/// Gets the live configs as a Dictionary. Useful for JSON conversion.
		/// </summary>
		/// <returns></returns>
		public static Dictionary<string, dynamic> ToDictionary()
		{
			return new()
			{
				{ nameof(ConfigVersion), ConfigVersion },
				{ nameof(BackendName), BackendName },
				{ nameof(SecondsToUpdate), SecondsToUpdate },
				{ nameof(PathToDataFolder), PathToDataFolder },
				{ nameof(PathToComposeFolder), PathToComposeFolder },
				{ nameof(DockerIntegrationEnabled), DockerIntegrationEnabled },
				{ nameof(KeepMoreLogs), KeepMoreLogs },
				{ nameof(ShowTimestampsInLogs), ShowTimestampsInLogs },
				{ nameof(WatchedMounts), WatchedMounts },
				{ nameof(WolClients), WolClients }
			};
		}
		/// <summary>
		/// Serialize and flush the live configs to disk.
		/// </summary>
		private static void SaveConfig()
		{
			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(ToDictionary(), BaytJsonSerializerOptions));
		}

		/// <summary>
		/// Checks the corresponding in-disk configuration file for corruption or incompleteness and loads it.
		/// </summary>
		public static void LoadConfig()
		{
			CheckConfig();

			var loadedDict = (JsonSerializer.Deserialize<JsonDocument>(File.ReadAllText(ConfigFilePath, Encoding.UTF8))
			                 ?? throw new Exception("Failed to deserialize config file")).RootElement;

			ConfigVersion = loadedDict.GetProperty(nameof(ConfigVersion)).GetByte();
			BackendName = loadedDict.TryGetProperty(nameof(BackendName), out var backendName) ? backendName.GetString() ?? BackendName : BackendName;
			SecondsToUpdate = loadedDict.TryGetProperty(nameof(SecondsToUpdate), out var secondsToUpdate) ? secondsToUpdate.GetUInt16() : SecondsToUpdate;
			PathToDataFolder = loadedDict.TryGetProperty(nameof(PathToDataFolder), out var pathToDataFolder) ? pathToDataFolder.GetString() ?? PathToDataFolder : PathToDataFolder;
			PathToComposeFolder = loadedDict.TryGetProperty(nameof(PathToComposeFolder), out var pathToComposeFolder) ? pathToComposeFolder.GetString() ?? PathToComposeFolder : PathToComposeFolder;
			DockerIntegrationEnabled = loadedDict.TryGetProperty(nameof(DockerIntegrationEnabled), out var dockerIntegrationEnabled) ? dockerIntegrationEnabled.GetBoolean() : DockerIntegrationEnabled;
			KeepMoreLogs = loadedDict.TryGetProperty(nameof(KeepMoreLogs), out var keepMoreLogs) ? keepMoreLogs.GetBoolean() : KeepMoreLogs;
			ShowTimestampsInLogs = loadedDict.TryGetProperty(nameof(ShowTimestampsInLogs), out var showTimestampsInLogs) ? showTimestampsInLogs.GetBoolean() : ShowTimestampsInLogs;

			if (loadedDict.TryGetProperty(nameof(WatchedMounts), out var watchedMounts))
			{
				try
				{
					WatchedMounts = watchedMounts.Deserialize<Dictionary<string, string>>() ?? WatchedMounts;
				}
				catch (JsonException e)
				{
					// Technically could recover from this by regenerating the config, but I don't wanna reset the user's entire config every time they mess up their JSON.
					Logs.LogStream.Write(new(StreamId.Fatal, "Watched Mounts Load",
						"Failed to load a mount entry from the configuration file. Please ensure the JSON is valid."));
					Logs.LogStream.Write(new(StreamId.Fatal, "Watched Mounts Load", $"Error: {e.Message}\n\tStack trace: {e.StackTrace}"));
					throw new Exception();
				}
			}
			if (loadedDict.TryGetProperty(nameof(WolClients), out var wolClients))
			{
				try
				{
					WolClients = wolClients.Deserialize<Dictionary<string, Dictionary<string, string?>>>() ?? WolClients;
				}
				catch (JsonException e)
				{
					// Technically could recover from this by regenerating the config, but I don't wanna reset the user's entire config every time they mess up their JSON.
					Logs.LogStream.Write(new(StreamId.Fatal, "WoL Load",
						"Failed to load the list of WoL clients from the configuration file. Please ensure the JSON is valid."));
					Logs.LogStream.Write(new(StreamId.Fatal, "WoL Load", $"Error: {e.Message}\n\tStack trace: {e.StackTrace}"));
					throw new Exception();
				}
			}

			LoadWolClientsList();
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
				if (!Directory.Exists(PathToDataFolder))
				{
					const string defaultReadmeContent = """
					                                    This folder contains all server-wide data for each client, separated by client.

					                                    Please refer to the specific client's documentation for info on the file types, along with usage details.
					                                    Make sure the server is shut down before modifying these files.
					                                    This folder was made by Bayt API. More info: https://github.com/Trip7274/Bayt_API
					                                    """;

					Directory.CreateDirectory(PathToDataFolder);
					File.WriteAllText(Path.Combine(PathToDataFolder, "README"), defaultReadmeContent);
				}
				if (!Directory.Exists(PathToComposeFolder))
				{
					const string defaultReadmeContent = """
					                                    This folder contains all the Docker compose files made by Bayt API.

					                                    You should be safe manually deleting these files, provided the container is not running.
					                                    This folder was made by Bayt API. More info: https://github.com/Trip7274/Bayt_API
					                                    """;

					Directory.CreateDirectory(PathToComposeFolder);
					File.WriteAllText(Path.Combine(PathToComposeFolder, "README"), defaultReadmeContent);
				}
				if (ValidateConfigSyntax()) return;


				if (File.Exists($"{ConfigFilePath}.old"))
				{
					File.Delete($"{ConfigFilePath}.old");
				}
				File.Move(ConfigFilePath, $"{ConfigFilePath}.old");
			}
			else
			{
				Logs.LogStream.Write(new LogEntry(StreamId.Info, "Config", "Couldn't find the configuration file. A new one will be generated."));
			}

			SaveConfig();
		}

		/// <summary>
		/// Check the config file for any corruption and the existence of the required minimum properties.
		/// </summary>
		/// <returns>True if the file is valid. False otherwise.</returns>
		private static bool ValidateConfigSyntax()
		{
			try
			{
				var jsonDocument = JsonDocument.Parse(File.ReadAllText(ConfigFilePath)).RootElement;

				if (jsonDocument.TryGetProperty(nameof(ConfigVersion), out var configVersion) && configVersion.ValueKind == JsonValueKind.Number && configVersion.GetByte() > ApiVersion)
				{
					Logs.LogStream.Write(new(StreamId.Warning, "Config", $"Loaded configuration file is version {configVersion.GetByte()}, but the current version is {ApiVersion}. Here be dragons."));
				}

				return jsonDocument.TryGetProperty(nameof(ConfigVersion), out configVersion) && configVersion.ValueKind == JsonValueKind.Number;
			}
			catch (JsonException exception)
			{
				Logs.LogStream.Write(new(StreamId.Error, "Config", $"Failed processing JSON File, error message: {exception.Message} at {exception.LineNumber}:{exception.BytePositionInLine}."));
				Logs.LogStream.Write(new(StreamId.Error, "Config", $"Your configuration will be regenerated. You can find your old configuration in {ConfigFilePath}.old"));
				return false;
			}
		}

		// Altering/Accessing config files

		/// <summary>
		/// Provides edit access to the configuration, both live and in-disk.
		/// </summary>
		/// <remarks>
		///	Changes to the <see cref="WatchedMounts"/>, <see cref="WolClients"/>, <see cref="WolClientsClass"/>,
		/// or the <see cref="ConfigVersion"/> properties are ignored by this method. Use the appropriate methods for that.
		/// </remarks>
		/// <param name="newProps">
		///	Configs to edit. In the format: <c>{ "BackendName": "Test" }</c>
		/// </param>
		/// <seealso cref="AddMountpoint"/>
		/// <seealso cref="RemoveMountpoints"/>
		/// <seealso cref="AddWolClient"/>
		/// <seealso cref="RemoveWolClient"/>
		/// <returns>Whether the method actually changed any configs.</returns>
		public static bool EditConfig(Dictionary<string, dynamic> newProps)
		{
			Logs.LogStream.Write(new(StreamId.Verbose, "Config Edit", $"Config Edit Requested: {string.Join(", ", newProps.Keys)}"));
			if (newProps.Count == 0) return false;
			bool configChanged = false;

			foreach (var newPropKvp in newProps)
			{
				switch (newPropKvp.Key)
				{
					case nameof(BackendName) when newPropKvp.Value.GetString() is not null && newPropKvp.Value.GetString()! != BackendName:
					{
						Logs.LogStream.Write(new(StreamId.Verbose, "Config Edit", $"Config Edit Requested: {string.Join(", ", newProps.Keys)}"));
						BackendName = newPropKvp.Value.GetString() ?? BackendName;
						configChanged = true;
						break;
					}
					case nameof(SecondsToUpdate) when newPropKvp.Value.GetUInt16() != SecondsToUpdate:
					{
						SecondsToUpdate = newPropKvp.Value.GetUInt16();
						configChanged = true;
						break;
					}
					case nameof(PathToDataFolder) when newPropKvp.Value.GetString() is not null && newPropKvp.Value.GetString()! != PathToDataFolder:
					{
						PathToDataFolder = newPropKvp.Value.GetString() ?? PathToDataFolder;
						configChanged = true;
						break;
					}
					case nameof(PathToComposeFolder) when newPropKvp.Value.GetString() is not null && newPropKvp.Value.GetString()! != PathToComposeFolder:
					{
						PathToComposeFolder = newPropKvp.Value.GetString() ?? PathToComposeFolder;
						configChanged = true;
						break;
					}
					case nameof(DockerIntegrationEnabled) when newPropKvp.Value.GetBoolean() != DockerIntegrationEnabled:
					{
						DockerIntegrationEnabled = newPropKvp.Value.GetBoolean();
						configChanged = true;
						break;
					}
					case nameof(KeepMoreLogs) when newPropKvp.Value.GetBoolean() != KeepMoreLogs:
					{
						KeepMoreLogs = newPropKvp.Value.GetBoolean();
						configChanged = true;
						break;
					}
					case nameof(ShowTimestampsInLogs) when newPropKvp.Value.GetBoolean() != ShowTimestampsInLogs:
					{
						ShowTimestampsInLogs = newPropKvp.Value.GetBoolean();
						configChanged = true;
						break;
					}
				}
			}

			if (configChanged) SaveConfig();
			return configChanged;
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
		/// <returns>True if the config was changed. False otherwise.</returns>
		public static bool AddMountpoint(Dictionary<string, string?> mountPoints)
		{
			if (mountPoints.Count == 0) return false;

			bool configChanged = false;
			foreach (var mountPointToAdd in mountPoints.Where(mountPointToAdd => !WatchedMounts.ContainsKey(mountPointToAdd.Key)))
			{
				WatchedMounts.Add(mountPointToAdd.Key, mountPointToAdd.Value ?? "Mount");
				DiskHandling.FullDisksData.AddMount(mountPointToAdd.Key, mountPointToAdd.Value ?? "Mount");
				configChanged = true;
			}

			if (!configChanged) return false;
			SaveConfig();
			return true;
		}


		/// <summary>
		/// Remove a list of mountpoints from the configuration, both live and in-disk.
		/// </summary>
		/// <param name="mountPoints">The list of mountpoint's paths (Dict keys) to remove</param>
		public static void RemoveMountpoints(List<string> mountPoints)
		{
			if (mountPoints.Count == 0) return;

			foreach (var mountPoint in mountPoints)
			{
				WatchedMounts.Remove(mountPoint);
				DiskHandling.FullDisksData.RemoveMount(mountPoint);
			}

			SaveConfig();
		}

		// WOL management

		/// <summary>
		/// Generates and sets the appropriate <see cref="WolClientsClass"/> derived from the <see cref="WolClients"/> property.
		/// </summary>
		private static void LoadWolClientsList()
		{
			List<WolHandling.WolClient> wolClientsList = [];

			foreach (var wolClientDict in WolClients)
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
					var name = wolClientDict.Value.GetValueOrDefault("Name");
					Logs.LogStream.Write(new(StreamId.Error, "WoL Init",
						$"Failed to load a WoL client from the configuration file. Detected name: {name ?? "(unable to fetch name)"} Skipping."));
					Logs.LogStream.Write(new(StreamId.Error, "WoL Init", $"Error: {e.Message}\n\tStack trace: {e.StackTrace}"));
				}
			}

			WolClientsClass = wolClientsList;
		}

		/// <summary>
		/// Append a WoL client to the configuration. Updates the live and in-disk configuration.
		/// </summary>
		/// <param name="clientAddress">The IP Address of the client to add.</param>
		/// <param name="clientLabel">The label for the client to add.</param>
		public static int[] AddWolClient(string clientAddress, string clientLabel)
		{
			var physicalAddressProcess = ShellMethods.RunShell($"{BaseExecutablePath}/scripts/getNet.sh",
				["PhysicalAddress", clientAddress], throwIfTimedout: false).Result;

			var subnetMaskProcess =
				ShellMethods.RunShell($"{BaseExecutablePath}/scripts/getNet.sh", ["Netmask"], throwIfTimedout: false).Result;

			if (!subnetMaskProcess.IsSuccess || !IPAddress.TryParse(subnetMaskProcess.StandardOutput, out var subnetMask)
			                                 || !physicalAddressProcess.IsSuccess
			                                 || !PhysicalAddress.TryParse(physicalAddressProcess.StandardOutput, out var physicalAddress))
			{
				return [subnetMaskProcess.ExitCode, physicalAddressProcess.ExitCode];
			}

			WolClients.TryAdd(physicalAddress.ToString(), new()
			{
				{ "Name", clientLabel },
				{ "IpAddress", clientAddress },
				{ "SubnetMask", subnetMask.ToString() },
				{ "BroadcastAddress", null }
			});

			SaveConfig();
			LoadWolClientsList();
			return [0, 0];
		}

		/// <summary>
		/// Remove a specific client from the current configuration. Updates the live and in-disk configuration.
		/// </summary>
		/// <param name="clientAddress">Local IP Address of the client to remove.</param>
		public static int RemoveWolClient(string clientAddress)
		{
			var physicalAddressProcess = ShellMethods.RunShell($"{BaseExecutablePath}/scripts/getNet.sh",
				["PhysicalAddress", clientAddress], throwIfTimedout: false).Result;
			if (physicalAddressProcess.ExitCode == 124 || !PhysicalAddress.TryParse(physicalAddressProcess.StandardOutput, out var physicalAddress))
				return physicalAddressProcess.ExitCode;

			WolClients.Remove(physicalAddress.ToString());
			LoadWolClientsList();
			SaveConfig();
			return 0;
		}

		/// <summary>
		/// Update or "fill in" the broadcast address of a specific WolClient and reload the live and in-disk configuration.
		/// </summary>
		/// <param name="wolClient">WolClient object</param>
		/// <param name="newBroadcastAddress">The broadcast address to fill in</param>
		internal static void UpdateBroadcastAddress(WolHandling.WolClient wolClient, string newBroadcastAddress)
		{
			WolClients[wolClient.PhysicalAddress.ToString()]["BroadcastAddress"] = newBroadcastAddress;

			SaveConfig();
			LoadWolClientsList();
		}
	}
}