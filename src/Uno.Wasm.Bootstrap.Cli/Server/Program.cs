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
        public static IWebHost BuildWebHost(string[] args) =>
               WebHost.CreateDefaultBuilder(args)
                   .UseConfiguration(new ConfigurationBuilder()
                       .AddCommandLine(args)
                       .Build())
                   .UseStartup<Startup>()
                   .Build();
   }
}
