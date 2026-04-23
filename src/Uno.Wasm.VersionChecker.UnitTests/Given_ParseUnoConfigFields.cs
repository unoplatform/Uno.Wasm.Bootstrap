using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_ParseUnoConfigFields
{
	[TestMethod]
	[Description("Regression guard: verifies dotnet.js filenames are extracted from uno-config.js.")]
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
	[Description("Regression guard: verifies optional dotnet.js filenames stay null when absent.")]
	public void When_ConfigWithoutDotnetJsFilename_Then_FieldIsNull()
	{
		var content = """
			config.uno_app_base = "/myapp";
			config.uno_remote_managedpath = "managed";
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		Assert.IsNull(fields.DotnetJsFilename);
		Assert.AreEqual("/myapp", fields.PackagePath);
		Assert.AreEqual("managed", fields.ManagedPath);
	}

	[TestMethod]
	[Description("Regression guard: verifies assemblies_with_size keys are preserved as assembly names.")]
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
	[Description("Regression guard: verifies all supported uno-config fields can be parsed together.")]
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
		Assert.AreEqual("managed", fields.ManagedPath);
		Assert.AreEqual("MyApp.Wasm", fields.MainAssembly);
		Assert.IsNotNull(fields.Assemblies);
		Assert.AreEqual(1, fields.Assemblies.Length);
		Assert.AreEqual("MyApp.dll", fields.Assemblies[0]);
		Assert.AreEqual("dotnet.xyz789.js", fields.DotnetJsFilename);
	}

	[TestMethod]
	[Description("Regression guard: verifies dotnet_js_filename is still captured even when it appears after the other known fields.")]
	public void When_DotnetJsFilenameAppearsLast_Then_ItIsStillCaptured()
	{
		var content = """
			config.uno_app_base = "/myapp";
			config.uno_remote_managedpath = "managed";
			config.uno_main = "[MyApp.Wasm]MyApp.Program:Main";
			config.assemblies_with_size = {"MyApp.dll":100};
			config.dotnet_js_filename = "dotnet.late.js";
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		Assert.AreEqual("/myapp", fields.PackagePath);
		Assert.AreEqual("managed", fields.ManagedPath);
		Assert.AreEqual("MyApp.Wasm", fields.MainAssembly);
		Assert.IsNotNull(fields.Assemblies);
		Assert.AreEqual("dotnet.late.js", fields.DotnetJsFilename);
	}

	[TestMethod]
	[Description("Regression guard: verifies empty config content leaves every parsed field unset.")]
	public void When_EmptyContent_Then_AllFieldsNull()
	{
		var fields = VersionCheckService.ParseUnoConfigFields("");

		Assert.IsNull(fields.ManagedPath);
		Assert.IsNull(fields.PackagePath);
		Assert.IsNull(fields.MainAssembly);
		Assert.IsTrue(fields.Assemblies.IsDefaultOrEmpty);
		Assert.IsNull(fields.DotnetJsFilename);
	}

	[TestMethod]
	[Description("Regression guard: verifies malformed non-assignment lines are ignored without breaking subsequent parsing.")]
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

	[TestMethod]
	[Description("Regression guard: verifies malformed JSON literals in uno-config.js do not crash the whole inspection.")]
	public void When_FieldValueIsMalformedJson_Then_ParseContinues()
	{
		var content = """
			config.dotnet_js_filename = {not-json};
			config.uno_app_base = "/app";
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		Assert.AreEqual("/app", fields.PackagePath);
		Assert.IsNull(fields.DotnetJsFilename);
	}
}
