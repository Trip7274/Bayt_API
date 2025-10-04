using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Bayt_API;

public static class Docker
{
	public static bool IsDockerAvailable => File.Exists("/var/run/docker.sock") && ApiConfig.ApiConfiguration.DockerIntegrationEnabled;

	/// <summary>
	/// Returns whether Docker-Compose is available.
	/// </summary>
	[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
	public static bool IsDockerComposeAvailable
	{
		get
		{
			if (!IsDockerAvailable) return false;

			string[] pathsInPathEnv = Environment.GetEnvironmentVariable("PATH")!.Split(':');
			string? composeBinPath = null;
			foreach (var pathInEnv in pathsInPathEnv)
			{
				string[] dockerComposeBins;
				try
				{
					dockerComposeBins = Directory.GetFiles(pathInEnv, "docker-compose", SearchOption.TopDirectoryOnly);
				}
				catch (DirectoryNotFoundException)
				{
					continue;
				}
				if (dockerComposeBins.Length == 0) continue;

				composeBinPath = dockerComposeBins[0];
			}

			return composeBinPath is not null && (File.GetUnixFileMode(composeBinPath) & UnixFileMode.UserExecute) != 0;
		}
	}

	/// <summary>
	/// The default icon to use if a container doesn't have a preferred icon.
	/// </summary>
	private const string GenericIconLink = "https://api.iconify.design/mdi/cube-outline.svg";

	/// <summary>
	/// Provides methods and properties for interacting with the system's Docker containers.
	/// </summary>
	public static class DockerContainers
	{
		static DockerContainers()
		{
			var dockerRequest = SendRequest("containers/json?all=true").Result;
			if (!dockerRequest.IsSuccess) throw new Exception($"Docker request failed. ({dockerRequest.Status})\n Got body: {dockerRequest.Body}");
			var dockerOutput = JsonSerializer.Deserialize<JsonElement>(dockerRequest.Body);

			foreach (var containerEntries in dockerOutput.EnumerateArray())
			{
				Containers.Add(new DockerContainer(containerEntries));
			}
			LastUpdate = DateTime.Now + TimeSpan.FromSeconds(ApiConfig.ApiConfiguration.ClampedSecondsToUpdate);
		}

		/// <summary>
		/// Fetches the current container list from Docker and updates the <see cref="Containers"/> list.
		/// </summary>
		/// <exception cref="Exception">Something went wrong trying to communicate with the Docker daemon.</exception>
		public static async Task UpdateData()
		{
			var dockerRequest = await SendRequest("containers/json?all=true");
			if (!dockerRequest.IsSuccess) throw new Exception($"Docker request failed. ({dockerRequest.Status})\n Got body: {dockerRequest.Body}");

			var dockerOutput = JsonSerializer.Deserialize<JsonElement>(dockerRequest.Body);

			Containers.Clear();
			foreach (var containerEntries in dockerOutput.EnumerateArray())
			{
				Containers.Add(new DockerContainer(containerEntries));
			}
			LastUpdate = DateTime.Now;
		}

		/// <summary>
		/// Contains the task currently updating the data. Null if no update is currently in progress.
		/// </summary>
		private static Task? UpdatingTask { get; set; }
		/// <summary>
		/// Used to prevent multiple threads from updating the data at the same time.
		/// </summary>
		private static readonly Lock UpdatingLock = new();

		/// <summary>
		/// Check if the container data is too old and needs to be updated. If so, update it.
		/// </summary>
		public static async Task UpdateDataIfNecessary()
		{
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "Docker Container Fetch", "Checking for Docker container data update..."));
			if (!ShouldUpdate) return;
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "Docker Container Fetch", "Updating Docker container data..."));

			var localTask = UpdatingTask;
			if (localTask is null)
			{
				lock (UpdatingLock)
				{
					UpdatingTask ??= UpdateData();
					localTask = UpdatingTask;
				}
			}

			await localTask;
			lock (UpdatingLock)
			{
				UpdatingTask = null;
			}
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "Docker Container Fetch", "Docker container data updated."));
		}

		/// <summary>
		/// Formats all the Docker containers into an array of Dictionaries.
		/// </summary>
		/// <param name="getAllContainers">Whether to include non-running containers. Defaults to include all containers.</param>
		/// <returns>an array of Dictionary{string, dynamic?} objects.</returns>
		public static Dictionary<string, dynamic?>[] ToDictionary(bool getAllContainers = true)
		{
			List<Dictionary<string, dynamic?>> containersList = [];
			containersList.AddRange(getAllContainers
				? Containers.Select(container => container.ToDictionary())
				: Containers.Where(container => container.State == "running")
					.Select(container => container.ToDictionary()));

			return containersList.ToArray();
		}

		/// <summary>
		/// Returns the standard format of a container's metadata file. Each field can be customized but defaults to null.
		/// </summary>
		/// <param name="prettyName">The pretty name for the container.</param>
		/// <param name="note">The field of Notes for the container.</param>
		/// <param name="preferredIconLink">The preferred icon link for the container. Should be a valid link starting with <c>http(s)://</c>.</param>
		/// <param name="webpageLink">The URL pointing at the container's web UI, if applicable.</param>
		/// <returns>A Dictionary that should be serialized to JSON before being saved to the container's metadata file.</returns>
		public static Dictionary<string, string?> GetDefaultMetadata(string? prettyName = null, string? note = null, string? preferredIconLink = null, string? webpageLink = null)
		{
			return new()
			{
				{ "_comment", "This file indicates that this container is managed by Bayt and contains some details about the container. You are free to edit or delete it. It is okay for some to be null." },
				{ "_types", "All of the values are strings that can be null. (string? type)" },

				{ nameof(DockerContainer.PrettyName), prettyName },
				{ nameof(DockerContainer.Note), note },
				{ nameof(DockerContainer.PreferredIconLink), preferredIconLink },
				{ nameof(DockerContainer.WebpageLink), webpageLink }
			};
		}

		/// <summary>
		/// Contains all the system's Docker containers.
		/// </summary>
		/// <seealso cref="UpdateDataIfNecessary"/>
		public static List<DockerContainer> Containers { get; } = [];

		/// <summary>
		/// The last time this was updated. Used internally.
		/// </summary>
		public static DateTime LastUpdate { get; private set; }

		/// <summary>
		/// Returns whether the current data is too stale and should be updated.
		/// </summary>
		public static bool ShouldUpdate =>
			LastUpdate.AddSeconds(ApiConfig.ApiConfiguration.SecondsToUpdate) < DateTime.Now;
	}

	public sealed class DockerContainer
	{
		public DockerContainer(JsonElement dockerOutput)
		{
			Id = dockerOutput.GetProperty(nameof(Id)).GetString() ?? throw new ArgumentException("Docker container ID is null.");

			_labels = dockerOutput.GetProperty("Labels").Deserialize<Dictionary<string, string?>>()!;

			if (_labels.TryGetValue("com.docker.compose.project.config_files", out string? composePath) && File.Exists(composePath))
			{
				ComposePath = composePath;
			}
			if (ComposePath is not null)
			{
				IsCompose = true;
				var composeDirectory = Path.GetDirectoryName(ComposePath);
				IsManaged = composeDirectory is not null && File.Exists(Path.Combine(composeDirectory, ".BaytMetadata.json"));
			}
			Image = dockerOutput.GetProperty(nameof(Image)).GetString() ?? throw new ArgumentException("Docker container image is null.");

			FillContainerMetadata();
			GetContainerNames(dockerOutput);
			ImageDescription = GetDescription(_labels);
			GetHrefFromLabels();
			IconUrls = GetIconUrls(_labels);

			ImageID = dockerOutput.GetProperty(nameof(ImageID)).GetString() ?? throw new ArgumentException("Docker container image ID is null.");
			ImageUrl = GetImageUrl(_labels);
			ImageVersion = GetImageVersion(_labels, [Image]);

			Command = dockerOutput.GetProperty(nameof(Command)).GetString() ?? throw new ArgumentException("Docker container command is null.");

			CreatedUnix = dockerOutput.GetProperty(nameof(Created)).GetInt64();
			Created = DateTimeOffset.FromUnixTimeSeconds(CreatedUnix).DateTime.ToUniversalTime();

			State = dockerOutput.GetProperty(nameof(State)).GetString() ?? throw new ArgumentException("Docker container state is null.");
			Status = dockerOutput.GetProperty(nameof(Status)).GetString() ?? throw new ArgumentException("Docker container status is null.");

			NetworkMode = dockerOutput.GetProperty("HostConfig").GetProperty(nameof(NetworkMode)).GetString() ?? throw new ArgumentException("Docker container network mode is null.");
			IpAddress = GetIp(dockerOutput.GetProperty("NetworkSettings"), NetworkMode);

			if (dockerOutput.TryGetProperty("Ports", out var portBindingsElement) && portBindingsElement.GetArrayLength() != 0)
			{
				foreach (var portEntry in dockerOutput.GetProperty("Ports").EnumerateArray())
				{
					PortBindings.Add(new PortBinding(portEntry));
				}
			}

			if (dockerOutput.TryGetProperty("Mounts", out var mountBindingsElement) &&
			    mountBindingsElement.GetArrayLength() != 0)
			{
				foreach (var mountEntry in dockerOutput.GetProperty("Mounts").EnumerateArray())
				{
					MountBindings.Add(new MountBinding(mountEntry));
				}
			}
		}
		public Dictionary<string, dynamic?> ToDictionary()
		{
			List<Dictionary<string, dynamic?>> portBindings = [];
			portBindings.AddRange(PortBindings.Select(portBinding => portBinding.ToDictionary()));

			List<Dictionary<string, string>> mountBindings = [];
			mountBindings.AddRange(MountBindings.Select(mountBinding => mountBinding.ToDictionary()));

			return new()
			{
				{ nameof(Id), Id },
				{ nameof(Names), Names },

				{ nameof(Image), Image },
				{ nameof(ImageID), ImageID },
				{ nameof(ImageUrl), ImageUrl },
				{ nameof(ImageVersion), ImageVersion },
				{ nameof(ImageDescription), ImageDescription },

				{ nameof(Command), Command },
				{ nameof(Created), Created },
				{ nameof(CreatedUnix), CreatedUnix },

				{ nameof(State), State },
				{ nameof(Status), Status },

				{ nameof(IsCompose), IsCompose },
				{ nameof(IsManaged), IsManaged },

				{ nameof(IconUrls), IconUrls },

				{ nameof(IpAddress), IpAddress.ToString() },
				{ nameof(NetworkMode), NetworkMode },
				{ nameof(PortBindings), portBindings },
				{ nameof(MountBindings), mountBindings },

				{ nameof(Note), Note },
				{ nameof(WebpageLink), WebpageLink },

				{ nameof(DockerContainers.LastUpdate), DockerContainers.LastUpdate.ToUniversalTime() }
			};
		}

		private static IPAddress GetIp(JsonElement networkSettingsElement, string networkName)
		{
			var machineLocalIp = StatsApi.GetLocalIpAddress();

			if (!networkSettingsElement.GetProperty("Networks").GetProperty(networkName).TryGetProperty("IPAddress", out var ipElement)
			    || string.IsNullOrEmpty(ipElement.GetString())) return machineLocalIp;


			var ip = ipElement.GetString();
			return ip is null ? machineLocalIp : IPAddress.Parse(ip);

		}
		private void GetContainerNames(JsonElement dockerOutput)
		{
			Names = GetNames(_labels, dockerOutput, [Image]);

			if (IsManaged && PrettyName is not null)
			{
				Names = Names.Prepend(PrettyName).ToList();
			}
		}
		private void GetHrefFromLabels()
		{
			if (_labels is null) return;

			if (_labels.TryGetValue("bayt.url", out var href)
			    || _labels.TryGetValue("glance.url", out href)
			    || _labels.TryGetValue("homepage.instance.internal.href", out href))
			{
				WebpageLink = href;
			}
		}
		private void FillContainerMetadata()
		{
			if (!IsManaged) return;
			string composeMetadataPath = Path.Combine(Path.GetDirectoryName(ComposePath) ?? "/", ".BaytMetadata.json");

			var currentMetadata = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(composeMetadataPath))
			                      ?? throw new Exception("Failed to deserialize metadata file. Please make sure it is valid JSON.");

			try
			{
				PrettyName = currentMetadata[nameof(PrettyName)];
				Note = currentMetadata[nameof(Note)];
				PreferredIconLink = currentMetadata[nameof(PreferredIconLink)] ?? PreferredIconLink;
				WebpageLink = currentMetadata[nameof(WebpageLink)];
			}
			catch (KeyNotFoundException e)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("[ERROR] Couldn't process the metadata file for a Docker container. Please make sure it is valid JSON and contains all the required keys.");
				Console.WriteLine($"\tContainer ID: {Id}");
				Console.WriteLine($"\tError message: {e.Message}");
				Console.WriteLine($"\tMetadata File: {composeMetadataPath}");
				Console.ResetColor();
			}
		}

		public static readonly List<string> SupportedLabels = [nameof(PrettyName), nameof(Note), nameof(PreferredIconLink), nameof(WebpageLink)];
		public async Task<bool> SetContainerMetadata(Dictionary<string, string?> props)
		{
			if (!IsManaged) return false;
			bool changedAnything = false;

			string composeMetadataFile = Path.Combine(Path.GetDirectoryName(ComposePath) ?? "/", ".BaytMetadata.json");
			await using var composeMetadataFileStream = File.Open(composeMetadataFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

			var currentMetadata = await JsonSerializer.DeserializeAsync<Dictionary<string, string?>>(composeMetadataFileStream) ?? throw new Exception("Failed to deserialize metadata file. Please make sure it is valid JSON.");
			foreach (var prop in props.Where(prop => SupportedLabels.Contains(prop.Key)))
			{
				switch (prop.Key)
				{
					case nameof(PrettyName):
					{
						PrettyName = prop.Value;
						currentMetadata[nameof(PrettyName)] = prop.Value;
						changedAnything = true;
						break;
					}
					case nameof(Note):
					{
						Note = prop.Value;
						currentMetadata[nameof(Note)] = prop.Value;
						changedAnything = true;
						break;
					}
					case nameof(PreferredIconLink):
					{
						PreferredIconLink = prop.Value ?? GenericIconLink;
						currentMetadata[nameof(PreferredIconLink)] = prop.Value;
						changedAnything = true;
						break;
					}
					case nameof(WebpageLink):
					{
						WebpageLink = prop.Value;
						currentMetadata[nameof(WebpageLink)] = prop.Value;
						changedAnything = true;
						break;
					}
				}
			}

			if (!changedAnything) return false;

			composeMetadataFileStream.SetLength(0);
			await JsonSerializer.SerializeAsync(composeMetadataFileStream, currentMetadata, ApiConfig.BaytJsonSerializerOptions);
			return true;
		}

		public string Id { get; }
		public List<string> Names { get; private set; } = [];
		private readonly Dictionary<string, string>? _labels;

		public string Image { get; }
		// ReSharper disable once InconsistentNaming
		public string ImageID { get; }
		public string? ImageUrl { get; }
		public string? ImageVersion { get;  }
		public string? ImageDescription { get; private set; }

		public string? ComposePath { get; }
		public string Command { get; }
		public DateTime Created { get; }
		public long CreatedUnix { get; }

		public string State { get; }
		public string Status { get; }

		public bool IsCompose { get; }
		public bool IsManaged { get; }

		public List<string> IconUrls { get; }

		public IPAddress IpAddress { get; }

		public string NetworkMode { get; }
		public List<PortBinding> PortBindings { get; } = [];
		public List<MountBinding> MountBindings { get; } = [];
		public ContainerStats? Stats => State == "running" ? new(Id) : null;

		public string? PrettyName { get; private set; }
		public string? Note { get; private set; }
		public string PreferredIconLink { get; private set; } = GenericIconLink;
		public string? WebpageLink { get; private set; }
	}

	public sealed class ContainerStats
	{
		public ContainerStats(string containerId)
		{
			var dockerRequest = SendRequest($"/containers/{containerId}/stats?stream=false").Result;
			if (!dockerRequest.IsSuccess) throw new Exception($"Docker request for stats failed. ({dockerRequest.Status})\n Got body: {dockerRequest.Body}");

			var dockerOutput = JsonSerializer.Deserialize<JsonElement>(dockerRequest.Body);

			// These are required to calculate the CPU usage.
			// https://docs.docker.com/reference/api/engine/version/v1.51/#tag/Container/operation/ContainerStats
			//	- cpu_delta = cpu_stats.cpu_usage.total_usage - precpu_stats.cpu_usage.total_usage
			//	- system_cpu_delta = cpu_stats.system_cpu_usage - precpu_stats.system_cpu_usage
			//	- number_cpus = length(cpu_stats.cpu_usage.percpu_usage) or cpu_stats.online_cpus
			//	- CPU usage % = (cpu_delta / system_cpu_delta) * number_cpus * 100.0

			var cpuProperty = dockerOutput.GetProperty("cpu_stats");
			var cpuDelta = cpuProperty.GetProperty("cpu_usage").GetProperty("total_usage").GetUInt64()
			               - dockerOutput.GetProperty("precpu_stats").GetProperty("cpu_usage").GetProperty("total_usage").GetUInt64();
			var systemCpuDelta = cpuProperty.GetProperty("system_cpu_usage").GetUInt64()
			                     - dockerOutput.GetProperty("precpu_stats").GetProperty("system_cpu_usage").GetUInt64();
			var cpuCount = cpuProperty.GetProperty("online_cpus").GetUInt32();

			CpuUtilizationPerc = MathF.Round((float) cpuDelta / systemCpuDelta * cpuCount * 100, 2);

			var memoryProperty = dockerOutput.GetProperty("memory_stats");
			UsedMemory = memoryProperty.GetProperty("usage").GetUInt64();
			TotalMemory = memoryProperty.GetProperty("limit").GetUInt64();

			var networkEntry = dockerOutput.GetProperty("networks").EnumerateObject().First().Value;

			RecievedNetworkBytes = networkEntry.GetProperty("rx_bytes").GetUInt64();
			SentNetworkBytes = networkEntry.GetProperty("tx_bytes").GetUInt64();
		}

		public Dictionary<string, dynamic> ToDictionary()
		{
			return new()
			{
				{ nameof(CpuUtilizationPerc), CpuUtilizationPerc },
				{ nameof(UsedMemory), UsedMemory },
				{ nameof(TotalMemory), TotalMemory },
				{ nameof(AvailableMemory), AvailableMemory },
				{ nameof(UsedMemoryPercent), UsedMemoryPercent },
				{ nameof(RecievedNetworkBytes), RecievedNetworkBytes },
				{ nameof(SentNetworkBytes), SentNetworkBytes }
			};
		}

		public float CpuUtilizationPerc { get; }
		public ulong UsedMemory { get; }
		public ulong TotalMemory { get; }
		public ulong AvailableMemory => TotalMemory - UsedMemory;
		public float UsedMemoryPercent => TotalMemory == 0 ? 0 : MathF.Round((float) UsedMemory / TotalMemory * 100, 2);

		public ulong RecievedNetworkBytes { get; }
		public ulong SentNetworkBytes { get; }
	}

	public sealed class PortBinding(JsonElement portEntry)
	{
		public Dictionary<string, dynamic?> ToDictionary()
		{
			return new()
			{
				{ "IpAddress", IpAddress },
				{ "PrivatePort", PrivatePort },
				{ "PublicPort", PublicPort },
				{ "Type", Type }
			};
		}

		public string? IpAddress { get; } = portEntry.TryGetProperty("IP", out var ipAddr) ? ipAddr.GetString() : null;
		public ushort? PrivatePort { get; } = portEntry.TryGetProperty(nameof(PrivatePort), out var privatePort) ? privatePort.GetUInt16() : null;
		public ushort? PublicPort { get; } = portEntry.TryGetProperty(nameof(PublicPort), out var publicPort) ? publicPort.GetUInt16() : null;
		public string? Type { get; } = portEntry.TryGetProperty(nameof(Type), out var bindingType) ? bindingType.GetString() : null;
	}

	public sealed class MountBinding(JsonElement mountEntry)
	{
		public Dictionary<string, string> ToDictionary()
		{
			return new()
			{
				{ "Type", Type },
				{ "Source", Source },
				{ "Destination", Destination },
				{ "Mode", Mode }
			};
		}

		public string Type { get; } = mountEntry.GetProperty(nameof(Type)).GetString() ?? throw new ArgumentException("Docker container's Mount is null.");
		public string Source { get; } = mountEntry.GetProperty(nameof(Source)).GetString() ?? throw new ArgumentException("Docker container's Mount Source is null.");
		public string Destination { get; } = mountEntry.GetProperty(nameof(Destination)).GetString() ?? throw new ArgumentException("Docker container's Mount Destination is null.");
		public string Mode { get; } = mountEntry.GetProperty(nameof(Mode)).GetString() ?? throw new ArgumentException("Docker container's Mount Mode is null.");
	}

	public static class ImagesInfo
	{
		static ImagesInfo()
		{
			var dockerRequest = SendRequest("images/json").Result;
			if (!dockerRequest.IsSuccess) throw new Exception($"Docker request failed. ({dockerRequest.Status})\n Got body: {dockerRequest.Body}");
			var dockerOutput = JsonSerializer.Deserialize<JsonElement>(dockerRequest.Body);

			foreach (var imageEntry in dockerOutput.EnumerateArray())
			{
				Images.Add(imageEntry.Deserialize<ImageInfo>()!);
			}
			LastUpdate = DateTime.Now + TimeSpan.FromSeconds(ApiConfig.ApiConfiguration.ClampedSecondsToUpdate);
		}

		public static async Task UpdateData()
		{
			var dockerRequest = await SendRequest("images/json");
			if (!dockerRequest.IsSuccess) throw new Exception($"Docker request failed. ({dockerRequest.Status})\n Got body: {dockerRequest.Body}");

			var dockerOutput = JsonSerializer.Deserialize<JsonElement>(dockerRequest.Body);

			Images.Clear();
			foreach (var imageEntry in dockerOutput.EnumerateArray())
			{
				Images.Add(imageEntry.Deserialize<ImageInfo>()!);
			}
			LastUpdate = DateTime.Now;
		}

		public static async Task UpdateDataIfNecessary()
		{
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "Docker Image Fetch", "Checking for Docker image data update..."));
			if (!ShouldUpdate) return;
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "Docker Image Fetch", "Updating Docker image data..."));

			var localTask = UpdatingTask;
			if (localTask is null)
			{
				lock (UpdatingLock)
				{
					UpdatingTask ??= UpdateData();
					localTask = UpdatingTask;
				}
			}

			await localTask;
			lock (UpdatingLock)
			{
				UpdatingTask = null;
			}
			await Logs.LogStream.WriteAsync(new LogEntry(StreamId.Verbose, "Docker Image Fetch", "Docker image data updated."));
		}

		public static Dictionary<string, dynamic?>[] ToDictionary()
		{
			List<Dictionary<string, dynamic?>> imagesList = [];

			imagesList.AddRange(Images.Select(container => container.ToDictionary()));

			return imagesList.ToArray();
		}

		public static List<ImageInfo> Images { get; } = [];

		/// <summary>
		/// The last time this was updated. Used internally.
		/// </summary>
		public static DateTime LastUpdate { get; private set; }

		/// <summary>
		/// Returns whether the current data is too stale and should be updated.
		/// </summary>
		public static bool ShouldUpdate =>
			LastUpdate.AddSeconds(ApiConfig.ApiConfiguration.SecondsToUpdate) < DateTime.Now;

		private static Task? UpdatingTask { get; set; }
		private static readonly Lock UpdatingLock = new();
	}
	public sealed class ImageInfo
	{
		public required string Id { get; init; }
		public string ParentId { get; init; } = string.Empty;
		public string[] RepoTags { get; init; } = [];

		public required long Created { get; init; }
		public long Size { get; init; }
		public Dictionary<string, string>? Labels { get; init; }
		public int Containers { get; init; }

		// Bayt-specific

		public List<string> Names => GetNames(Labels, repoTags:RepoTags);
		public string? Description => GetDescription(Labels);
		public string? ImageUrl => GetImageUrl(Labels);
		public List<string> IconUrls => GetIconUrls(Labels);
		public string? ImageVersion => GetImageVersion(Labels, RepoTags);

		public Dictionary<string, dynamic?> ToDictionary()
		{
			return new()
			{
				{ nameof(Id), Id },

				{ nameof(Names), Names },
				{ nameof(Description), Description },
				{ nameof(ImageUrl), ImageUrl },
				{ nameof(IconUrls), IconUrls },
				{ nameof(ImageVersion), ImageVersion },

				{ nameof(ParentId), ParentId },
				{ nameof(RepoTags), RepoTags },

				{ nameof(Created), Created },
				{ nameof(Size), Size },
				{ nameof(Containers), Containers },

				{ nameof(ImagesInfo.LastUpdate), ImagesInfo.LastUpdate.ToUniversalTime() }
			};
		}
	}

	private static List<string> GetNames(Dictionary<string, string>? labelsDict, JsonElement? dockerOutput = null, string[]? repoTags = null)
	{
		List<string> names = [];
		if (labelsDict is not null)
		{
			foreach (var labelKvp in labelsDict)
			{
				switch (labelKvp.Key)
				{
					case "bayt.name":
					case "glance.name":
					case "homepage.name":
					case "com.docker.compose.project":
					case "org.opencontainers.image.title":
						names.Add(labelKvp.Value);
						break;
				}
			}
		}

		if (dockerOutput is not null && dockerOutput.Value.TryGetProperty(nameof(names), out var namesElement) && namesElement.GetArrayLength() > 0)
		{
			List<JsonElement> dockerNames = namesElement.EnumerateArray().ToList();
			dockerNames.RemoveAll(name => name.GetString() is null);

			// If we're going to have to use the Docker default name, let's clean it up first. (remove leading /, e.g. "/jellyfin" => "jellyfin")
			var firstName = dockerNames.First().GetString();
			if (names.Count == 0 && firstName!.StartsWith('/'))
			{
				names.Add(firstName[1..]);
				dockerNames = dockerNames.Skip(1).ToList();
			}

			// Add the rest of the names, if any.
			if (dockerNames.Count > 0)
			{
				names.AddRange(dockerNames.Select(name => name.GetString()!));
			}
		}

		if (repoTags is not null && repoTags.Length > 0)
		{
			var nameToAdd = repoTags.First();
			nameToAdd = nameToAdd.Split(':').First();

			names.Add(nameToAdd);
		}

		// If no names have been found yet, try to get the base image name at the very least.
		if (labelsDict is not null && names.Count == 0 && labelsDict.TryGetValue("org.opencontainers.image.ref.name", out var baseImageName))
		{
			names.Add(baseImageName);
		}

		names = names.Distinct().ToList();

		return names;
	}
	private static string? GetDescription(Dictionary<string, string>? labelsDict)
	{
		if (labelsDict is null) return null;
		if (labelsDict.TryGetValue("bayt.description", out var description)
		    || labelsDict.TryGetValue("glance.description", out description)
		    || labelsDict.TryGetValue("homepage.description", out description)
		    || labelsDict.TryGetValue("org.opencontainers.image.description", out description))
		{
			return description;
		}

		return null;
	}

	private static List<string> GetIconUrls(Dictionary<string, string>? labelsDict, string? preferredIconLink = null)
	{
		if (labelsDict is null) return [];
		List<string> iconUrls = [];
		foreach (var labelKvp in labelsDict)
		{
			switch (labelKvp.Key)
			{
				case "bayt.icon":
				case "glance.icon":
				case "com.docker.desktop.extension.icon":
					iconUrls.Add(GetUrlFromRepos(labelKvp.Value));
					break;
			}
		}

		if (preferredIconLink is not null && (iconUrls.Count == 0 || preferredIconLink != GenericIconLink))
			iconUrls.Add(GetUrlFromRepos(preferredIconLink));

		return iconUrls;

		string GetUrlFromRepos(string repoName)
		{
			if (repoName.StartsWith("http")) return repoName;
			if (repoName.StartsWith("di:"))
			{
				repoName = repoName[3..];
				return $"https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/svg/{repoName}.svg";
			}
			if (repoName.StartsWith("sh:"))
			{
				repoName = repoName[3..];
				return $"https://cdn.jsdelivr.net/gh/selfhst/icons/png/{repoName}.png";
			}
			if (repoName.StartsWith("si:"))
			{
				repoName = repoName[3..];
				return $"https://cdn.jsdelivr.net/npm/simple-icons@v15/icons/{repoName}.svg";
			}
			if (repoName.StartsWith("mdi:"))
			{
				repoName = repoName[4..];
				return $"https://api.iconify.design/mdi/{repoName}.svg";
			}

			return repoName;
		}
	}

	private static string? GetImageUrl(Dictionary<string, string>? labelsDict)
	{
		if (labelsDict is null) return null;

		if (labelsDict.TryGetValue("bayt.image.url", out var imageUrl) && imageUrl.StartsWith("http")
			|| labelsDict.TryGetValue("org.opencontainers.image.url", out imageUrl) && imageUrl.StartsWith("http")
			|| labelsDict.TryGetValue("org.opencontainers.image.source", out imageUrl) && imageUrl.StartsWith("http")
		    || labelsDict.TryGetValue("com.docker.extension.publisher-url", out imageUrl) && imageUrl.StartsWith("http"))
			return imageUrl;

		return null;
	}

	private static string? GetImageVersion(Dictionary<string, string>? labelsDict, string[]? repoTags = null)
	{
		if (labelsDict is not null)
		{
			if (labelsDict.TryGetValue("bayt.version", out var version)
			    || labelsDict.TryGetValue("org.opencontainers.image.version", out version))
			{
				return version;
			}
		}

		if (repoTags is null || repoTags.Length <= 0 || !repoTags[0].Contains(':')) return null;

		var repoTagVersion = repoTags[0].Split(':').Last();
		return repoTagVersion;
	}

	public sealed record DockerResponse
	{
		public HttpStatusCode Status { get; init; }
		public bool IsSuccess { get; init; }
		public string Body { get; init; } = "";
	}

	private static HttpClient GetDockerClient()
	{
		var handler = new SocketsHttpHandler
		{
			// ReSharper disable once UnusedParameter.Local
			ConnectCallback = async (context, cancellationToken) =>
			{
				var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
				await socket.ConnectAsync(new UnixDomainSocketEndPoint("/var/run/docker.sock"), cancellationToken);
				return new NetworkStream(socket, true);
			}
		};

		return new HttpClient(handler);
	}

	public static async Task StreamDockerLogs(string? containerId, bool? stdout, bool? stderr, bool? timestamps, HttpContext context)
	{
		var response = context.Response;
		if (containerId is null || containerId.Length < 12)
		{
			response.StatusCode = 400;
			response.ContentType = "text/plain";
			await response.WriteAsync("At least the first 12 characters of he container ID must be specified.", context.RequestAborted);
			return;
		}
		if (!IsDockerAvailable)
		{
			response.StatusCode = 500;
			return;
		}
		await DockerContainers.UpdateDataIfNecessary();

		if (DockerContainers.Containers.All(container => !container.Id.StartsWith(containerId)))
		{
			response.StatusCode = 404;
			return;
		}
		if (stdout is null && stderr is null && timestamps is null)
		{
			stdout = true;
			stderr = true;
			timestamps = false;
		}

		response.ContentType = "text/event-stream";
		response.Headers.Append("Cache-Control", "no-cache");
		response.Headers.Append("Connection", "keep-alive");

		var client = GetDockerClient();
		client.BaseAddress = new Uri("http://localhost");

		try
		{
			await using var stream = await client.GetStreamAsync($"/containers/{containerId}/logs?stdout={stdout}&stderr={stderr}&timestamps={timestamps}&follow=true");
			if (!stream.CanRead) throw new Exception("Docker UNIX socket is not readable.");

			byte[] logHeader = new byte[8];

			while (!context.RequestAborted.IsCancellationRequested)
			{
				var bytesRead = await stream.ReadAtLeastAsync(logHeader, 8, false, context.RequestAborted);
				if (bytesRead < 8)
				{
					break;
				}
				var payloadSize = (int) BinaryPrimitives.ReadUInt32BigEndian(logHeader.AsSpan(4));

				var payloadBuffer = new byte[payloadSize];
				await stream.ReadExactlyAsync(payloadBuffer, 0, payloadSize);

				await response.WriteAsync($"data: {Encoding.UTF8.GetString(payloadBuffer)}\n\n", context.RequestAborted);
				await response.Body.FlushAsync(context.RequestAborted);
			}
		}
		catch (Exception e)
		{
			response.StatusCode = 500;
			await response.WriteAsync("data: There was an error fetching the logs. The details will follow.", context.RequestAborted);
			await response.Body.FlushAsync(context.RequestAborted);
			await response.WriteAsync($"data: Error message:{e.Message}", context.RequestAborted);
			await response.Body.FlushAsync(context.RequestAborted);
		}
	}

	public static async Task<DockerResponse> SendRequest(string path, string method = "GET", string content = "")
	{
		if (path.StartsWith('/')) path = path[1..];
		if (!IsDockerAvailable) throw new FileNotFoundException("Docker socket not found. " +
		                                                         "Double check that the Docker daemon is running and that the socket is accessible.");

		var client = GetDockerClient();
		var clientResponse = method switch
		{
			"GET" => await client.GetAsync($"http://localhost/{path}"),
			"POST" => await client.PostAsync($"http://localhost/{path}", new StringContent(content)),
			"DELETE" => await client.DeleteAsync($"http://localhost/{path}"),
			_ => throw new ArgumentException("Method must be either GET, POST, or DELETE.")
		};

		byte[] fullResponse;
		await using (var stream = await clientResponse.Content.ReadAsStreamAsync())
		{
			if (!stream.CanRead) throw new Exception("Docker UNIX socket is not writable or not readable.");

			using var memoryStream = new MemoryStream();
			await stream.CopyToAsync(memoryStream);

			fullResponse = memoryStream.ToArray();
		}

		return new()
		{
			Status = clientResponse.StatusCode,
			IsSuccess = clientResponse.IsSuccessStatusCode,
			Body = Encoding.UTF8.GetString(fullResponse)
		};
	}
}
