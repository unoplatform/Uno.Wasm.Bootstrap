using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Uno.Wasm.Bootstrap.Cli.Server
{
    class Program
    {
		public static IWebHost BuildWebHost(string[] args)
		{
			var initialData = new Dictionary<string, string>()
			{
				[WebHostDefaults.EnvironmentKey] = "Development",
				["Logging:LogLevel:Microsoft"] = "Warning",
				["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Information",
			};

			return WebHost.CreateDefaultBuilder(args)
			.UseConfiguration(new ConfigurationBuilder()
				.AddCommandLine(args)
				.AddInMemoryCollection(initialData)
				.Build())
			.UseStartup<Startup>()
			.Build();
		}
	}
}
