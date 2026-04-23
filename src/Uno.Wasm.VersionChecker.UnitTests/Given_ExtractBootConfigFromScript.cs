using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;
using AwesomeAssertions;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_ExtractBootConfigFromScript
{
	[TestMethod]
	[Description("Verifies embedded boot-config markers are extracted from dotnet.js payloads.")]
	public void When_ValidScriptWithMarkers_Then_ExtractsConfig()
	{
		var script = """
		// some javascript code
		var config = /*json-start*/{"mainAssemblyName":"MyApp","resources":{"assembly":[{"virtualPath":"MyApp.wasm","name":"MyApp.abc.wasm","integrity":"sha256-xxx"}]}}/*json-end*/;
		// more javascript
		""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		config.Should().NotBeNull();
		config!.MainAssemblyName.Should().Be("MyApp");
		config!.Assemblies.Length.Should().Be(1);
		config!.Assemblies[0].Should().Be("MyApp.abc.wasm");
	}

	[TestMethod]
	[Description("Verifies unrelated scripts do not accidentally parse as boot configuration.")]
	public void When_ScriptWithoutMarkers_Then_ReturnsNull()
	{
		var script = """
		// some javascript code without boot config
		var x = 42;
		""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		config.Should().BeNull();
	}

	[TestMethod]
	[Description("Verifies minimal boot config payloads still return a valid parsed object.")]
	public void When_MinimalBootConfig_Then_ExtractsMainAssemblyName()
	{
		var script = """/*json-start*/{"mainAssemblyName":"MinimalApp"}/*json-end*/""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		config.Should().NotBeNull();
		config.MainAssemblyName.Should().Be("MinimalApp");
		config.Assemblies.Length.Should().Be(0);
		config.GlobalizationMode.Should().BeNull();
		config.DebugLevel.Should().BeNull();
		config.LinkerEnabled.Should().BeNull();
	}

	[TestMethod]
	[Description("Verifies all boot config metadata fields survive parsing.")]
	public void When_FullBootConfig_Then_ExtractsAllFields()
	{
		var script = """
		stuff/*json-start*/{"mainAssemblyName":"FullApp","globalizationMode":"hybrid","debugLevel":1,"linkerEnabled":true,"resources":{"coreAssembly":[{"virtualPath":"System.Private.CoreLib.wasm","name":"System.Private.CoreLib.abc.wasm","integrity":"sha256-aaa"}],"assembly":[{"virtualPath":"FullApp.wasm","name":"FullApp.def.wasm","integrity":"sha256-bbb"}]}}/*json-end*/more
		""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		config.Should().NotBeNull();
		config.MainAssemblyName.Should().Be("FullApp");
		config.GlobalizationMode.Should().Be("hybrid");
		config.DebugLevel.Should().Be(1);
		config.LinkerEnabled.Should().BeTrue();
		config.Assemblies.Length.Should().Be(2);
		config.Assemblies[0].Should().Be("System.Private.CoreLib.abc.wasm");
		config.Assemblies[1].Should().Be("FullApp.def.wasm");
	}

	[TestMethod]
	[Description("Verifies malformed JSON between markers is ignored safely.")]
	public void When_MalformedJson_Then_ReturnsNull()
	{
		var script = """/*json-start*/{ this is not valid json /*json-end*/""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		config.Should().BeNull();
	}

	[TestMethod]
	[Description("Verifies dotnet.js extraction does not invent an assemblies path before URL resolution.")]
	public void When_AssembliesPathNotSet_Then_IsNull()
	{
		var script = """/*json-start*/{"mainAssemblyName":"Test"}/*json-end*/""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		config.Should().NotBeNull();
		config.AssembliesPath.Should().BeNull();
	}
}
