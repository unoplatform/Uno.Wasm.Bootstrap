// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

//
// Imported from https://github.com/aspnet/Blazor/tree/release/0.7.0
//
// History:
//  - 2019-01-14: update for Uno support
//

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.CommandLineUtils;
using System.Threading.Tasks;

namespace Uno.Wasm.Bootstrap.Cli.Commands
{
    internal class ServeCommand : CommandLineApplication
    {
        public ServeCommand(CommandLineApplication parent)

            // We pass arbitrary arguments through to the ASP.NET Core configuration
            : base(throwOnUnexpectedArg: false)
        {
            Parent = parent;

            Name = "serve";
            Description = "Serve requests to an Uno WebAssembly application";

            HelpOption("-?|-h|--help");

            OnExecute(() => Execute());
        }

        private int Execute()
        {
            Server.Program.BuildWebHost(RemainingArguments.ToArray()).Run();
            return 0;
        }
    }
}