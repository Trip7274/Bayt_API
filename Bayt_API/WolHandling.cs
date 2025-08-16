using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Bayt_API;

public static class WolHandling
{
	public class WolClient
	{
		public string? Name { get; init; }
		public required PhysicalAddress PhysicalAddress { get; init; }
		public required IPAddress IpAddress { get; init; }
		public required IPAddress SubnetMask { get; init; }
		public IPAddress? BroadcastAddress { get; set; }
	}

	public static void WakeClient(WolClient wolClient)
	{
		// Magic Packet creation
		List<byte> magicPacket = [255, 255, 255, 255, 255, 255];
		for (byte i = 0; i < 16; i++)
		{
			magicPacket.AddRange(wolClient.PhysicalAddress.GetAddressBytes());
		}

		using var client = new UdpClient();
		client.EnableBroadcast = true;

		var directedBroadcastAddress = wolClient.BroadcastAddress ?? GetBroadcastAddress(ref wolClient);

		byte[] magicPacketBytes = magicPacket.ToArray();
		client.Send(magicPacketBytes, new IPEndPoint(directedBroadcastAddress, 7));
		client.Send(magicPacketBytes, new IPEndPoint(directedBroadcastAddress, 9));
		client.Send(magicPacketBytes, new IPEndPoint(directedBroadcastAddress, 40000));
	}

	private static IPAddress GetBroadcastAddress(ref WolClient wolClient)
	{
		if (wolClient.IpAddress.AddressFamily != wolClient.SubnetMask.AddressFamily)
		{
			throw new ArgumentException("Both addresses must be of the same family.");
		}

		byte[] ipAddressBytes = wolClient.IpAddress.GetAddressBytes();
		byte[] subnetMaskBytes = wolClient.SubnetMask.GetAddressBytes();

		var broadcastAddressBytes = new byte[ipAddressBytes.Length];
		for (int i = 0; i < broadcastAddressBytes.Length; i++)
		{
			// Apply the subnet mask to the IP address to get the broadcast address. (192.168.1.x -> 192.168.1.255)
			broadcastAddressBytes[i] = (byte) (ipAddressBytes[i] | (subnetMaskBytes[i] ^ 255));
		}
		var broadcastAddress = new IPAddress(broadcastAddressBytes);

		ApiConfig.ApiConfiguration.UpdateBroadcastAddress(wolClient, broadcastAddress.ToString());

		return broadcastAddress;
	}
}