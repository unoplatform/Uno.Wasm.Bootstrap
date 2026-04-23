using System.Net;
using System.Net.Http;

namespace Uno.VersionChecker;

public static class VersionCheckHttp
{
	public static HttpClient CreateDefaultHttpClient()
	{
		var handler = new SocketsHttpHandler
		{
			AutomaticDecompression = DecompressionMethods.All
		};

		return new HttpClient(handler, disposeHandler: true);
	}
}
