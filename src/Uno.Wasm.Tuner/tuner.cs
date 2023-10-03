#nullable enable
#pragma warning disable IDE0011
#pragma warning disable IDE0270
#pragma warning disable IDE0021
#pragma warning disable IDE0022

//
// tuner.cs: WebAssembly build time helpers
//
//
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Json;
using System.Collections.Generic;
using Mono.Cecil;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

public class WasmTuner
{
	// Avoid sharing this cache with all the invocations of this task throughout the build
	private readonly Dictionary<string, string> _symbolNameFixups = new();
	private static readonly char[] s_charsToReplace = new[] { '.', '-', '+' };

	public static int Main (string[] args) {
		return new WasmTuner ().Run (args);
	}

	void Usage () {
		Console.WriteLine ("Usage: tuner.exe <arguments>");
		Console.WriteLine ("Arguments:");
		Console.WriteLine ("--gen-icall-table icall-table.json <assemblies>.");
		Console.WriteLine ("--gen-pinvoke-table <list of native library names separated by commas> <assemblies>.");
		Console.WriteLine ("--gen-interp-to-native <output file name> <assemblies>.");
		Console.WriteLine ("--gen-empty-assemblies <filenames>.");
	}

	int Run (string[] args)
	{
		if (args.Length < 1) {
			Usage ();
			return 1;
		}
		string cmd = args [0];
		if (cmd == "--gen-icall-table") {
			if (args.Length < 3) {
				Usage ();
				return 1;
			}
			return GenIcallTable (args);
		} else if (cmd == "--gen-pinvoke-table") {
			return GenPinvokeTable (args);
		} else if (cmd == "--gen-empty-assemblies") {
			return GenEmptyAssemblies2 (args);
		} else {
			Usage ();
			return 1;
		}
	}

	public static string MapType (TypeReference t) {
		if (t.Name == "Void")
			return "void";
		else if (t.Name == "Double")
			return "double";
		else if (t.Name == "Single")
			return "float";
		else if (t.Name == "Int64")
			return "int64_t";
		else if (t.Name == "UInt64")
			return "uint64_t";
		else
			return "int";
	}

	int GenPinvokeTable (string[] args)
	{
		if (args[1].StartsWith("@"))
		{
			var rawContent = File.ReadAllText(args[1].Substring(1));

			var content = rawContent.Split(" ");

			args = new[] { args[0] }
				.Concat(content)
				.ToArray();
		}

		var PInvokeOutputPath = args[1];
		var RuntimeIcallTableFile = args[3];
		var InterpToNativeOutputPath = Path.Combine(Path.GetDirectoryName(PInvokeOutputPath)!, "wasm_m2n_invoke.g.h");

		var modules = new List<string> ();
		foreach (var module in args [2].Split (','))
		{
			modules.Add(module);
		}
		var PInvokeModules = modules.ToArray();

		var managedAssemblies = args.Skip(4).ToArray();

		var Log = new TaskLoggingHelper();

		var pinvoke = new PInvokeTableGenerator(FixupSymbolName, Log);
		var icall = new IcallTableGenerator(RuntimeIcallTableFile, FixupSymbolName, Log);

		var resolver = new PathAssemblyResolver(managedAssemblies);
		using var mlc = new MetadataLoadContext(resolver, "System.Private.CoreLib");

		managedAssemblies.AsParallel().ForAll(
		asmPath =>
		{
			Log.LogMessage(MessageImportance.Low, $"[{Thread.CurrentThread.ManagedThreadId}] Loading {asmPath} to scan for pinvokes, and icalls");
			Assembly asm = mlc.LoadFromAssemblyPath(asmPath);
			pinvoke.ScanAssembly(asm);
			icall.ScanAssembly(asm);
		});

		IEnumerable<string> cookies = Enumerable.Concat(
			pinvoke.Generate(PInvokeModules, PInvokeOutputPath),
			icall.Generate(Path.GetTempFileName()));

		var m2n = new InterpToNativeGenerator(Log);
		m2n.Generate(cookies, InterpToNativeOutputPath);

		return 0;
	}

	void Error (string msg) {
		Console.Error.WriteLine (msg);
		Environment.Exit (1);
	}

	public string FixupSymbolName(string name)
	{
		if (_symbolNameFixups.TryGetValue(name, out string? fixedName))
			return fixedName;
		UTF8Encoding utf8 = new();
		byte[] bytes = utf8.GetBytes(name);
		StringBuilder sb = new();
		foreach (byte b in bytes)
		{
			if ((b >= (byte)'0' && b <= (byte)'9') ||
				(b >= (byte)'a' && b <= (byte)'z') ||
				(b >= (byte)'A' && b <= (byte)'Z') ||
				(b == (byte)'_'))
			{
				sb.Append((char)b);
			}
			else if (s_charsToReplace.Contains((char)b))
			{
				sb.Append('_');
			}
			else
			{
				sb.Append($"_{b:X}_");
			}
		}
		fixedName = sb.ToString();
		_symbolNameFixups[name] = fixedName;
		return fixedName;
	}

	//
	// Given the runtime generated icall table, and a set of assemblies, generate
	// a smaller linked icall table mapping tokens to C function names
	//
	int GenIcallTable(string[] args) {
		// Unused, work is done in GenPinvokeTable.

		return 0;
	}

	// Generate empty assemblies for the filenames in ARGS if they don't exist
	int GenEmptyAssemblies2 (IEnumerable<string> args)
	{
		args = args.SelectMany(arg =>
		{
			// Expand a response file

			if (arg.StartsWith("@"))
			{
				var rawContent = File.ReadAllText(arg.Substring(1));
				var content = rawContent.Split(" ");

#if DEBUG
				Console.WriteLine($"Tuner Response content: {rawContent}");
#endif

				return content;
			}
			return new[] { arg };
		});

		foreach (var fname in args.Skip(1))
		{
			if (File.Exists (fname) || !Path.GetExtension(fname).Equals(".dll", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			Console.WriteLine($"Generating empty linked assembly for {fname}");

			var basename = Path.GetFileName (fname).Replace (".exe", "").Replace (".dll", "");
			var assembly = AssemblyDefinition.CreateAssembly (new AssemblyNameDefinition (basename, new Version (0, 0, 0, 0)), basename, ModuleKind.Dll);
			assembly.Write (fname);

			File.WriteAllText(Path.ChangeExtension(fname, ".aot-only"), "");
			File.WriteAllText(Path.ChangeExtension(fname, ".pdb"), "");
		}
		return 0;
	}
}
