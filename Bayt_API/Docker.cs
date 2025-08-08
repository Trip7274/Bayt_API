using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Bayt_API;

public static class Docker
{
	public static bool IsDockerAvailable => File.Exists("/var/run/docker.sock");

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
		}

		public static async Task UpdateData()
		{
			var dockerRequest = await SendRequest("containers/json");
			if (!dockerRequest.IsSuccess) throw new Exception($"Docker request failed. ({dockerRequest.Status})\n Got body: {dockerRequest.Body}");
			var dockerOutput = JsonSerializer.Deserialize<JsonElement>(dockerRequest.Body);

			Containers.Clear();
			foreach (var containerEntries in dockerOutput.EnumerateArray())
			{
				Containers.Add(new DockerContainer(containerEntries));
			}
		}

		public static Dictionary<string, dynamic?>[] ToDictionary()
		{
			List<Dictionary<string, dynamic?>> containersList = [];
			containersList.AddRange(Containers.Select(container => container.ToDictionary()));

			return containersList.ToArray();
		}

		public static List<DockerContainer> Containers { get; } = [];
	}

	public sealed class DockerContainer
	{
		public DockerContainer(JsonElement dockerOutput)
		{
			var labelsElement = dockerOutput.GetProperty("Labels");

			Id = dockerOutput.GetProperty(nameof(Id)).GetString() ?? throw new ArgumentException("Docker container ID is null.");
			Names = dockerOutput.GetProperty(nameof(Names)).EnumerateArray().Select(x => x.GetString() ?? throw new ArgumentException("Docker container name is null.")).ToList();
			if (labelsElement.TryGetProperty("org.opencontainers.image.title", out var title))
			{
				Title = title.GetString();
			}

			Image = dockerOutput.GetProperty(nameof(Image)).GetString() ?? throw new ArgumentException("Docker container image is null.");
			ImageID = dockerOutput.GetProperty(nameof(ImageID)).GetString() ?? throw new ArgumentException("Docker container image ID is null.");
			ImageUrl = GetImageUrl(labelsElement);

			if (labelsElement.TryGetProperty("com.docker.compose.project.config_files", out var composePath))
			{
				ComposePath = composePath.GetString();
			}
			if (labelsElement.TryGetProperty("com.docker.compose.project.working_dir", out var workingPath))
			{
				WorkingPath = workingPath.GetString();
			}
			Command = dockerOutput.GetProperty(nameof(Command)).GetString() ?? throw new ArgumentException("Docker container command is null.");

			CreatedUnix = dockerOutput.GetProperty(nameof(Created)).GetInt64();
			DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(CreatedUnix);
			Created = dateTimeOffset.DateTime.ToUniversalTime();

			State = dockerOutput.GetProperty(nameof(State)).GetString() ?? throw new ArgumentException("Docker container state is null.");
			Status = dockerOutput.GetProperty(nameof(Status)).GetString() ?? throw new ArgumentException("Docker container status is null.");

			IconUrl = GetIconUrl(labelsElement);

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
			List<Dictionary<string, dynamic?>> portBindingsList = [];
			portBindingsList.AddRange(PortBindings.Select(portBinding => portBinding.ToDictionary()));

			List<Dictionary<string, string>> mountBindingsList = [];
			mountBindingsList.AddRange(MountBindings.Select(mountBinding => mountBinding.ToDictionary()));

			return new()
			{
				{ "Id", Id },
				{ "Names", Names },
				{ "Title", Title },

				{ "Image", Image },
				{ "ImageID", ImageID },
				{ "ImageUrl", ImageUrl },

				{ "Command", Command },
				{ "Created", Created },
				{ "CreatedUnix", CreatedUnix },

				{ "State", State },
				{ "Status", Status },

				{ "IconUrl", IconUrl },

				{ "IpAddress", IpAddress.ToString() },
				{ "NetworkMode", NetworkMode },
				{ "PortBindings", portBindingsList },
				{ "MountBindings", mountBindingsList },
			};
		}

		private static string? GetIconUrl(JsonElement labelsElement)
		{
			string? iconUrl = null;

			if (labelsElement.TryGetProperty("com.docker.desktop.extension.icon", out var iconElement)
			    || labelsElement.TryGetProperty("glance.icon", out iconElement))
			{
				iconUrl = iconElement.GetString();
			}

			if (iconUrl is null || iconUrl.StartsWith("http")) return iconUrl;

			if (iconUrl.StartsWith("di:"))
			{
				iconUrl = iconUrl[3..];
				return $"https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/svg/{iconUrl}.svg";
			}
			if (iconUrl.StartsWith("sh:"))
			{
				iconUrl = iconUrl[3..];
				return $"https://cdn.jsdelivr.net/gh/selfhst/icons/svg/{iconUrl}.svg";
			}
			if (iconUrl.StartsWith("si:"))
			{
				iconUrl = iconUrl[3..];
				return $"https://cdn.jsdelivr.net/npm/simple-icons@v15/icons/{iconUrl}.svg";
			}
			return null;
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

		public string Id { get; }
		public List<string> Names { get; }
		public string? Title { get; }

		public string Image { get; }
		// ReSharper disable once InconsistentNaming
		public string ImageID { get; }
		public string? ImageUrl { get; }

		public string? ComposePath { get; }
		public string? WorkingPath { get; }
		public string Command { get; }
		public DateTime Created { get; }
		public long CreatedUnix { get; }

		public string State { get; }
		public string Status { get; }

		public string? IconUrl { get; }

		public IPAddress IpAddress { get; }

		public string NetworkMode { get; }
		public List<PortBinding> PortBindings { get; } = [];
		public List<MountBinding> MountBindings { get; } = [];
	}

	public sealed class PortBinding
	{
		public PortBinding(JsonElement portEntry)
		{
			if (portEntry.TryGetProperty("IP", out var ipAddr) && IPAddress.TryParse(ipAddr.GetString(), out var ip))
			{
				IpAddress = ip.ToString();
			}

			if (portEntry.TryGetProperty(nameof(PrivatePort), out var privatePort))
			{
				PrivatePort = privatePort.GetUInt16();
			}
			if (portEntry.TryGetProperty(nameof(PrivatePort), out var publicPort))
			{
				PublicPort = publicPort.GetUInt16();
			}
			if (portEntry.TryGetProperty(nameof(Type), out var bindingType))
			{
				Type = bindingType.GetString() ?? throw new ArgumentException("Docker container IP address is null.");
			}


		}

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

		public string? IpAddress { get; }
		public ushort? PrivatePort { get; }
		public ushort? PublicPort { get; }
		public string? Type { get; }
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
		public ushort Status { get; set; }
		public bool IsSuccess => Status is >= 200 and < 300;
		public string Body { get; set; } = "";
	}

	public static async Task<DockerResponse> SendRequest(string path, string method = "GET")
	{
		if (method != "GET" && method != "POST") throw new ArgumentException("Method must be either GET or POST.");
		if (path.StartsWith('/')) path = path[1..];
		if (!IsDockerAvailable) throw new FileNotFoundException("Docker socket not found. " +
		                                                         "Double check that the Docker daemon is running and that the socket is accessible.");

		string requestString = $"{method} /{path} HTTP/1.0\r\n" +
		                       "Host: localhost\r\n" +
		                       "User-Agent: Bayt_API\r\n" +
		                       "Accept: */*\r\n" +
		                       "\r\n";

		byte[] request = Encoding.UTF8.GetBytes(requestString);

		List<byte> fullResponse = new List<byte>();
		int bytesRead = 0;

		var dockerSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		await dockerSocket.ConnectAsync(new UnixDomainSocketEndPoint("/var/run/docker.sock"));

		await using (var stream = new NetworkStream(dockerSocket, false))
		{
			if (!stream.CanWrite || !stream.CanRead) throw new Exception("Docker UNIX socket is not writable or not readable.");

			await stream.WriteAsync(request);
			while (stream.DataAvailable || bytesRead == 0)
			{
				byte[] responseCache = new byte[1024];
				bytesRead = await stream.ReadAsync(responseCache);
				fullResponse.AddRange(responseCache[..bytesRead]);
			}
		}
		await dockerSocket.DisconnectAsync(false);
		dockerSocket.Dispose();

		return ProcessHttpRequest(Encoding.UTF8.GetString(fullResponse.ToArray()));
	}

	private static DockerResponse ProcessHttpRequest(string responseString)
	{
		List<string> responseLines = responseString.Split('\n').ToList();
		if (!responseLines[0].StartsWith("HTTP/1.0"))
		{
			throw new ArgumentException("Invalid HTTP response text.");
		}

		DockerResponse dockerResponse = new()
		{
			Status = ushort.Parse(responseLines[0].Split(' ')[1])
		};

		var bodyStartIndex = responseLines.IndexOf("\r");
		if (bodyStartIndex == -1) throw new ArgumentException("Invalid HTTP response text.");

		responseLines = responseLines.Skip(bodyStartIndex + 1).ToList();

		dockerResponse.Body = string.Join("\n", responseLines);

		return dockerResponse;
	}
}
