#if !NET10_0
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace Uno.VersionChecker;

internal static class Program
{
	private static async Task<int> Main(string[] args)
	{
		var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		ConsoleReportWriter.WriteBanner(version);

		var input = args.FirstOrDefault()
#if DEBUG
			?? "https://nuget.info/";
#else
			;
#endif

		if (string.IsNullOrWhiteSpace(input))
		{
			ConsoleReportWriter.WriteUsage(Assembly.GetEntryAssembly()?.GetName().Name ?? "uno-wasm-version");
			return 100;
		}

		if (!VersionCheckTarget.TryParse(input, out var target, out var error))
		{
			Colorful.Console.Error.WriteLine(error ?? $"Invalid target '{input}'.", System.Drawing.Color.Red);
			return 250;
		}

		ConsoleReportWriter.WriteCheckingTarget(target!);

		try
		{
			using var httpClient = new HttpClient();
			using var service = new VersionCheckService(httpClient);
			var report = await service.InspectAsync(target!);
			ConsoleReportWriter.WriteSuccessPreamble(report);
			ConsoleReportWriter.WriteResults(report);
			return 0;
		}
		catch (InvalidOperationException ex)
		{
			Colorful.Console.Error.WriteLine(ex.Message, System.Drawing.Color.Red);
			return 1;
		}
		catch (Exception ex)
		{
			Colorful.Console.Error.WriteLine($"Unable to read uno config: {ex}", System.Drawing.Color.Red);
			return 255;
		}
	}
}
#endif
