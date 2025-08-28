using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Bayt_API;

public static class Docker
{
	public static bool IsDockerAvailable => File.Exists("/var/run/docker.sock") && ApiConfig.ApiConfiguration.DockerIntegrationEnabled;

	public static class DockerContainers
	{
		static DockerContainers()
		{
			var dockerRequest = SendRequest("containers/json").Result;
			if (!dockerRequest.IsSuccess) throw new Exception($"Docker request failed. ({dockerRequest.Status})\n Got body: {dockerRequest.Body}");
			var dockerOutput = JsonSerializer.Deserialize<JsonElement>(dockerRequest.Body);

			foreach (var containerEntries in dockerOutput.EnumerateArray())
			{
				Containers.Add(new DockerContainer(containerEntries));
			}
			LastUpdate = DateTime.Now + TimeSpan.FromSeconds(ApiConfig.ApiConfiguration.ClampedSecondsToUpdate);
		}

		public static async Task UpdateData(bool getAllContainers = true)
		{
			var dockerRequest = await SendRequest($"containers/json?all={getAllContainers}");
			if (!dockerRequest.IsSuccess) throw new Exception($"Docker request failed. ({dockerRequest.Status})\n Got body: {dockerRequest.Body}");

			var dockerOutput = JsonSerializer.Deserialize<JsonElement>(dockerRequest.Body);

			Containers.Clear();
			foreach (var containerEntries in dockerOutput.EnumerateArray())
			{
				Containers.Add(new DockerContainer(containerEntries));
			}
			LastUpdate = DateTime.Now;
		}

		public static async Task UpdateDataIfNecessary(bool getAllContainers = true)
		{
			if (!ShouldUpdate) return;

			await UpdateData(getAllContainers);
		}

		public static Dictionary<string, dynamic?>[] ToDictionary()
		{
			List<Dictionary<string, dynamic?>> containersList = [];
			containersList.AddRange(Containers.Select(container => container.ToDictionary()));

			return containersList.ToArray();
		}

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
			var labelsElement = dockerOutput.GetProperty("Labels");

			if (labelsElement.TryGetProperty("com.docker.compose.project.config_files", out var composePath) && File.Exists(composePath.GetString()))
			{
				ComposePath = composePath.GetString();
			}
			if (labelsElement.TryGetProperty("com.docker.compose.project.working_dir", out var workingPath))
			{
				WorkingPath = workingPath.GetString();
			}
			if (ComposePath is not null)
			{
				IsCompose = true;
				var composeDirectory = Path.GetDirectoryName(ComposePath);
				IsManaged = composeDirectory is not null && File.Exists(Path.Combine(composeDirectory, ".BaytMetadata"));
			}

			GetContainerMetadata();
			GetContainerNames(dockerOutput, labelsElement);
			GetContainerDescription(labelsElement);
			GetContainerHrefs(labelsElement);
			GetIconUrls(labelsElement);

			Id = dockerOutput.GetProperty(nameof(Id)).GetString() ?? throw new ArgumentException("Docker container ID is null.");

			Image = dockerOutput.GetProperty(nameof(Image)).GetString() ?? throw new ArgumentException("Docker container image is null.");
			ImageID = dockerOutput.GetProperty(nameof(ImageID)).GetString() ?? throw new ArgumentException("Docker container image ID is null.");
			ImageUrl = GetImageUrl(labelsElement);
			if (labelsElement.TryGetProperty("org.opencontainers.image.version", out var versionLabel))
			{
				ImageVersion = versionLabel.GetString();
			}

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
				{ nameof(Title), Title },

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

				{ nameof(Notes), Notes },
				{ nameof(ContainerWebpageLinks), ContainerWebpageLinks },

				{ nameof(DockerContainers.LastUpdate), DockerContainers.LastUpdate.ToUniversalTime() }
			};
		}

		private void GetIconUrls(JsonElement labelsElement)
		{
			if (IsManaged && IconUrl is not null) IconUrls.Add(IconUrl);
			if (labelsElement.TryGetProperty("bayt.icon", out var iconElement))
			{
				var baytIconUrl = iconElement.GetString();
				if (baytIconUrl is not null) IconUrls.Add(GetUrlFromRepos(baytIconUrl));
			}
			if (labelsElement.TryGetProperty("glance.icon", out iconElement))
			{
				var glanceIconUrl = iconElement.GetString();
				if (glanceIconUrl is not null) IconUrls.Add(GetUrlFromRepos(glanceIconUrl));
			}
			if (labelsElement.TryGetProperty("com.docker.desktop.extension.icon", out iconElement))
			{
				var desktopIconUrl = iconElement.GetString();
				if (desktopIconUrl is not null) IconUrls.Add(GetUrlFromRepos(desktopIconUrl));
			}

			return;

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

				return repoName;
			}
		}
		private static string? GetImageUrl(JsonElement labelsElement)
		{
			string? imageUrl = null;

			if (labelsElement.TryGetProperty("org.opencontainers.image.url", out var imageUrlElement)
			    || labelsElement.TryGetProperty("com.docker.extension.publisher-url", out imageUrlElement))
			{
				imageUrl = imageUrlElement.GetString();
			}

			if (imageUrl is not null && !imageUrl.StartsWith("http")) return null;

			return imageUrl;
		}
		private static IPAddress GetIp(JsonElement networkSettingsElement, string networkName)
		{
			var machineLocalIp = StatsApi.GetLocalIpAddress();

			if (!networkSettingsElement.GetProperty("Networks").GetProperty(networkName).TryGetProperty("IPAddress", out var ipElement)
			    || string.IsNullOrEmpty(ipElement.GetString())) return machineLocalIp;


			var ip = ipElement.GetString();
			return ip is null ? machineLocalIp : IPAddress.Parse(ip);

		}
		private void GetContainerNames(JsonElement dockerOutput, JsonElement labelsElement)
		{
			if (IsManaged && PrettyName is not null)
			{
				Names.Add(PrettyName);
			}

			foreach (var labelKvp in labelsElement.EnumerateObject())
			{
				switch (labelKvp.Name)
				{
					case "bayt.name":
					case "glance.name":
					case "homepage.name":
						Names.Add(labelKvp.Value.GetString()!);
						break;
					case "org.opencontainers.image.title":
						Title = labelKvp.Value.GetString();
						if (Title is not null) Names.Add(Title);
						break;
				}
			}

			if (dockerOutput.TryGetProperty(nameof(Names), out var namesElement) && namesElement.GetArrayLength() > 0)
			{
				List<JsonElement> dockerNames = namesElement.EnumerateArray().ToList();
				dockerNames.RemoveAll(name => name.GetString() is null);

				// If we're going to have to use the Docker default name, let's clean it up first. (remove leading /, e.g. "/jellyfin" => "jellyfin")
				var firstName = dockerNames.First().GetString();
				if (Names.Count == 0 && firstName!.StartsWith('/'))
				{
					Names.Add(firstName[1..]);
					dockerNames = dockerNames.Skip(1).ToList();
				}

				// Add the rest of the names, if any.
				if (dockerNames.Count > 0)
				{
					Names.AddRange(dockerNames.Select(name => name.GetString()!));
				}
			}
			Names = Names.Distinct().ToList();
		}
		private void GetContainerHrefs(JsonElement labelsElement)
		{
			foreach (var labelKvp in labelsElement.EnumerateObject())
			{
				switch (labelKvp.Name)
				{
					case "bayt.url":
					case "glance.url":
					case "homepage.instance.internal.href":
						ContainerWebpageLinks.Add(labelKvp.Value.GetString()!);
						break;
				}
			}
			ContainerWebpageLinks = ContainerWebpageLinks.Distinct().ToList();
		}
		private void GetContainerDescription(JsonElement labelsElement)
		{
			foreach (var labelKvp in labelsElement.EnumerateObject())
			{
				ImageDescription = labelKvp.Name switch
				{
					"bayt.description" or "glance.description" or "homepage.description"
						or "org.opencontainers.image.description" => labelKvp.Value.GetString()!,
					_ => ImageDescription
				};
			}
		}

		private void GetContainerMetadata()
		{
			if (!IsManaged || ComposePath is null) return;

			string composeMetadataFile = Path.Combine(Path.GetDirectoryName(ComposePath) ?? "/", ".BaytMetadata");
			if (!File.Exists(composeMetadataFile)) return;
			var lines = File.ReadAllLines(composeMetadataFile).ToList();

			var prettyNameLine = lines.FirstOrDefault(line => line.StartsWith("Pretty name"));
			PrettyName = ExtractProp(prettyNameLine);

			var notesLine = lines.FirstOrDefault(line => line.StartsWith("Notes"));
			Notes = ExtractProp(notesLine);

			var iconUrlLine = lines.FirstOrDefault(line => line.StartsWith("Icon URL"));
			IconUrl = ExtractProp(iconUrlLine);

			var containerWebpageLinkLine = lines.FirstOrDefault(line => line.StartsWith("Webpage URL"));
			var containerWebpageLink = ExtractProp(containerWebpageLinkLine);
			if (containerWebpageLink is not null) ContainerWebpageLinks.Add(containerWebpageLink);

			return;

			static string? ExtractProp(string? line)
			{
				if (line is null || line.Length == 0 || !line.Contains(':') || !line.Contains('"')) return null;

				var colonIndex = line.IndexOf(':');
				var firstIndex = line.IndexOf('"');
				var lastIndex = line.LastIndexOf('"');

				// Make sure that the order is at least: '${key}: "${value}"'
				if (colonIndex > firstIndex || colonIndex > lastIndex
				                             || lastIndex < firstIndex
				                             || string.IsNullOrWhiteSpace(line[(firstIndex + 1)..lastIndex]))
				{
					return null;
				}

				return line[(firstIndex + 1)..lastIndex];
			}
		}

		public string Id { get; }
		public List<string> Names { get; private set; } = [];
		public string? Title { get; private set; }

		public string Image { get; }
		// ReSharper disable once InconsistentNaming
		public string ImageID { get; }
		public string? ImageUrl { get; }
		public string? ImageVersion { get;  }
		public string? ImageDescription { get; private set; }

		public string? ComposePath { get; }
		public string? WorkingPath { get; }
		public string Command { get; }
		public DateTime Created { get; }
		public long CreatedUnix { get; }

		public string State { get; }
		public string Status { get; }

		public bool IsCompose { get; }
		public bool IsManaged { get; }

		public List<string> IconUrls { get; } = [];

		public IPAddress IpAddress { get; }

		public string NetworkMode { get; }
		public List<PortBinding> PortBindings { get; } = [];
		public List<MountBinding> MountBindings { get; } = [];
		public ContainerStats Stats => new(Id);

		public string? PrettyName { get; private set; }
		public string? Notes { get; private set; }
		public string? IconUrl { get; private set; }
		public List<string> ContainerWebpageLinks { get; private set; } = [];
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
