using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Uno.Wasm.Bootstrap.UnitTests
{
	/// <summary>
	/// Tests that verify the bootstrap TypeScript source properly initializes
	/// the Emscripten Module object with required array properties.
	/// </summary>
	/// <remarks>
	/// These tests ensure compatibility with Emscripten's --preload-file flag,
	/// which generates code that expects Module.preRun to be an array.
	/// See: https://github.com/unoplatform/Uno.Wasm.Bootstrap/issues/994
	/// </remarks>
	[TestClass]
	public class Given_ModuleInitialization
	{
		[TestMethod]
		public void When_BootstrapTs_Contains_ModulePreRunInitialization()
		{
			var bootstrapTs = GetBootstrapperTs();

			Assert.IsTrue(
				bootstrapTs.Contains("g.Module.preRun = []"),
				"Bootstrap TypeScript should initialize Module.preRun as an array");
		}

		[TestMethod]
		public void When_BootstrapTs_Contains_ModulePostRunInitialization()
		{
			var bootstrapTs = GetBootstrapperTs();

			Assert.IsTrue(
				bootstrapTs.Contains("g.Module.postRun = []"),
				"Bootstrap TypeScript should initialize Module.postRun as an array");
		}

		[TestMethod]
		public void When_BootstrapTs_Contains_ModulePreInitInitialization()
		{
			var bootstrapTs = GetBootstrapperTs();

			Assert.IsTrue(
				bootstrapTs.Contains("g.Module.preInit = []"),
				"Bootstrap TypeScript should initialize Module.preInit as an array");
		}

		[TestMethod]
		public void When_BootstrapTs_Contains_ArrayIsArrayCheck_ForPreRun()
		{
			var bootstrapTs = GetBootstrapperTs();

			Assert.IsTrue(
				bootstrapTs.Contains("Array.isArray(g.Module.preRun)"),
				"Bootstrap TypeScript should check if Module.preRun is already an array before initializing");
		}

		[TestMethod]
		public void When_BootstrapTs_Contains_ModuleObjectInitialization()
		{
			var bootstrapTs = GetBootstrapperTs();

			Assert.IsTrue(
				bootstrapTs.Contains("g.Module = {}"),
				"Bootstrap TypeScript should initialize Module object if it doesn't exist");
		}

		[TestMethod]
		public void When_BootstrapTs_InitializesModuleBeforeBootstrapperClass()
		{
			var bootstrapTs = GetBootstrapperTs();

			var moduleInitIndex = bootstrapTs.IndexOf("initializeModuleArrays");
			var bootstrapperIndex = bootstrapTs.IndexOf("class Bootstrapper");

			Assert.IsTrue(
				moduleInitIndex >= 0,
				"Bootstrap TypeScript should contain initializeModuleArrays function");

			Assert.IsTrue(
				bootstrapperIndex >= 0,
				"Bootstrap TypeScript should contain Bootstrapper class");

			Assert.IsTrue(
				moduleInitIndex < bootstrapperIndex,
				"Module initialization should occur before Bootstrapper class definition");
		}

		private static string GetBootstrapperTs()
		{
			// Get the path to the Bootstrapper.ts source file
			// The test project is at src/Uno.Wasm.Bootstrap.UnitTests
			// The source file is at src/Uno.Wasm.Bootstrap/ts/Uno/WebAssembly/Bootstrapper.ts
			var testAssemblyLocation = typeof(Given_ModuleInitialization).Assembly.Location;
			var testDirectory = Path.GetDirectoryName(testAssemblyLocation);
			
			// Navigate up to find the src directory
			var currentDir = new DirectoryInfo(testDirectory);
			while (currentDir != null && currentDir.Name != "src")
			{
				currentDir = currentDir.Parent;
			}
			
			if (currentDir == null)
			{
				// Fallback: try relative path from working directory
				var relativePath = Path.Combine("src", "Uno.Wasm.Bootstrap", "ts", "Uno", "WebAssembly", "Bootstrapper.ts");
				if (File.Exists(relativePath))
				{
					return File.ReadAllText(relativePath);
				}
				
				Assert.Fail("Could not locate Bootstrapper.ts source file");
			}

			var bootstrapperPath = Path.Combine(
				currentDir.FullName,
				"Uno.Wasm.Bootstrap",
				"ts",
				"Uno",
				"WebAssembly",
				"Bootstrapper.ts");

			Assert.IsTrue(File.Exists(bootstrapperPath), $"Could not find Bootstrapper.ts at {bootstrapperPath}");

			return File.ReadAllText(bootstrapperPath);
		}
	}
}
