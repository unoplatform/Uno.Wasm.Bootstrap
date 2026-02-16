using System.Collections.Generic;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_ExtractAssembliesFromResources
{
	[TestMethod]
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

		var assemblies = new List<string>();
		UnoVersionExtractor.ExtractAssembliesFromResources(json.RootElement, "assembly", assemblies);

		Assert.AreEqual(2, assemblies.Count);
		Assert.AreEqual("System.Runtime.abc123.wasm", assemblies[0]);
		Assert.AreEqual("MyApp.def456.wasm", assemblies[1]);
	}

	[TestMethod]
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

		var assemblies = new List<string>();
		UnoVersionExtractor.ExtractAssembliesFromResources(json.RootElement, "assembly", assemblies);

		Assert.AreEqual(2, assemblies.Count);
		Assert.AreEqual("System.Runtime.dll", assemblies[0]);
		Assert.AreEqual("MyApp.dll", assemblies[1]);
	}

	[TestMethod]
	public void When_MissingProperty_Then_ReturnsEmpty()
	{
		var json = JsonDocument.Parse("{}");

		var assemblies = new List<string>();
		UnoVersionExtractor.ExtractAssembliesFromResources(json.RootElement, "assembly", assemblies);

		Assert.AreEqual(0, assemblies.Count);
	}

	[TestMethod]
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

		var assemblies = new List<string>();
		UnoVersionExtractor.ExtractAssembliesFromResources(json.RootElement, "assembly", assemblies);

		Assert.AreEqual(1, assemblies.Count);
		Assert.AreEqual("MyApp.def456.wasm", assemblies[0]);
	}

	[TestMethod]
	public void When_EmptyArray_Then_ReturnsEmpty()
	{
		var json = JsonDocument.Parse("""{"assembly": []}""");

		var assemblies = new List<string>();
		UnoVersionExtractor.ExtractAssembliesFromResources(json.RootElement, "assembly", assemblies);

		Assert.AreEqual(0, assemblies.Count);
	}

	[TestMethod]
	public void When_EmptyObject_Then_ReturnsEmpty()
	{
		var json = JsonDocument.Parse("""{"assembly": {}}""");

		var assemblies = new List<string>();
		UnoVersionExtractor.ExtractAssembliesFromResources(json.RootElement, "assembly", assemblies);

		Assert.AreEqual(0, assemblies.Count);
	}

	[TestMethod]
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

		var assemblies = new List<string>();
		UnoVersionExtractor.ExtractAssembliesFromResources(json.RootElement, "coreAssembly", assemblies);
		UnoVersionExtractor.ExtractAssembliesFromResources(json.RootElement, "assembly", assemblies);

		Assert.AreEqual(2, assemblies.Count);
		Assert.AreEqual("Core.abc.wasm", assemblies[0]);
		Assert.AreEqual("App.def.wasm", assemblies[1]);
	}
}
