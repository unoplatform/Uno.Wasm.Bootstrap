using Microsoft.Extensions.CommandLineUtils;
using System;
using Uno.Wasm.Bootstrap.Cli.Commands;

namespace Uno.Wasm.Bootstrap.Cli
{
    class Program
    {
        static int Main(string[] args)
        {
            var app = new CommandLineApplication(throwOnUnexpectedArg: false)
            {
                Name = "unowasm-cli"
            };
            app.HelpOption("-?|-h|--help");

            app.Commands.Add(new ServeCommand(app));

            app.OnExecute(() =>
            {
                app.ShowHelp();
                return 0;
            });

            try
            {
                return app.Execute(args);
            }
            catch (CommandParsingException cex)
            {
                app.Error.WriteLine(cex.Message);
                app.ShowHelp();
                return 1;
            }
        }
    }
}
