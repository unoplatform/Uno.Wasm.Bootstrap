using System.Reflection;
using Uno.VersionChecker;

var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
var app = VersionCheckerReplHost.CreateApp(version);
return app.Run(args);
