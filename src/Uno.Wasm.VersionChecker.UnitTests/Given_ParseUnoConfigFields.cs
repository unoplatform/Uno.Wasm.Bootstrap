using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_ParseUnoConfigFields
{
	[TestMethod]
	[Description("Verifies dotnet.js filenames are extracted from uno-config.js.")]
	public void When_ConfigWithDotnetJsFilename_Then_ExtractsIt()
	{
		var content = """
			config.dotnet_js_filename = "dotnet.abc123.js";
			config.uno_app_base = "/myapp";
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		fields.DotnetJsFilename.Should().Be("dotnet.abc123.js");
		fields.PackagePath.Should().Be("/myapp");
	}

	[TestMethod]
	[Description("Verifies optional dotnet.js filenames stay null when absent.")]
	public void When_ConfigWithoutDotnetJsFilename_Then_FieldIsNull()
	{
		var content = """
			config.uno_app_base = "/myapp";
			config.uno_remote_managedpath = "managed";
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		fields.DotnetJsFilename.Should().BeNull();
		fields.PackagePath.Should().Be("/myapp");
		fields.ManagedPath.Should().Be("managed");
	}

	[TestMethod]
	[Description("Verifies assemblies_with_size keys are preserved as assembly names.")]
	public void When_ConfigWithAssembliesWithSize_Then_ExtractsAssemblyNames()
	{
		var content = """
			config.uno_app_base = "/app";
			config.uno_remote_managedpath = "managed";
			config.uno_main = "[MyApp.Wasm]MyApp.Program:Main";
			config.assemblies_with_size = {"MyApp.dll":12345,"System.Runtime.dll":67890};
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		fields.Assemblies.Should().NotBeNull();
		fields.Assemblies.Length.Should().Be(2);
		fields.Assemblies.Should().Contain("MyApp.dll");
		fields.Assemblies.Should().Contain("System.Runtime.dll");
	}

	[TestMethod]
	[Description("Verifies all supported uno-config fields can be parsed together.")]
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

		fields.PackagePath.Should().Be("/myapp");
		fields.ManagedPath.Should().Be("managed");
		fields.MainAssembly.Should().Be("MyApp.Wasm");
		fields.Assemblies.Should().NotBeNull();
		fields.Assemblies.Length.Should().Be(1);
		fields.Assemblies[0].Should().Be("MyApp.dll");
		fields.DotnetJsFilename.Should().Be("dotnet.xyz789.js");
	}

	[TestMethod]
	[Description("Verifies dotnet_js_filename is still captured even when it appears after the other known fields.")]
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

		fields.PackagePath.Should().Be("/myapp");
		fields.ManagedPath.Should().Be("managed");
		fields.MainAssembly.Should().Be("MyApp.Wasm");
		fields.Assemblies.Should().NotBeNull();
		fields.DotnetJsFilename.Should().Be("dotnet.late.js");
	}

	[TestMethod]
	[Description("Verifies empty config content leaves every parsed field unset.")]
	public void When_EmptyContent_Then_AllFieldsNull()
	{
		var fields = VersionCheckService.ParseUnoConfigFields("");

		fields.ManagedPath.Should().BeNull();
		fields.PackagePath.Should().BeNull();
		fields.MainAssembly.Should().BeNull();
		fields.Assemblies.IsDefaultOrEmpty.Should().BeTrue();
		fields.DotnetJsFilename.Should().BeNull();
	}

	[TestMethod]
	[Description("Verifies malformed non-assignment lines are ignored without breaking subsequent parsing.")]
	public void When_LinesWithoutEquals_Then_IgnoredGracefully()
	{
		var content = """
			// This is a comment
			config.uno_app_base = "/app";
			some random text
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		fields.PackagePath.Should().Be("/app");
	}

	[TestMethod]
	[Description("Verifies malformed JSON literals in uno-config.js do not crash the whole inspection.")]
	public void When_FieldValueIsMalformedJson_Then_ParseContinues()
	{
		var content = """
			config.dotnet_js_filename = {not-json};
			config.uno_app_base = "/app";
			""";

		var fields = VersionCheckService.ParseUnoConfigFields(content);

		fields.PackagePath.Should().Be("/app");
		fields.DotnetJsFilename.Should().BeNull();
	}
}
