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

		var fields = UnoVersionExtractor.ParseUnoConfigFields(content);

		Assert.AreEqual("dotnet.abc123.js", fields.dotnetJsFilename);
		Assert.AreEqual("/myapp", fields.packagePath);
	}

	[TestMethod]
	public void When_ConfigWithoutDotnetJsFilename_Then_FieldIsNull()
	{
		var content = """
			config.uno_app_base = "/myapp";
			config.uno_remote_managedpath = "managed";
			""";

		var fields = UnoVersionExtractor.ParseUnoConfigFields(content);

		Assert.IsNull(fields.dotnetJsFilename);
		Assert.AreEqual("/myapp", fields.packagePath);
		Assert.AreEqual("managed", fields.managePath);
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

		var fields = UnoVersionExtractor.ParseUnoConfigFields(content);

		Assert.IsNotNull(fields.assemblies);
		Assert.AreEqual(2, fields.assemblies.Length);
		CollectionAssert.Contains(fields.assemblies, "MyApp.dll");
		CollectionAssert.Contains(fields.assemblies, "System.Runtime.dll");
	}

	[TestMethod]
	public void When_ConfigWithAllFields_Then_ExtractsEverything()
	{
		var content = """
			config.uno_app_base = "/myapp";
			config.uno_remote_managedpath = "managed";
			config.uno_main = "[MyApp.Wasm]MyApp.Program:Main";
			config.assemblies_with_size = {"MyApp.dll":100};
			config.dotnet_js_filename = "dotnet.xyz789.js";
			""";

		var fields = UnoVersionExtractor.ParseUnoConfigFields(content);

		Assert.AreEqual("/myapp", fields.packagePath);
		Assert.AreEqual("managed", fields.managePath);
		Assert.AreEqual("MyApp.Wasm", fields.mainAssembly);
		Assert.IsNotNull(fields.assemblies);
		Assert.AreEqual(1, fields.assemblies.Length);
		Assert.AreEqual("MyApp.dll", fields.assemblies[0]);
		Assert.AreEqual("dotnet.xyz789.js", fields.dotnetJsFilename);
	}

	[TestMethod]
	public void When_EmptyContent_Then_AllFieldsNull()
	{
		var fields = UnoVersionExtractor.ParseUnoConfigFields("");

		Assert.IsNull(fields.managePath);
		Assert.IsNull(fields.packagePath);
		Assert.IsNull(fields.mainAssembly);
		Assert.IsNull(fields.assemblies);
		Assert.IsNull(fields.dotnetJsFilename);
	}

	[TestMethod]
	public void When_LinesWithoutEquals_Then_IgnoredGracefully()
	{
		var content = """
			// This is a comment
			config.uno_app_base = "/app";
			some random text
			""";

		var fields = UnoVersionExtractor.ParseUnoConfigFields(content);

		Assert.AreEqual("/app", fields.packagePath);
	}
}
