using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Uno.VersionChecker;

internal static class VersionCheckNetworkPolicy
{
	public static bool IsSafePublicTarget(Uri siteUri, out string? error)
	{
		const string message = "Target resolves to a local or private network address.";
		error = null;

		if (string.Equals(siteUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
		{
			error = message;
			return false;
		}

		if (IPAddress.TryParse(siteUri.Host, out var address))
		{
			if (!IsPublicAddress(address))
			{
				error = message;
				return false;
			}

			return true;
		}

		try
		{
			var addresses = Dns.GetHostAddresses(siteUri.DnsSafeHost);
			if (addresses.Any(addressCandidate => !IsPublicAddress(addressCandidate)))
			{
				error = message;
				return false;
			}
		}
		catch (SocketException)
		{
			// Keep DNS failures as connection-time errors. ConnectCallback validates again with the resolved endpoints.
		}

		return true;
	}

	public static bool IsPublicAddress(IPAddress address)
	{
		if (address.IsIPv4MappedToIPv6)
		{
			address = address.MapToIPv4();
		}

		if (IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address) || IPAddress.IPv6None.Equals(address))
		{
			return false;
		}

		if (IPAddress.IsLoopback(address))
		{
			return false;
		}

		if (address.AddressFamily == AddressFamily.InterNetworkV6)
		{
			if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
			{
				return false;
			}

			var bytes = address.GetAddressBytes();
			return (bytes[0] & 0xFE) != 0xFC;
		}

		var ipv4Bytes = address.GetAddressBytes();
		return !(ipv4Bytes[0] == 0
			|| ipv4Bytes[0] == 10
			|| ipv4Bytes[0] == 127
			|| (ipv4Bytes[0] == 100 && ipv4Bytes[1] >= 64 && ipv4Bytes[1] <= 127)
			|| (ipv4Bytes[0] == 169 && ipv4Bytes[1] == 254)
			|| (ipv4Bytes[0] == 172 && ipv4Bytes[1] >= 16 && ipv4Bytes[1] <= 31)
			|| (ipv4Bytes[0] == 192 && ipv4Bytes[1] == 168)
			|| ipv4Bytes[0] >= 224);
	}

	public static Uri EnsureSameOrigin(Uri siteUri, Uri candidate, string sourceName)
	{
		if (Uri.Compare(siteUri, candidate, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
		{
			throw new InvalidOperationException($"Remote field '{sourceName}' points outside the inspected site.");
		}

		return candidate;
	}

	public static Uri ResolveTrustedUri(Uri siteUri, Uri baseUri, string relativeOrAbsolute, string sourceName) =>
		EnsureSameOrigin(siteUri, new Uri(baseUri, relativeOrAbsolute), sourceName);
}
