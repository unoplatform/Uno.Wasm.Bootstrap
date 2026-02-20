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

		var config = UnoVersionExtractor.ExtractBootConfigFromScript(script);

		Assert.IsNotNull(config);
		Assert.AreEqual("MyApp", config.mainAssemblyName);
		Assert.AreEqual(1, config.assemblies.Length);
		Assert.AreEqual("MyApp.abc.wasm", config.assemblies[0]);
	}

	[TestMethod]
	public void When_ScriptWithoutMarkers_Then_ReturnsNull()
	{
		var script = """
		// some javascript code without boot config
		var x = 42;
		""";

		var config = UnoVersionExtractor.ExtractBootConfigFromScript(script);

		Assert.IsNull(config);
	}

	[TestMethod]
	public void When_MinimalBootConfig_Then_ExtractsMainAssemblyName()
	{
		var script = """/*json-start*/{"mainAssemblyName":"MinimalApp"}/*json-end*/""";

		var config = UnoVersionExtractor.ExtractBootConfigFromScript(script);

		Assert.IsNotNull(config);
		Assert.AreEqual("MinimalApp", config.mainAssemblyName);
		Assert.AreEqual(0, config.assemblies.Length);
		Assert.IsNull(config.globalizationMode);
		Assert.IsNull(config.debugLevel);
		Assert.IsNull(config.linkerEnabled);
	}

	[TestMethod]
	public void When_FullBootConfig_Then_ExtractsAllFields()
	{
		var script = """
		stuff/*json-start*/{"mainAssemblyName":"FullApp","globalizationMode":"hybrid","debugLevel":1,"linkerEnabled":true,"resources":{"coreAssembly":[{"virtualPath":"System.Private.CoreLib.wasm","name":"System.Private.CoreLib.abc.wasm","integrity":"sha256-aaa"}],"assembly":[{"virtualPath":"FullApp.wasm","name":"FullApp.def.wasm","integrity":"sha256-bbb"}]}}/*json-end*/more
		""";

		var config = UnoVersionExtractor.ExtractBootConfigFromScript(script);

		Assert.IsNotNull(config);
		Assert.AreEqual("FullApp", config.mainAssemblyName);
		Assert.AreEqual("hybrid", config.globalizationMode);
		Assert.AreEqual(1, config.debugLevel);
		Assert.AreEqual(true, config.linkerEnabled);
		Assert.AreEqual(2, config.assemblies.Length);
		Assert.AreEqual("System.Private.CoreLib.abc.wasm", config.assemblies[0]);
		Assert.AreEqual("FullApp.def.wasm", config.assemblies[1]);
	}

	[TestMethod]
	public void When_MalformedJson_Then_ReturnsNull()
	{
		var script = """/*json-start*/{ this is not valid json /*json-end*/""";

		var config = UnoVersionExtractor.ExtractBootConfigFromScript(script);

		Assert.IsNull(config);
	}

	[TestMethod]
	public void When_AssembliesPathNotSet_Then_IsNull()
	{
		var script = """/*json-start*/{"mainAssemblyName":"Test"}/*json-end*/""";

		var config = UnoVersionExtractor.ExtractBootConfigFromScript(script);

		Assert.IsNotNull(config);
		Assert.IsNull(config.assembliesPath);
	}
}
