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

public class WasmTuner
{
	public static int Main (String[] args) {
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

	int Run (String[] args) {
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
			return GenEmptyAssemblies (args);
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

	int GenPinvokeTable (String[] args) {
		var modules = new Dictionary<string, string> ();
		foreach (var module in args [1].Split (','))
			modules [module] = module;

		args = args.Skip (2).ToArray ();

#if NETFRAMEWORK
		var assemblies = new List<AssemblyDefinition> ();
		foreach (var fname in args)
			assemblies.Add (AssemblyDefinition.ReadAssembly (fname));

		var generator = new PInvokeTableGenerator ();
		generator.Run (assemblies, modules);
#else
		var generator = new PInvokeTableGenerator();

		generator.OutputPath = Path.GetTempFileName();
		generator.GenPInvokeTable(modules.Keys.ToArray(), args.ToArray());

		Console.WriteLine(File.ReadAllText(generator.OutputPath));
#endif

		return 0;
	}

	void Error (string msg) {
		Console.Error.WriteLine (msg);
		Environment.Exit (1);
	}


	//
	// Given the runtime generated icall table, and a set of assemblies, generate
	// a smaller linked icall table mapping tokens to C function names
	//
	int GenIcallTable(String[] args) {
		var icall_table_filename = args [1];
		var fileNames = args.Skip (2).ToArray ();

#if NETFRAMEWORK
		throw new NotSupportedException($"icall table generation is not supported for netstandard2.0");
#else
		var generator = new IcallTableGenerator();
		generator.OutputPath = Path.GetTempFileName();
		generator.GenIcallTable(icall_table_filename, fileNames);
		Console.WriteLine(File.ReadAllText(generator.OutputPath));
#endif

		return 0;
	}

	// Generate empty assemblies for the filenames in ARGS if they don't exist
	int GenEmptyAssemblies (String[] args) {
		foreach (var fname in args) {
			if (File.Exists (fname))
				continue;
			var basename = Path.GetFileName (fname).Replace (".exe", "").Replace (".dll", "");
			var assembly = AssemblyDefinition.CreateAssembly (new AssemblyNameDefinition (basename, new Version (0, 0, 0, 0)), basename, ModuleKind.Dll);
			assembly.Write (fname);
		}
		return 0;
	}
}
