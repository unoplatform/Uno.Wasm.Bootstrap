using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Uno.VersionChecker;

public static class VersionCheckHttp
{
	public const int MaxResponseBytes = 64 * 1024 * 1024;
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
	public static readonly TimeSpan DefaultPooledConnectionLifetime = TimeSpan.FromMinutes(5);

	public static HttpClient CreateDefaultHttpClient()
	{
		var handler = new SocketsHttpHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
			AllowAutoRedirect = false,
			ConnectTimeout = TimeSpan.FromSeconds(10),
			PooledConnectionLifetime = DefaultPooledConnectionLifetime,
			ConnectCallback = ConnectAsync
		};

		return new HttpClient(handler, disposeHandler: true)
		{
			MaxResponseContentBufferSize = MaxResponseBytes,
			Timeout = DefaultTimeout
		};
	}

	private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
	{
		var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
		var allowedAddresses = addresses
			.Where(VersionCheckNetworkPolicy.IsPublicAddress)
			.ToArray();

		if (allowedAddresses.Length == 0)
		{
			throw new HttpRequestException($"Target host '{context.DnsEndPoint.Host}' resolves only to local or private network addresses.");
		}

		Exception? lastError = null;
		foreach (var address in allowedAddresses)
		{
			var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			try
			{
				await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
				return new NetworkStream(socket, ownsSocket: true);
			}
			catch (Exception ex)
			{
				lastError = ex;
				socket.Dispose();
			}
		}

		throw new HttpRequestException($"Unable to connect to '{context.DnsEndPoint.Host}:{context.DnsEndPoint.Port}'.", lastError);
	}
}
