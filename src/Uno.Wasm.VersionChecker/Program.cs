using Colorful;
using ConsoleTables;
using HtmlAgilityPack;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Console = Colorful.Console;

namespace Uno.VersionChecker;

class Program
{
	static async Task<int> Main(string[] args)
	{
		var thisToolVersion = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		Console.WriteLineFormatted("Uno Version Checker v{0}.", Color.Gray, new Formatter(thisToolVersion, Color.Aqua));
		var webSiteUrl = args.FirstOrDefault()
#if DEBUG
			?? "https://nuget.info/";
#else
			;
#endif

		if(string.IsNullOrEmpty(webSiteUrl))
		{
			var module = global::System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
			Console.WriteLine($"Usage: {module} [application hostname or url]", Color.Yellow);
			Console.WriteLine($"Default scheme is https if not specified.");
			return 100;
		}

		if(!webSiteUrl.EndsWith('/'))
		{
			webSiteUrl += '/';
		}

		Console.WriteLineFormatted("Checking website at address {0}.", Color.Gray, new Formatter(webSiteUrl, Color.Aqua));

		UnoVersionExtractor extractor;

		try
		{

			var siteUri = BuildSiteUri(webSiteUrl);

			extractor = new UnoVersionExtractor(siteUri);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Unable to build Uri from {webSiteUrl}. {ex}", Color.Red);

			return 250;
		}

		try
		{
			var result = await extractor.Extract();
			if (result != 0)
			{
				return result; // an error occurred and the reason has been pushed to output
			}

			extractor.OutputResults();

			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Unable to read uno config: {ex}", Color.Red);
			return 255;
		}
	}

	private static Uri BuildSiteUri(string webSiteUrl)
	{
		Uri siteUri;

		try
		{
			siteUri = new Uri(webSiteUrl);

			if (siteUri.Scheme != Uri.UriSchemeHttp && siteUri.Scheme != Uri.UriSchemeHttps)
			{
				throw new UriFormatException();
			}
		}
		catch (UriFormatException)
		{
			siteUri = new Uri($"https://{webSiteUrl}");
		}

		return siteUri;
	}
}
