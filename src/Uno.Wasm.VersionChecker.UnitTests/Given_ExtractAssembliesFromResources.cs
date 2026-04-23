using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_ExtractAssembliesFromResources
{
	[TestMethod]
	[Description("Regression guard: verifies array-based resource entries are projected to their file names.")]
	public void When_ArrayFormat_Then_ExtractsNames()
	{
		var json = JsonDocument.Parse("""
		{
			"assembly": [
				{"virtualPath": "System.Runtime.wasm", "name": "System.Runtime.abc123.wasm", "integrity": "sha256-xxx"},
				{"virtualPath": "MyApp.wasm", "name": "MyApp.def456.wasm", "integrity": "sha256-yyy"}
			]
		}
		""");

		var assemblies = VersionCheckService.ExtractAssembliesFromResources(json.RootElement, "assembly").ToArray();

		Assert.AreEqual(2, assemblies.Length);
		Assert.AreEqual("System.Runtime.abc123.wasm", assemblies[0]);
		Assert.AreEqual("MyApp.def456.wasm", assemblies[1]);
	}

	[TestMethod]
	[Description("Regression guard: verifies object-based resources are projected to their property names.")]
	public void When_ObjectFormat_Then_ExtractsKeys()
	{
		var json = JsonDocument.Parse("""
		{
			"assembly": {
				"System.Runtime.dll": "sha256-xxx",
				"MyApp.dll": "sha256-yyy"
			}
		}
		""");

		var assemblies = VersionCheckService.ExtractAssembliesFromResources(json.RootElement, "assembly").ToArray();

		Assert.AreEqual(2, assemblies.Length);
		Assert.AreEqual("System.Runtime.dll", assemblies[0]);
		Assert.AreEqual("MyApp.dll", assemblies[1]);
	}

	[TestMethod]
	[Description("Regression guard: verifies missing resource properties do not produce phantom entries.")]
	public void When_MissingProperty_Then_ReturnsEmpty()
	{
		var json = JsonDocument.Parse("{}");

		var assemblies = VersionCheckService.ExtractAssembliesFromResources(json.RootElement, "assembly").ToArray();

		Assert.AreEqual(0, assemblies.Length);
	}

	[TestMethod]
	[Description("Regression guard: verifies unnamed array entries are skipped instead of producing empty assembly names.")]
	public void When_ArrayEntryMissingName_Then_SkipsIt()
	{
		var json = JsonDocument.Parse("""
		{
			"assembly": [
				{"virtualPath": "System.Runtime.wasm"},
				{"virtualPath": "MyApp.wasm", "name": "MyApp.def456.wasm", "integrity": "sha256-yyy"}
			]
		}
		""");

		var assemblies = VersionCheckService.ExtractAssembliesFromResources(json.RootElement, "assembly").ToArray();

		Assert.AreEqual(1, assemblies.Length);
		Assert.AreEqual("MyApp.def456.wasm", assemblies[0]);
	}

	[TestMethod]
	[Description("Regression guard: verifies empty arrays produce no assembly entries.")]
	public void When_EmptyArray_Then_ReturnsEmpty()
	{
		var json = JsonDocument.Parse("""{"assembly": []}""");

		var assemblies = VersionCheckService.ExtractAssembliesFromResources(json.RootElement, "assembly").ToArray();

		Assert.AreEqual(0, assemblies.Length);
	}

	[TestMethod]
	[Description("Regression guard: verifies empty objects produce no assembly entries.")]
	public void When_EmptyObject_Then_ReturnsEmpty()
	{
		var json = JsonDocument.Parse("""{"assembly": {}}""");

		var assemblies = VersionCheckService.ExtractAssembliesFromResources(json.RootElement, "assembly").ToArray();

		Assert.AreEqual(0, assemblies.Length);
	}

	[TestMethod]
	[Description("Regression guard: verifies callers can concatenate core and app assemblies from separate resource sections.")]
	public void When_BothCoreAssemblyAndAssembly_Then_ExtractsBoth()
	{
		var json = JsonDocument.Parse("""
		{
			"coreAssembly": [
				{"virtualPath": "Core.wasm", "name": "Core.abc.wasm", "integrity": "sha256-aaa"}
			],
			"assembly": [
				{"virtualPath": "App.wasm", "name": "App.def.wasm", "integrity": "sha256-bbb"}
			]
		}
		""");

		var assemblies = VersionCheckService.ExtractAssembliesFromResources(json.RootElement, "coreAssembly")
			.Concat(VersionCheckService.ExtractAssembliesFromResources(json.RootElement, "assembly"))
			.ToArray();

		Assert.AreEqual(2, assemblies.Length);
		Assert.AreEqual("Core.abc.wasm", assemblies[0]);
		Assert.AreEqual("App.def.wasm", assemblies[1]);
	}
}
