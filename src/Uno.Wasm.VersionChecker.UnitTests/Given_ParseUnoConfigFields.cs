using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_ParseUnoConfigFields
{
	[TestMethod]
	public void When_ConfigWithDotnetJsFilename_Then_ExtractsIt()
	{
		var content = """
			config.dotnet_js_filename = "dotnet.abc123.js";
			config.uno_app_base = "/myapp";
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		Assert.AreEqual("dotnet.abc123.js", fields.DotnetJsFilename);
		Assert.AreEqual("/myapp", fields.PackagePath);
	}

	[TestMethod]
	public void When_ConfigWithoutDotnetJsFilename_Then_FieldIsNull()
	{
		var content = """
			config.uno_app_base = "/myapp";
			config.uno_remote_managedpath = "managed";
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		Assert.IsNull(fields.DotnetJsFilename);
		Assert.AreEqual("/myapp", fields.PackagePath);
		Assert.AreEqual("managed", fields.ManagePath);
	}

	[TestMethod]
	public void When_ConfigWithAssembliesWithSize_Then_ExtractsAssemblyNames()
	{
		var content = """
			config.uno_app_base = "/app";
			config.uno_remote_managedpath = "managed";
			config.uno_main = "[MyApp.Wasm]MyApp.Program:Main";
			config.assemblies_with_size = {"MyApp.dll":12345,"System.Runtime.dll":67890};
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		Assert.IsNotNull(fields.Assemblies);
		Assert.AreEqual(2, fields.Assemblies.Length);
		CollectionAssert.Contains(fields.Assemblies, "MyApp.dll");
		CollectionAssert.Contains(fields.Assemblies, "System.Runtime.dll");
	}

	[TestMethod]
	public void When_ConfigWithAllFields_Then_ExtractsEverything()
	{
		var content = """
			config.uno_app_base = "/myapp";
			config.uno_remote_managedpath = "managed";
			config.uno_main = "[MyApp.Wasm]MyApp.Program:Main";
			config.dotnet_js_filename = "dotnet.xyz789.js";
			config.assemblies_with_size = {"MyApp.dll":100};
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		Assert.AreEqual("/myapp", fields.PackagePath);
		Assert.AreEqual("managed", fields.ManagePath);
		Assert.AreEqual("MyApp.Wasm", fields.MainAssembly);
		Assert.IsNotNull(fields.Assemblies);
		Assert.AreEqual(1, fields.Assemblies.Length);
		Assert.AreEqual("MyApp.dll", fields.Assemblies[0]);
		Assert.AreEqual("dotnet.xyz789.js", fields.DotnetJsFilename);
	}

	[TestMethod]
	public void When_ConfigWithoutDotnetJsFilename_Then_EarlyBreakStillWorks()
	{
		var content = """
			config.uno_app_base = "/myapp";
			config.uno_remote_managedpath = "managed";
			config.uno_main = "[MyApp.Wasm]MyApp.Program:Main";
			config.assemblies_with_size = {"MyApp.dll":100};
			config.some_other_field = "should not be reached";
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		Assert.AreEqual("/myapp", fields.PackagePath);
		Assert.AreEqual("managed", fields.ManagePath);
		Assert.AreEqual("MyApp.Wasm", fields.MainAssembly);
		Assert.IsNotNull(fields.Assemblies);
		Assert.IsNull(fields.DotnetJsFilename);
	}

	[TestMethod]
	public void When_EmptyContent_Then_AllFieldsNull()
	{
		var fields = VersionCheckService.ParseUnoConfigFields("");

		Assert.IsNull(fields.ManagePath);
		Assert.IsNull(fields.PackagePath);
		Assert.IsNull(fields.MainAssembly);
		Assert.IsNull(fields.Assemblies);
		Assert.IsNull(fields.DotnetJsFilename);
	}

	[TestMethod]
	public void When_LinesWithoutEquals_Then_IgnoredGracefully()
	{
		var content = """
			// This is a comment
			config.uno_app_base = "/app";
			some random text
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		Assert.AreEqual("/app", fields.PackagePath);
	}
}
