using System;
using System.Net;
using System.Net.Http;
using System.Threading;

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
			ConnectTimeout = TimeSpan.FromSeconds(10),
			PooledConnectionLifetime = DefaultPooledConnectionLifetime
		};

		return new HttpClient(handler, disposeHandler: true)
		{
			MaxResponseContentBufferSize = MaxResponseBytes,
			Timeout = DefaultTimeout
		};
	}
}
