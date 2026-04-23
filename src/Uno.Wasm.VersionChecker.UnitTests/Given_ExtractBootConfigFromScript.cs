using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_ExtractBootConfigFromScript
{
	[TestMethod]
	public void When_ValidScriptWithMarkers_Then_ExtractsConfig()
	{
		var script = """
		// some javascript code
		var config = /*json-start*/{"mainAssemblyName":"MyApp","resources":{"assembly":[{"virtualPath":"MyApp.wasm","name":"MyApp.abc.wasm","integrity":"sha256-xxx"}]}}/*json-end*/;
		// more javascript
		""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		Assert.IsNotNull(config);
		Assert.AreEqual("MyApp", config.MainAssemblyName);
		Assert.AreEqual(1, config.Assemblies.Length);
		Assert.AreEqual("MyApp.abc.wasm", config.Assemblies[0]);
	}

	[TestMethod]
	public void When_ScriptWithoutMarkers_Then_ReturnsNull()
	{
		var script = """
		// some javascript code without boot config
		var x = 42;
		""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		Assert.IsNull(config);
	}

	[TestMethod]
	public void When_MinimalBootConfig_Then_ExtractsMainAssemblyName()
	{
		var script = """/*json-start*/{"mainAssemblyName":"MinimalApp"}/*json-end*/""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		Assert.IsNotNull(config);
		Assert.AreEqual("MinimalApp", config.MainAssemblyName);
		Assert.AreEqual(0, config.Assemblies.Length);
		Assert.IsNull(config.GlobalizationMode);
		Assert.IsNull(config.DebugLevel);
		Assert.IsNull(config.LinkerEnabled);
	}

	[TestMethod]
	public void When_FullBootConfig_Then_ExtractsAllFields()
	{
		var script = """
		stuff/*json-start*/{"mainAssemblyName":"FullApp","globalizationMode":"hybrid","debugLevel":1,"linkerEnabled":true,"resources":{"coreAssembly":[{"virtualPath":"System.Private.CoreLib.wasm","name":"System.Private.CoreLib.abc.wasm","integrity":"sha256-aaa"}],"assembly":[{"virtualPath":"FullApp.wasm","name":"FullApp.def.wasm","integrity":"sha256-bbb"}]}}/*json-end*/more
		""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		Assert.IsNotNull(config);
		Assert.AreEqual("FullApp", config.MainAssemblyName);
		Assert.AreEqual("hybrid", config.GlobalizationMode);
		Assert.AreEqual(1, config.DebugLevel);
		Assert.AreEqual(true, config.LinkerEnabled);
		Assert.AreEqual(2, config.Assemblies.Length);
		Assert.AreEqual("System.Private.CoreLib.abc.wasm", config.Assemblies[0]);
		Assert.AreEqual("FullApp.def.wasm", config.Assemblies[1]);
	}

	[TestMethod]
	public void When_MalformedJson_Then_ReturnsNull()
	{
		var script = """/*json-start*/{ this is not valid json /*json-end*/""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		Assert.IsNull(config);
	}

	[TestMethod]
	public void When_AssembliesPathNotSet_Then_IsNull()
	{
		var script = """/*json-start*/{"mainAssemblyName":"Test"}/*json-end*/""";

		var config = VersionCheckService.ExtractBootConfigFromScript(script);

		Assert.IsNotNull(config);
		Assert.IsNull(config.AssembliesPath);
	}
}
