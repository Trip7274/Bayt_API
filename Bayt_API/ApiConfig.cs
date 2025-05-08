using System.Text;
using System.Text.Json;

namespace Bayt_API;

public static class ApiConfig
{
	public const string Version = "0.5.7";
	public const byte ApiVersion = 0;
	public static readonly string BaseApiUrlPath = $"/api/v{ApiVersion}";
	public static DateTime LastUpdated { get; set; }
	public static readonly string BaseExecutablePath = Path.GetDirectoryName(Environment.ProcessPath)
	                                                   ?? throw new Exception("Could not get process path");
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
			public ushort SecondsToUpdate { get; init; }
			public ushort NetworkPort { get; init; }

			public required Dictionary<string, string> WatchedMounts { get; init; }
		}
		private static readonly List<string> RequiredProperties = ["ConfigVersion", "WatchedMounts"];


		/// <summary>
		/// Checks the corresponding in-disk configuration file for corruption or incompleteness and returns the ConfigProperties of it.
		/// </summary>
		/// <returns>
		/// ConfigProperties of the in-disk configuration file
		/// </returns>
		private static ConfigProperties GetConfig()
		{
			CheckConfig();

			return JsonSerializer.Deserialize<ConfigProperties>(File.ReadAllText(ConfigFilePath, Encoding.UTF8))
			       ?? throw new Exception("Failed to deserialize config file");
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
				BackendName = "Bayt Api Host",
				SecondsToUpdate = 5,
				NetworkPort = 5899,
				WatchedMounts = new Dictionary<string, string> { {"/", "Root Partition"} }
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
					// Boohoo, Rider, I know ApiVersion is currently 0, hush.
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
	}



	public static ApiConfiguration MainConfigs { get; } = new();
}