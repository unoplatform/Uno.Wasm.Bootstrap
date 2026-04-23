#if NET10_0
using System.Reflection;

namespace Uno.VersionChecker;

internal static class Program
{
	private static int Main(string[] args)
	{
		var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		var app = VersionCheckerReplHost.CreateApp(version);
		return app.Run(args);
	}
}
#endif
