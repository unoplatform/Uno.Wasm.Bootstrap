using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Options;
using Mono.Cecil.Cil;
using System.Diagnostics;

//
// Google V8 style options:
// - bool: --foo/--no-foo
//

enum FlagType {
	BoolFlag,
}

// 'Option' is already used by Mono.Options
class Flag {
	public Flag (string name, string desc, FlagType type) {
		Name = name;
		FlagType = type;
		Description = desc;
	}

	public string Name {
		get; set;
	}

	public FlagType FlagType {
		get; set;
	}

	public string Description {
		get; set;
	}
}

class BoolFlag : Flag {
	public BoolFlag (string name, string description, bool def_value, Action<bool> action) : base (name, description, FlagType.BoolFlag) {
		Setter = action;
		DefaultValue = def_value;
	}

	public Action<bool> Setter {
		get; set;
	}

	public bool DefaultValue {
		get; set;
	}
}

class Driver {
	static bool enable_debug, enable_linker, invariant_globalization;
	static string app_prefix, framework_prefix, bcl_tools_prefix, bcl_facades_prefix, out_prefix;
	static List<string> bcl_prefixes;
	static HashSet<string> asm_map = new HashSet<string> ();
	static List<string>  file_list = new List<string> ();
	static HashSet<string> assemblies_with_dbg_info = new HashSet<string> ();
	static List<string> root_search_paths = new List<string>();
	static CapturingAssemblyResolver resolver;

	const string BINDINGS_ASM_NAME_MONO = "WebAssembly.Bindings";
	const string BINDINGS_RUNTIME_CLASS_NAME = "WebAssembly.Runtime";
	const string HTTP_ASM_NAME = "System.Net.Http.WebAssemblyHttpHandler";
	const string WEBSOCKETS_ASM_NAME = "WebAssembly.Net.WebSockets";
	const string BINDINGS_MODULE = "corebindings.o";
	const string BINDINGS_MODULE_SUPPORT = "$tool_prefix/src/binding_support.js";
	private static readonly string[] jiterpreterOptions = new[] { "jiterpreter-traces-enabled", "jiterpreter-interp-entry-enabled", "jiterpreter-jit-call-enabled" };

	class AssemblyData {
		// Assembly name
		public string name;
		// Base filename
		public string filename;
		// Path outside build tree
		public string src_path;
		// Path of .bc file
		public string bc_path;
		// Path of the wasm object file
		public string o_path;
		// Path in appdir
		public string app_path;
		// Path of the AOT depfile
		public string aot_depfile_path;
		// Linker input path
		public string linkin_path;
		// Linker pdb input path
		public string linkin_pdb_path;
		// Linker pdb output path
		public string linkout_pdb_path;
		// Linker output path
		public string linkout_path;
		// AOT input path
		public string aotin_path;
		// Final output path after IL strip
		public string final_path;
		// Whenever to AOT this assembly
		public bool aot;

		// If not null, this is a satellite assembly
		public string culture;
	}

	static List<AssemblyData> assemblies = new List<AssemblyData> ();

	enum AssemblyKind {
		User,
		Framework,
		Bcl,
		None,
	}

	void AddFlag (OptionSet options, Flag flag) {
		if (flag is BoolFlag) {
			options.Add (flag.Name, s => (flag as BoolFlag).Setter (true));
			options.Add ("no-" + flag.Name, s => (flag as BoolFlag).Setter (false));
		}
		option_list.Add (flag);
	}

	static List<Flag> option_list = new List<Flag> ();

	static void Usage () {
		Console.WriteLine ("Usage: packager.exe <options> <assemblies>");
		Console.WriteLine ("Valid options:");
		Console.WriteLine ("\t--help          Show this help message");
		Console.WriteLine ("\t--debugrt       Use the debug runtime (default release) - this has nothing to do with C# debugging");
		Console.WriteLine ("\t--aot           Enable AOT mode");
		Console.WriteLine ("\t--aot-interp    Enable AOT+INTERP mode");
		Console.WriteLine ("\t--prefix=x      Set the input assembly prefix to 'x' (default to the current directory)");
		Console.WriteLine ("\t--out=x         Set the output directory to 'x' (default to the current directory)");
		Console.WriteLine ("\t--mono-sdkdir=x Set the mono sdk directory to 'x'");
		Console.WriteLine ("\t--deploy=x      Set the deploy prefix to 'x' (default to 'managed')");
		Console.WriteLine ("\t--vfs=x         Set the VFS prefix to 'x' (default to 'managed')");
		Console.WriteLine ("\t--target-framework=x Set app target framework");
		Console.WriteLine ("\t--template=x    Set the template name to  'x' (default to 'runtime.js')");
		Console.WriteLine ("\t--asset=x       Add specified asset 'x' to list of assets to be copied");
		Console.WriteLine ("\t--search-path=x Add specified path 'x' to list of paths used to resolve assemblies");
		Console.WriteLine ("\t--copy=always|ifnewer        Set the type of copy to perform.");
		Console.WriteLine ("\t\t              'always' overwrites the file if it exists.");
		Console.WriteLine ("\t\t              'ifnewer' copies or overwrites the file if modified or size is different.");
		Console.WriteLine ("\t--profile=x     Enable the 'x' mono profiler.");
		Console.WriteLine ("\t--runtime-config=x  sets the mono runtime to use (defaults to release).");
		Console.WriteLine ("\t--pthread-pool-size=x  sets the number of available pthreads the runtime can use (defaults to 4).");
		Console.WriteLine ("\t--aot-assemblies=x List of assemblies to AOT in AOT+INTERP mode.");
		Console.WriteLine ("\t--skip-aot-assemblies=x List of assemblies to skip AOT in AOT+INTERP mode.");
		Console.WriteLine ("\t--aot-compiler-opts=x Adjust aot compiler options.");
		Console.WriteLine ("\t--aot-profile=x Use 'x' as the AOT profile.");
		Console.WriteLine ("\t--link-mode=sdkonly|all        Set the link type used for AOT. (EXPERIMENTAL)");
		Console.WriteLine ("\t--pinvoke-libs=x DllImport libraries used.");
		Console.WriteLine ("\t\t              'sdkonly' only link the Core libraries.");
		Console.WriteLine ("\t\t              'all' link Core and User assemblies. (default)");
		Console.WriteLine ("\t--pinvoke-libs=x DllImport libraries used.");
		Console.WriteLine ("\t--native-lib=x  Link the native library 'x' into the final executable.");
		Console.WriteLine ("\t--native-compile=x  Provide the file to emcc.");
		Console.WriteLine ("\t--preload-file=x Preloads the file or directory 'x' into the virtual filesystem.");
		Console.WriteLine ("\t--embed-file=x  Embeds the file or directory 'x' into the virtual filesystem.");
		Console.WriteLine ("\t--extra-emccflags=\"x\"  Additional emscripten arguments (e.g. -s USE_LIBPNG=1).");
		Console.WriteLine ("\t--extra-linkerflags=\"x\"  Additional linker arguments.");

		Console.WriteLine ("foo.dll         Include foo.dll as one of the root assemblies");
		Console.WriteLine ();

		Console.WriteLine ("Additional options (--option/--no-option):");
		foreach (var flag in option_list) {
			if (flag is BoolFlag) {
				Console.WriteLine ("  --" + flag.Name + " (" + flag.Description + ")");
				Console.WriteLine ("        type: bool  default: " + ((flag as BoolFlag).DefaultValue ? "true" : "false"));
			}
		}
	}

	static void Debug (string s) {
		Console.WriteLine (s);
	}

	static string FindFrameworkAssembly (string asm) {
		return asm;
	}

	static bool Try (string prefix, string name, out string out_res) {
		out_res = null;

		string res = (Path.Combine (prefix, name));
		if (File.Exists (res)) {
			out_res = Path.GetFullPath (res);
			return true;
		}
		return false;
	}

	static string ResolveWithExtension (string prefix, string name) {
		string res = null;

		if (Try (prefix, name, out res))
			return res;
		if (Try (prefix, name + ".dll", out res))
			return res;
		if (Try (prefix, name + ".exe", out res))
			return res;
		return null;
	}

	static string ResolveUser (string asm_name) {
		return ResolveWithExtension (app_prefix, asm_name);
	}

	static string ResolveFramework (string asm_name) {
		return ResolveWithExtension (framework_prefix, asm_name);
	}

	static string ResolveBcl (string asm_name) {
		foreach (var prefix in bcl_prefixes) {
			string res = ResolveWithExtension (prefix, asm_name);
			if (res != null)
				return res;
		}
		return null;
	}

	static string ResolveBclFacade (string asm_name) {
		return ResolveWithExtension (bcl_facades_prefix, asm_name);
	}

	static string Resolve (string asm_name, out AssemblyKind kind) {
		kind = AssemblyKind.User;
		var asm = ResolveUser (asm_name);
		if (asm != null)
			return asm;

		kind = AssemblyKind.Framework;
		asm = ResolveFramework (asm_name);
		if (asm != null)
			return asm;

		kind = AssemblyKind.Bcl;
		asm = ResolveBcl (asm_name);
		if (asm == null)
			asm = ResolveBclFacade (asm_name);
		if (asm != null)
			return asm;

		kind = AssemblyKind.None;
		throw new Exception ($"Could not resolve {asm_name}");
	}

	static bool is_sdk_assembly (string filename) {
		foreach (var prefix in bcl_prefixes)
			if (filename.StartsWith (prefix))
				return true;
		return false;
	}

	static void Import (string ra, AssemblyKind kind) {
		if(ra == null)
		{
			return;
		}

		var assemblyFullPath = Path.GetFullPath(ra);
		var assemblyDirectory = Path.GetDirectoryName(assemblyFullPath);

		if (!asm_map.Add (assemblyFullPath))
			return;
		Console.WriteLine($"Resolving {ra}");
		ReaderParameters rp = new ReaderParameters();
		bool add_pdb = enable_debug && File.Exists (Path.ChangeExtension (ra, "pdb"));
		if (add_pdb) {
			rp.ReadSymbols = true;
			// Facades do not have symbols
			rp.ThrowIfSymbolsAreNotMatching = false;
			rp.SymbolReaderProvider = new DefaultSymbolReaderProvider(false);
		}

		if (resolver == null)
		{
			resolver = new CapturingAssemblyResolver();
			root_search_paths.ForEach(resolver.AddSearchDirectory);
			foreach (var prefix in bcl_prefixes)
				resolver.AddSearchDirectory(prefix);
			resolver.AddSearchDirectory(bcl_facades_prefix);
			resolver.AddSearchDirectory(framework_prefix);
		}

		rp.AssemblyResolver = resolver;

		rp.InMemory = true;
		var image = ModuleDefinition.ReadModule (ra, rp);
		file_list.Add (ra);
		//Debug ($"Processing {ra} debug {add_pdb}");

		var data = new AssemblyData () { name = image.Assembly.Name.Name, src_path = ra };
		assemblies.Add (data);

		if (add_pdb && (kind == AssemblyKind.User || kind == AssemblyKind.Framework)) {
			var pdb_path = Path.ChangeExtension (Path.GetFullPath (ra), "pdb");
			file_list.Add (pdb_path);
			assemblies_with_dbg_info.Add (pdb_path);
		}

		var parent_kind = kind;

		foreach (var ar in image.AssemblyReferences) {
			// Resolve using root search paths first
			AssemblyDefinition resolved = null;
			try {
				resolved = image.AssemblyResolver.Resolve(ar, rp);
			} catch {
			}

			if (resolved == null && is_sdk_assembly (ra))
				// FIXME: netcore assemblies have missing references
				continue;

			if (resolved != null) {
				Import (resolved.MainModule.FileName, parent_kind);
			} else {
				var resolve = Resolve (ar.Name, out kind);
				if (resolve != null)
				{
					Import(resolve, kind);
				}
				else
				{
					Console.WriteLine($"Could not resolve {ar.Name}");
				}
			}
		}

		// Resolving satellite assemblies
		if(!invariant_globalization && kind == AssemblyKind.User)
		{
			string resourceFile = GetAssemblyResourceFileName(assemblyFullPath);

			foreach (var subDirectory in Directory.EnumerateDirectories(assemblyDirectory))
			{
				var satelliteAssembly = Path.Combine(subDirectory, resourceFile);
				if (!File.Exists(satelliteAssembly))
				{
					continue;
				}

				string cultureName = subDirectory.Substring(subDirectory.LastIndexOf(Path.DirectorySeparatorChar) + 1);
				string culturePath = Path.Combine(assemblyDirectory, cultureName);

				var satelliteData = new AssemblyData() {
					name = resourceFile.Replace(".dll", ""),
					src_path = satelliteAssembly.Replace("\\", "/"),
					culture = cultureName,
					aot = false
				};

				assemblies.Add(satelliteData);

				file_list.Add(satelliteAssembly);

				Console.WriteLine($"Added satellite assembly {cultureName}/{resourceFile}");
			}
		}

		Console.WriteLine($"Resolved {ra}");
	}

	static string GetAssemblyResourceFileName(string assembly)
		=> Path.GetFileNameWithoutExtension(assembly) + ".resources.dll";

	void GenDriver (string builddir, List<string> profilers, ExecMode ee_mode, bool link_icalls) {
		var symbols = new List<string> ();
		foreach (var adata in assemblies) {
			if (adata.aot)
				symbols.Add (String.Format ("mono_aot_module_{0}_info", adata.name.Replace ('.', '_').Replace ('-', '_')));
		}

		var w = File.CreateText (Path.Combine (builddir, "driver-gen.c.in"));

		foreach (var symbol in symbols) {
			w.WriteLine ($"extern void *{symbol};");
		}

		w.WriteLine ("static void register_aot_modules ()");
		w.WriteLine ("{");
		foreach (var symbol in symbols)
			w.WriteLine ($"\tmono_aot_register_module ({symbol});");
		w.WriteLine ("}");

		foreach (var profiler in profilers) {
			w.WriteLine ($"void mono_profiler_init_{profiler} (const char *desc);");
			w.WriteLine ("EMSCRIPTEN_KEEPALIVE void mono_wasm_load_profiler_" + profiler + " (const char *desc) { mono_profiler_init_" + profiler + " (desc); }");
		}

		switch (ee_mode) {
		case ExecMode.AotInterp:
			w.WriteLine ("#define EE_MODE_LLVMONLY_INTERP 1");
			break;
		case ExecMode.Aot:
			w.WriteLine ("#define EE_MODE_LLVMONLY 1");
			break;
		default:
			break;
		}

		if (link_icalls)
			w.WriteLine ("#define LINK_ICALLS 1");

		w.Close ();
	}

	public static int Main (string[] args) {
		return new Driver ().Run (args);
	}

	enum CopyType
	{
		Default,
		Always,
		IfNewer
	}

	enum ExecMode {
		Interp = 1,
		Aot = 2,
		AotInterp = 3
	}

	enum LinkMode
	{
		SdkOnly,
		All
	}

	class WasmOptions {
		public bool Debug;
		public bool DebugRuntime;
		public bool AddBinding;
		public bool Linker;
		public bool LinkIcalls;
		public bool ILStrip;
		public bool LinkerVerbose;
		public bool EnableZLib;
		public bool EnableFS;
		public bool EnableThreads;
		public bool EnableJiterpreter;
		public bool Simd;
		public bool PrintSkippedAOTMethods;
		public bool EnableDynamicRuntime;
		public bool LinkerExcludeDeserialization;
		public bool EnableCollation;
		public bool EnableICU;
		public bool EnableDedup = true;
		public bool EmccLinkOptimizations = false;
		public bool EnableWasmExceptions = false;
		public bool InvariantGlobalization = false;
	}

	int Run (string[] args) {
		var add_binding = true;
		var root_assemblies = new List<string> ();
		enable_debug = false;
		string builddir = null;
		string sdkdir = null;
		string emscripten_sdkdir = null;
		var aot_assemblies = "";
		var skip_aot_assemblies = "";
		app_prefix = Environment.CurrentDirectory;
		var assembly_root = "managed";
		var vfs_prefix = "managed";
		var target_framework = "net5.0";
		var use_release_runtime = true;
		var enable_aot = false;
		var enable_dedup = true;
		var print_usage = false;
		var emit_ninja = false;
		bool build_wasm = false;
		bool enable_lto = false;
		bool link_icalls = false;
		bool gen_pinvoke = false;
		bool enable_zlib = false;
		bool enable_fs = false;
		bool enable_threads = false;
		bool enable_dynamic_runtime = false;
		bool is_netcore = false;
		bool is_windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
		bool enable_simd = false;
		bool print_skipped_aot_methods = false;
		var il_strip = false;
		var linker_verbose = false;
		var runtimeTemplate = "runtime.js";
		var assets = new List<string> ();
		var profilers = new List<string> ();
		var native_libs = new List<string> ();
		var preload_files = new List<string> ();
		var embed_files = new List<string> ();
		var emcc_exported_runtime_methods = new List<string> ();
		var native_compile = new List<string> ();
		var pinvoke_libs = "";
		var copyTypeParm = "default";
		var copyType = CopyType.Default;
		var ee_mode = ExecMode.Interp;
		var linkModeParm = "all";
		var linkMode = LinkMode.All;
		var linkDescriptor = "";
		var framework = "";
		var runtimepack_dir = "";
		string usermode;
		string runtimeOptions = null;
		string aot_profile = null;
		string aot_compiler_options = "";
		string wasm_runtime_path = null;
		var runtime_config = "release";
		int pthread_pool_size = 4;
		string wasmStackSize = "5MB";
		string illinker_path = "";
		string extra_emccflags = "";
		string extra_linkerflags = "";
		string linker_optimization_level = "";
		string wasm_tuner_path = "";
		var linker_args = new List<string>();

		var opts = new WasmOptions () {
				AddBinding = true,
				Debug = false,
				DebugRuntime = false,
				Linker = false,
				ILStrip = false, // disabled because of https://github.com/dotnet/runtime/issues/50609
				LinkerVerbose = false,
				EnableZLib = false,
				EnableFS = false,
				Simd = false,
				EnableDynamicRuntime = false,
				LinkerExcludeDeserialization = true,
				EnableCollation = false,
				EnableICU = false
			};

		var p = new OptionSet () {
				{ "nobinding", s => opts.AddBinding = false },
				{ "out=", s => out_prefix = s },
				{ "appdir=", s => out_prefix = s },
				{ "builddir=", s => builddir = s },
				{ "mono-sdkdir=", s => sdkdir = s },
				{ "emscripten-sdkdir=", s => emscripten_sdkdir = s },
				{ "runtimepack-dir=", s => runtimepack_dir = s },
				{ "prefix=", s => app_prefix = s },
				{ "wasm-runtime-path=", s => wasm_runtime_path = s },
				{ "deploy=", s => assembly_root = s },
				{ "vfs=", s => vfs_prefix = s },
				{ "target-framework=", s => target_framework = s },
				{ "aot", s => ee_mode = ExecMode.Aot },
				{ "aot-interp", s => ee_mode = ExecMode.AotInterp },
				{ "template=", s => runtimeTemplate = s },
				{ "asset=", s => assets.Add(s) },
				{ "search-path=", s => root_search_paths.Add(s) },
				{ "profile=", s => profilers.Add (s) },
				{ "copy=", s => copyTypeParm = s },
				{ "aot-assemblies=", s => aot_assemblies = s },
				{ "aot-profile=", s => aot_profile = s },
				{ "runtime-config=", s => runtime_config = s },
				{ "pthread-pool-size=", s => int.TryParse(s, out pthread_pool_size) },
				{ "wasm-stack-size=", s => wasmStackSize = s },
				{ "skip-aot-assemblies=", s => skip_aot_assemblies = s },
				{ "aot-compiler-opts=", s => aot_compiler_options = s },
				{ "link-mode=", s => linkModeParm = s },
				{ "link-descriptor=", s => linkDescriptor = s },
				{ "pinvoke-libs=", s => pinvoke_libs = s },
				{ "native-compile=", s => native_compile.Add(s) },
				{ "native-lib=", s => native_libs.Add (s) },
				{ "preload-file=", s => preload_files.Add (s) },
				{ "embed-file=", s => embed_files.Add (s) },
				{ "emcc-exported-runtime-method=", s => emcc_exported_runtime_methods.Add (s) },
				{ "framework=", s => framework = s },
				{ "extra-emccflags=", s => extra_emccflags = s },
				{ "illinker-path=", s => illinker_path = s },
				{ "extra-linkerflags=", s => extra_linkerflags = s },
				{ "runtime-options=", s => runtimeOptions = s },
				{ "linker-optimization-level=", s => linker_optimization_level = s },
				{ "wasm-tuner-path=", s => wasm_tuner_path = s },
				{ "help", s => print_usage = true },
			};

		AddFlag (p, new BoolFlag ("debug", "enable c# debugging", opts.Debug, b => opts.Debug = b));
		AddFlag (p, new BoolFlag ("debugrt", "enable debug runtime", opts.DebugRuntime, b => opts.DebugRuntime = b));
		AddFlag (p, new BoolFlag ("linker", "enable the linker", opts.Linker, b => opts.Linker = b));
		AddFlag (p, new BoolFlag ("binding", "enable the binding engine", opts.AddBinding, b => opts.AddBinding = b));
		AddFlag (p, new BoolFlag ("link-icalls", "link away unused icalls", opts.LinkIcalls, b => opts.LinkIcalls = b));
		AddFlag (p, new BoolFlag ("il-strip", "strip IL code from assemblies in AOT mode", opts.ILStrip, b => opts.ILStrip = b));
		AddFlag (p, new BoolFlag ("linker-verbose", "set verbose option on linker", opts.LinkerVerbose, b => opts.LinkerVerbose = b));
		AddFlag (p, new BoolFlag ("zlib", "enable the use of zlib for System.IO.Compression support", opts.EnableZLib, b => opts.EnableZLib = b));
		AddFlag (p, new BoolFlag ("enable-fs", "enable filesystem support (through Emscripten's file_packager.py in a later phase)", opts.EnableFS, b => opts.EnableFS = b));
		AddFlag (p, new BoolFlag ("threads", "enable threads", opts.EnableThreads, b => opts.EnableThreads = b));
		AddFlag (p, new BoolFlag ("jiterpreter", "enable jiterpreter", opts.EnableJiterpreter, b => opts.EnableJiterpreter = b));
		AddFlag (p, new BoolFlag ("print-skipped-aot-methods", "enable jiterpreter", opts.PrintSkippedAOTMethods, b => opts.PrintSkippedAOTMethods = b));
		AddFlag (p, new BoolFlag ("dedup", "enable dedup pass", opts.EnableDedup, b => opts.EnableDedup = b));
		AddFlag (p, new BoolFlag ("dynamic-runtime", "enable dynamic runtime (support for Emscripten's dlopen)", opts.EnableDynamicRuntime, b => opts.EnableDynamicRuntime = b));
		AddFlag (p, new BoolFlag ("simd", "enable SIMD support", opts.Simd, b => opts.Simd = b));
		AddFlag (p, new BoolFlag ("wasm-exceptions", "enable exceptions", opts.EnableWasmExceptions, b => opts.EnableWasmExceptions = b));
		AddFlag (p, new BoolFlag ("linker-exclude-deserialization", "Link out .NET deserialization support", opts.LinkerExcludeDeserialization, b => opts.LinkerExcludeDeserialization = b));
		AddFlag (p, new BoolFlag ("collation", "enable unicode collation support", opts.EnableCollation, b => opts.EnableCollation = b));
		AddFlag (p, new BoolFlag ("icu", "enable .NET 5+ ICU", opts.EnableICU, b => opts.EnableICU = b));
		AddFlag (p, new BoolFlag ("emcc-link-optimization", "enable emcc link-time optimizations", opts.EmccLinkOptimizations, b => opts.EmccLinkOptimizations = b));
		AddFlag (p, new BoolFlag ("invariant-globalization", "enables invariant globalization", opts.InvariantGlobalization, b => opts.InvariantGlobalization = b));
		p.Add(new ResponseFileSource());		

		var new_args = p.Parse (args).ToArray ();
		foreach (var a in new_args) {
			root_assemblies.Add (a);
		}

		if (print_usage) {
			Usage ();
			return 0;
		}

		if (!Enum.TryParse(copyTypeParm, true, out copyType)) {
			Console.WriteLine("Invalid copy value");
			Usage ();
			return 1;
		}

		if (!Enum.TryParse(linkModeParm, true, out linkMode)) {
			Console.WriteLine("Invalid link-mode value");
			Usage ();
			return 1;
		}

		if (out_prefix == null) {
			Console.Error.WriteLine ("The --appdir= argument is required.");
			return 1;
		}

		enable_debug = opts.Debug;
		enable_linker = opts.Linker;
		add_binding = opts.AddBinding;
		il_strip = opts.ILStrip;
		linker_verbose = opts.LinkerVerbose;
		gen_pinvoke = pinvoke_libs != "";
		enable_zlib = opts.EnableZLib;
		enable_fs = opts.EnableFS;
		enable_threads = opts.EnableThreads;
		enable_dynamic_runtime = opts.EnableDynamicRuntime;
		enable_simd = opts.Simd;
		print_skipped_aot_methods = opts.PrintSkippedAOTMethods;
		invariant_globalization = opts.InvariantGlobalization;

		// Dedup is disabled by default https://github.com/dotnet/runtime/issues/48814
		enable_dedup = opts.EnableDedup;

		if (opts.DebugRuntime) {
			runtime_config = "release";
		} else {
			switch (runtime_config) {
			case "debug":
				enable_debug = true;
				break;

			case "release":
				break;

			case "release-threads":
				enable_threads = true;
				break;

			case "debug-threads":
				enable_threads = true;
				enable_debug = true;
				break;

			case "dynamic-release":
				enable_dynamic_runtime = true;
				break;

			case "dynamic-debug":
				enable_dynamic_runtime = true;
				enable_debug = true;
				break;

			default:
				Console.WriteLine ("Invalid --runtime-config value. Must be either debug, release, dynamic-release, dynamic-debug.");
				Usage ();
				return 1;
			}
		}

		if (ee_mode == ExecMode.Aot || ee_mode == ExecMode.AotInterp)
			enable_aot = true;

		if (enable_aot || opts.Linker)
			enable_linker = true;
		if (opts.LinkIcalls)
			link_icalls = true;
		if (!enable_linker || !enable_aot)
			enable_dedup = false;
		if (enable_aot || link_icalls || gen_pinvoke || profilers.Count > 0 || native_libs.Count > 0 || native_compile.Count > 0 || preload_files.Count > 0 || embed_files.Count > 0) {
			build_wasm = true;
			emit_ninja = true;
		}
		if (!enable_aot && link_icalls)
			enable_lto = true;
		if (ee_mode != ExecMode.Aot)
			// Can't strip out IL code in mixed mode, since the interpreter might execute some methods even if they have AOTed code available
			il_strip = false;

		if (aot_assemblies != "") {
			if (ee_mode != ExecMode.AotInterp) {
				Console.Error.WriteLine ("The --aot-assemblies= argument requires --aot-interp.");
				return 1;
			}
		}
		if (skip_aot_assemblies != "") {
			if (ee_mode != ExecMode.AotInterp) {
				Console.Error.WriteLine ("The --skip-aot-assemblies= argument requires --aot-interp.");
				return 1;
			}
		}
		if (link_icalls && !enable_linker) {
			Console.Error.WriteLine ("The --link-icalls option requires the --linker option.");
			return 1;
		}

		var tool_prefix = runtimepack_dir;

		if (framework != "") {
			if (framework.StartsWith ("net5")) {
				is_netcore = true;
				if (runtimepack_dir == "") {
					Console.Error.WriteLine ("The --runtimepack-dir= argument is required.");
					return 1;
				}
				if (!Directory.Exists (runtimepack_dir)) {
					Console.Error.WriteLine ($"The directory '{runtimepack_dir}' doesn't exist.");
					return 1;
				}
				if (!Directory.Exists (Path.Combine (runtimepack_dir, "runtimes", "browser-wasm"))) {
					Console.Error.WriteLine ($"The directory '{runtimepack_dir}' doesn't contain a 'runtimes/browser-wasm' subdirectory.");
					return 1;
				}
				runtimepack_dir = Path.Combine (runtimepack_dir, "runtimes", "browser-wasm").Replace("\\", "/");
			} else {
				Console.Error.WriteLine ("The only valid value for --framework is 'net5...'");
				return 1;
			}
		}

		if (aot_profile != null && !File.Exists (aot_profile)) {
			Console.Error.WriteLine ($"AOT profile file '{aot_profile}' not found.");
			return 1;
		}

		if (enable_simd && !is_netcore) {
			Console.Error.WriteLine ("--simd is only supported with netcore.");
			return 1;
		}

		//are we working from the tree?
		if (sdkdir != null) {
			framework_prefix = Path.Combine (tool_prefix, "framework"); //all framework assemblies are currently side built to packager.exe
		} else if (Directory.Exists (Path.Combine (tool_prefix, "../out/wasm-bcl/wasm"))) {
			framework_prefix = Path.Combine (tool_prefix, "framework"); //all framework assemblies are currently side built to packager.exe
			sdkdir = Path.Combine (tool_prefix, "../out");
		} else {
			framework_prefix = Path.Combine (tool_prefix, "framework");
			sdkdir = tool_prefix;
		}
		string bcl_root = Path.Combine (sdkdir, "wasm-bcl");
		var bcl_prefix = Path.Combine (bcl_root, "wasm");
		bcl_facades_prefix = Path.Combine (bcl_prefix, "Facades");
		bcl_prefixes = new List<string> ();
		if (is_netcore) {
			bcl_tools_prefix = Path.Combine (sdkdir, "tools");
			/* corelib */
			bcl_prefixes.Add (Path.Combine (runtimepack_dir, "native"));
			/* .net runtime */
			bcl_prefixes.Add (Path.Combine (runtimepack_dir, "lib", "net8.0"));
		} else {
			bcl_tools_prefix = Path.Combine (bcl_root, "wasm_tools");
			bcl_prefixes.Add (bcl_prefix);
		}

		Console.WriteLine("Resolving assemblies");
		foreach (var ra in root_assemblies) {
			AssemblyKind kind;
			var resolved = Resolve (ra, out kind);
			Import (resolved, kind);
		}
		Console.WriteLine("Done resolving assemblies");

		if (enable_aot) {
			var to_aot = new Dictionary<string, bool> (StringComparer.OrdinalIgnoreCase);
			if (is_netcore)
				to_aot ["System.Private.CoreLib"] = true;
			else
				to_aot ["mscorlib"] = true;
			if (aot_assemblies != "") {
				foreach (var s in aot_assemblies.Split (','))
					to_aot [s] = true;
			}
			foreach (var ass in assemblies) {
				if (aot_assemblies == "" || to_aot.ContainsKey (ass.name)) {
					ass.aot = true;

					if(ass.culture is not null)
					{
						// Satellite assemblies cannot be AOTed as they're
						// implicitly duplicates.
						ass.aot = false;
					}

					to_aot.Remove (ass.name);
				}
			}

			if (to_aot.Count > 0) {
				Console.WriteLine ("Skipping AOT for unknown assembly names '" + string.Join(",", to_aot.Keys) + "' in --aot-assemblies option.");
			}

			if(skip_aot_assemblies != "") {
				var skipList = skip_aot_assemblies.Split(',');

				foreach(var asm in assemblies) {
					if (skipList.Any(s => asm.name.Equals(s, StringComparison.OrdinalIgnoreCase))) {
						Console.WriteLine ($"Disabling AOT for {asm.name}");
						asm.aot = false;
					}
				}
			}
		}

		if (builddir != null) {
			emit_ninja = true;
			if (!Directory.Exists (builddir))
				Directory.CreateDirectory (builddir);
		}

		if (!emit_ninja)
		{
			if (!Directory.Exists(out_prefix))
				Directory.CreateDirectory(out_prefix);
			var bcl_dir = Path.Combine(out_prefix, assembly_root);
			if (Directory.Exists(bcl_dir))
				Directory.Delete(bcl_dir, true);
			Directory.CreateDirectory(bcl_dir);

			file_list.AsParallel().ForAll(f =>
			{
				var fileName = Path.GetFileName(f);

				if (IsResourceAssembly(f, out var culture))
				{
					fileName = Path.Combine(culture, fileName);
				}

				CopyFile(f, Path.Combine(bcl_dir, fileName), copyType);
			});
		}

		if (assembly_root.EndsWith ("/"))
			assembly_root = assembly_root.Substring (0, assembly_root.Length - 1);
		if (vfs_prefix.EndsWith ("/"))
			vfs_prefix = vfs_prefix.Substring (0, vfs_prefix.Length - 1);

		string src_prefix = is_netcore ? Path.Combine(runtimepack_dir, "native") : Path.Combine(tool_prefix, "src");

		// wasm core bindings module
		var wasm_core_bindings = string.Empty;
		if (add_binding) {
			wasm_core_bindings = BINDINGS_MODULE;
		}
		// wasm core bindings support file
		var wasm_core_support = string.Empty;
		var wasm_core_support_library = string.Empty;
		if (add_binding) {

			if (is_netcore)
			{
				wasm_core_support_library += $"--js-library " + Path.Combine(src_prefix, "pal_random.lib.js") + " ";
			}
		}
		var runtime_js = Path.Combine (emit_ninja ? builddir : out_prefix, "runtime.js");
		if (emit_ninja) {
			File.Delete (runtime_js);
			File.Copy (runtimeTemplate, runtime_js);
		} else {
			if (File.Exists(runtime_js) && (File.Exists(runtimeTemplate))) {
				CopyFile (runtimeTemplate, runtime_js, CopyType.IfNewer, $"runtime template <{runtimeTemplate}> ");
			} else {
				if (File.Exists(runtimeTemplate))
					CopyFile (runtimeTemplate, runtime_js, CopyType.IfNewer, $"runtime template <{runtimeTemplate}> ");
				else {
					var runtime_gen = "\nvar Module = {\n\tonRuntimeInitialized: function () {\n\t\tMONO.mono_load_runtime_and_bcl (\n\t\tconfig.vfs_prefix,\n\t\tconfig.deploy_prefix,\n\t\tconfig.enable_debugging,\n\t\tconfig.file_list,\n\t\tfunction () {\n\t\t\tApp.init ();\n\t\t}\n\t)\n\t},\n};";
					File.Delete (runtime_js);
					File.WriteAllText (runtime_js, runtime_gen);
				}
			}
		}

		AssemblyData dedup_asm = null;

		if (enable_dedup) {
			dedup_asm = new AssemblyData () { name = "aot-instances",
					filename = "aot-instances.dll",
					bc_path = Path.Combine("$builddir", "aot-instances.dll.bc"),
					o_path = Path.Combine("$builddir", "aot-instances.dll.o"),
					app_path = Path.Combine("$appdir", "$deploy_prefix", "aot-instances.dll"),
					linkout_path = Path.Combine("$builddir", "linker-out", "aot-instances.dll"),
					aot = true
					};
			assemblies.Add (dedup_asm);
			file_list.Add ("aot-instances.dll");
		}

		file_list.Add ("dotnet.native.wasm");
		file_list.Add ("dotnet.native.js");
		file_list.Add ("dotnet.runtime.js");

		if (enable_threads) {
			file_list.Add("dotnet.native.worker.js");
		}

		string wasm_runtime_dir;
		if (is_netcore) {
			wasm_runtime_dir = Path.Combine (runtimepack_dir, "native");
		} else {
			if (wasm_runtime_path == null)
				wasm_runtime_path = Path.Combine (tool_prefix, "builds");

			if (enable_threads)
				wasm_runtime_dir = Path.Combine (wasm_runtime_path, use_release_runtime ? "threads-release" : "threads-debug");
			else if (enable_dynamic_runtime)
				wasm_runtime_dir = Path.Combine (wasm_runtime_path, use_release_runtime ? "dynamic-release" : "dynamic-debug");
			else
				wasm_runtime_dir = Path.Combine (wasm_runtime_path, use_release_runtime ? "release" : "debug");
		}

		if (is_netcore)
		{
			if (opts.EnableICU)
			{
				foreach (var icudat in Directory.EnumerateFiles(wasm_runtime_dir))
				{
					if (Path.GetFileName(icudat).StartsWith("icudt.dat"))
					{
						file_list.Add(icudat);
					}
				}
			}
			else
			{
				// ICU fails when running with AOT, reason yet unknown
				Console.WriteLine("WARNING: ICU Disabled when using AOT");
			}
		}

		var file_list_str = string.Join(",\n", file_list.Distinct().Select(f =>
		{
			var fileName = Path.GetFileName(f).ToLower(); ;

			var assetType = Path.GetExtension(f).ToLowerInvariant() switch
			{
				".dll" => "assembly",
				".pdb" => "assembly", // PDBs are loaded through https://github.com/dotnet/runtime/blob/55d35231b48ec0a66835a2bd71a968baf8ad9a12/src/mono/wasm/runtime/assets.ts#L411-L412
				".wasm" => "dotnetwasm",
				".js" when fileName is "dotnet.native.worker.js" => "js-module-threads",
				".js" when fileName is "dotnet.native.js" => "js-module-native",
				".js" when fileName is "dotnet.runtime.js" => "js-module-runtime",
				".js" when fileName is "dotnet.js" => "js-module-dotnet",
				".dat" => "icu",
				_ => throw new Exception($"Unsupported asset type")
			};

			string cultureField = null;
			string culturePathPrefix = null;

			if (assetType is "assembly")
			{
				if(IsResourceAssembly(f, out var culture))
				{
					assetType = "resource";
					cultureField = $", \"culture\":\"{Path.GetFileName(Path.GetDirectoryName(f))}\"";
					culturePathPrefix = $"{culture}/";
				}
			}

			return $" {{ \"name\": \"{culturePathPrefix}{Path.GetFileName(f)}\",\"virtualPath\": \"{Path.GetFileName(f)}\", \"loadRemote\": true, \"behavior\":\"{assetType}\" {cultureField} }}";
		}));
		var debugLevel = enable_debug ? " -1" : "0";

		// Follow https://github.com/dotnet/runtime/blob/e57438026c25707bf6dd52cd332db657e919bbd4/src/mono/wasm/runtime/dotnet.d.ts#L80

		var configOptions = new Dictionary<string, string>()
		{
			["assemblyRootFolder"] = $"\"{assembly_root}\"",
			["debugLevel"] = debugLevel.ToString(),
		};

		if (enable_threads)
		{
			configOptions["pthreadPoolSize"] = pthread_pool_size.ToString();
		}

		var runtimeOptionsSet = runtimeOptions?.Split(" ")?.ToHashSet() ?? new HashSet<string>(3);
		foreach (var jiterpreterOption in jiterpreterOptions)
		{
			if (opts.EnableJiterpreter)
			{
				if (!runtimeOptionsSet.Contains($"--no-{jiterpreterOption}"))
				{
					runtimeOptionsSet.Add($"--{jiterpreterOption}");
				}
			}
			else
			{
				if (!runtimeOptionsSet.Contains($"--{jiterpreterOption}"))
				{
					runtimeOptionsSet.Add($"--no-{jiterpreterOption}");
				}
			}
		}

		configOptions["runtimeOptions"] = $"[{string.Join(",", runtimeOptionsSet.Select(o => $"\"{o}\""))}]";
		configOptions["remoteSources"] = $"[\"{assembly_root}/\"]";
		configOptions["globalizationMode"] = "\"all\"";

		var config = $"{{" +
			string.Join(",", configOptions.Select(o => $"\n \t\"{o.Key}\": {o.Value}")) + "," +
			$"\n \t\"assets\": [ " + file_list_str + "]\n" +
			$"}}";

		var config_json = Path.Combine(emit_ninja ? builddir : out_prefix, "mono-config.json");
		File.Delete(config_json);
		File.WriteAllText(config_json, config);

		if (!emit_ninja) {
			var interp_files = new List<string> { "dotnet.js", "dotnet.native.wasm", "dotnet.runtime.js", "dotnet.native.js" };

			if (enable_threads) {
				interp_files.Add ("dotnet.native.worker.js");
			}
			foreach (var fname in interp_files) {
				File.Delete (Path.Combine (out_prefix, fname));
				File.Copy (
						   Path.Combine (wasm_runtime_dir, fname),
						   Path.Combine (out_prefix, fname));
			}

			assets.AsParallel().ForAll(asset =>
			{
				CopyFile(asset,
						Path.Combine(out_prefix, Path.GetFileName(asset)), copyType, "Asset: ");
			});
		}

		if (!emit_ninja)
			return 0;

		if (builddir == null) {
			Console.Error.WriteLine ("The --builddir argument is required.");
			return 1;
		}

		var filenames = new Dictionary<string, string> ();
		foreach (var a in assemblies) {
			var assembly = a.src_path;
			if (assembly == null)
				continue;
			string filename = Path.GetFileName (assembly);
			if (filenames.ContainsKey (filename) && !filename.EndsWith(".resources.dll",	StringComparison.OrdinalIgnoreCase)) {
				Console.WriteLine ("Duplicate input assembly: " + assembly + " " + filenames [filename]);
				return 1;
			}
			filenames [filename] = assembly;
		}

		if (build_wasm) {
			if (sdkdir == null) {
				Console.WriteLine ("The --mono-sdkdir argument is required.");
				return 1;
			}
			if (emscripten_sdkdir == null) {
				Console.WriteLine ("The --emscripten-sdkdir argument is required.");
				return 1;
			}
			GenDriver (builddir, profilers, ee_mode, link_icalls);
		}

		string runtime_dir;
		string runtime_libdir;
		if (is_netcore) {
			runtime_dir = "$runtimepack_dir/native";
			runtime_libdir = "$runtimepack_dir/native";
		} else {
			runtime_dir = "$mono_sdkdir/wasm-runtime-release";
			runtime_libdir = $"{runtime_dir}/lib";
		}
		string runtime_libs = "";
		if (ee_mode == ExecMode.Interp || ee_mode == ExecMode.AotInterp || link_icalls) {
			runtime_libs += $"$runtime_libdir/libmono-ee-interp.a ";
			// We need to link the icall table because the interpreter uses it to lookup icalls even if the aot-ed icall wrappers are available
			if (!link_icalls)
				runtime_libs += $"$runtime_libdir/libmono-icall-table.a ";
		}
		runtime_libs += $"$runtime_libdir/libmonosgen-2.0.a ";
		if (is_netcore)
		{
			runtime_libs += $"$runtime_libdir/wasm-bundled-timezones.a ";

			if (enable_simd)
			{
				runtime_libs += $"$runtime_libdir/libmono-wasm-simd.a ";
			}
			else
			{
				runtime_libs += $"$runtime_libdir/libmono-wasm-nosimd.a ";
			}

			runtime_libs += $"$runtime_libdir/libSystem.Native.a ";
			runtime_libs += $"$runtime_libdir/libSystem.IO.Compression.Native.a ";
			runtime_libs += $"$runtime_libdir/libSystem.Globalization.Native.a ";
			runtime_libs += $"$runtime_libdir/libicuuc.a ";
			runtime_libs += $"$runtime_libdir/libicui18n.a ";
			runtime_libs += $"$runtime_libdir/libicudata.a ";

			runtime_libs += opts.EnableWasmExceptions
				? $"$runtime_libdir/libmono-wasm-eh-wasm.a "
				: $"$runtime_libdir/libmono-wasm-eh-js.a ";

			if (enable_debug)
			{
				runtime_libs += $"$runtime_libdir/libmono-component-marshal-ilgen-static.a ";
				runtime_libs += $"$runtime_libdir/libmono-component-diagnostics_tracing-static.a ";
				runtime_libs += $"$runtime_libdir/libmono-component-hot_reload-static.a ";
				runtime_libs += $"$runtime_libdir/libmono-component-debugger-static.a ";
			}
			else
			{
				// ilgen is required most of the time and we'll need to include it conditionally
				// when incompatible assemblies are detected: https://github.com/dotnet/runtime/blob/8b25fd382260e8eafbfd77f64b0fe28dc7301c2e/src/tasks/MonoTargetsTasks/MarshalingPInvokeScanner/MarshalingPInvokeScanner.cs#L21
				// runtime_libs += $"$runtime_libdir/libmono-component-marshal-ilgen-stub-static.a ";

				// For now, we include the component always, as even basic tests require the component
				// to be linked in.
				 runtime_libs += $"$runtime_libdir/libmono-component-marshal-ilgen-static.a ";

				runtime_libs += $"$runtime_libdir/libmono-component-diagnostics_tracing-stub-static.a ";
				runtime_libs += $"$runtime_libdir/libmono-component-hot_reload-stub-static.a ";
				runtime_libs += $"$runtime_libdir/libmono-component-debugger-stub-static.a ";
			}
		}
		else
			runtime_libs += $"$runtime_libdir/libmono-native.a ";

		string emcc_flags = "";

		string aot_args = "llvm-path=\"$emscripten_sdkdir/upstream/bin\",";
		string profiler_libs = "";
		string profiler_aot_args = "";
		foreach (var profiler in profilers) {
			if (is_netcore)
			{
				profiler_libs += $"$runtime_libdir/libmono-profiler-{profiler}.a ";
			}
			else
			{
				profiler_libs += $"$runtime_libdir/libmono-profiler-{profiler}-static.a ";
			}
			if (profiler_aot_args != "")
				profiler_aot_args += " ";
			profiler_aot_args += $"--profile={profiler}";

			if (profiler == "aot")
			{
				// related to driver.c conditionals
				emcc_flags += " -DENABLE_AOT_PROFILER=1 ";
			}
		}
		string extra_link_libs = "";
		foreach (var lib in native_libs)
			extra_link_libs += $"{EscapePath(lib)} ";
		if (aot_profile != null) {
			CopyFile (aot_profile, Path.Combine (builddir, Path.GetFileName (aot_profile)), CopyType.IfNewer, "");
			aot_args += $"profile=\"{aot_profile}\",profile-only,";
		}
		if (ee_mode == ExecMode.AotInterp)
			aot_args += "interp,";
		if (build_wasm)
			enable_zlib = true;
		if (is_netcore)
			enable_zlib = false;

		wasm_runtime_dir = Path.GetFullPath (wasm_runtime_dir);
		sdkdir = Path.GetFullPath (sdkdir);
		out_prefix = Path.GetFullPath (out_prefix);

		string driver_deps = "";
		if (link_icalls)
			driver_deps += " $builddir/icall-table.h";
		if (gen_pinvoke)
			driver_deps += " $builddir/pinvoke-table.h";
		if (enable_lto)
			emcc_flags += "--llvm-lto 1 ";
		if (enable_zlib || is_netcore)
			emcc_flags += "-s USE_ZLIB=1 ";
		if (enable_fs)
			emcc_flags += "-s FORCE_FILESYSTEM=1 ";
		foreach (var pf in preload_files)
			emcc_flags += "--preload-file " + pf + " ";
		foreach (var f in embed_files)
			emcc_flags += "--embed-file " + f + " ";

		var emcc_link_flags = new List<string>();
		if (enable_debug || !opts.EmccLinkOptimizations)
		{
			emcc_link_flags.Add("-O0 ");
		}
		else
		{
			emcc_link_flags.Add(linker_optimization_level);
		}

		if (enable_threads)
		{
			emcc_link_flags.Add("-s USE_PTHREADS=1");
			emcc_link_flags.Add("-Wno-pthreads-mem-growth");

			emcc_flags += "-s USE_PTHREADS=1 ";
			emcc_flags += "-Wno-pthreads-mem-growth ";

			aot_compiler_options += " --wasm-gc-safepoints ";
		}

		if (opts.EnableWasmExceptions)
		{
			emcc_link_flags.Add("-fwasm-exceptions");
			emcc_flags += " -fwasm-exceptions ";
			aot_compiler_options += " --wasm-exceptions ";
		}
		else
		{
			emcc_flags += " -s DISABLE_EXCEPTION_CATCHING=0 ";
		}

		emcc_flags += " -s EXPORT_ES6=1 ";

		// https://github.com/dotnet/runtime/blob/0a57a9b20905b1e14993dc4604bad3bdf0b57fa2/src/mono/wasm/wasm.proj#L187
		emcc_exported_runtime_methods.Add("FS");
		emcc_exported_runtime_methods.Add("out");
		emcc_exported_runtime_methods.Add("err");
		emcc_exported_runtime_methods.Add("ccall");
		emcc_exported_runtime_methods.Add("cwrap");
		emcc_exported_runtime_methods.Add("setValue");
		emcc_exported_runtime_methods.Add("getValue");
		emcc_exported_runtime_methods.Add("UTF8ToString");
		emcc_exported_runtime_methods.Add("UTF8ArrayToString");
		emcc_exported_runtime_methods.Add("stringToUTF8Array");
		emcc_exported_runtime_methods.Add("FS_createPath");
		emcc_exported_runtime_methods.Add("FS_createDataFile");
		emcc_exported_runtime_methods.Add("removeRunDependency");
		emcc_exported_runtime_methods.Add("addRunDependency");
		emcc_exported_runtime_methods.Add("addFunction");
		emcc_exported_runtime_methods.Add("safeSetTimeout");
		emcc_exported_runtime_methods.Add("runtimeKeepalivePush");
		emcc_exported_runtime_methods.Add("runtimeKeepalivePop");
		emcc_exported_runtime_methods.Add("maybeExit");

		// Additional uno-only exports
		emcc_exported_runtime_methods.Add("FS_readFile");
		emcc_exported_runtime_methods.Add("lengthBytesUTF8");
		emcc_exported_runtime_methods.Add("stringToUTF8");
		emcc_exported_runtime_methods.Add("removeFunction");
		emcc_exported_runtime_methods.Add("IDBFS");
		emcc_exported_runtime_methods.Add("print");

		var exports = string.Join(",", emcc_exported_runtime_methods.Distinct().Select(m => $"\'{m}\'"));

		emcc_link_flags.Add("-s EXPORTED_RUNTIME_METHODS=\"[" + exports + "]\"");

		// https://github.com/dotnet/runtime/blob/0a57a9b20905b1e14993dc4604bad3bdf0b57fa2/src/mono/wasm/wasm.proj#L202
		List<string> exportedFunctions = new()
		{
			"_fmod" ,
			"_atan2" ,
			"_fma" ,
			"_pow" ,
			"_fmodf" ,
			"_atan2f" ,
			"_fmaf" ,
			"_powf" ,

			"_asin" ,
			"_asinh" ,
			"_acos" ,
			"_acosh" ,
			"_atan" ,
			"_atanh" ,
			"_cbrt" ,
			"_cos" ,
			"_cosh" ,
			"_exp" ,
			"_log" ,
			"_log2" ,
			"_log10" ,
			"_sin" ,
			"_sinh" ,
			"_tan" ,
			"_tanh" ,

			"_asinf" ,
			"_asinhf" ,
			"_acosf" ,
			"_acoshf" ,
			"_atanf" ,
			"_atanhf" ,
			"_cbrtf" ,
			"_cosf" ,
			"_coshf" ,
			"_expf" ,
			"_logf" ,
			"_log2f" ,
			"_log10f" ,
			"_sinf" ,
			"_sinhf" ,
			"_tanf" ,
			"_tanhf" ,

			// Uno specific
			"_malloc" ,
			"stackSave",
			"stackRestore",
			"stackAlloc",
			"_memalign",
			"_memset",
			"_htons",
			"_ntohs",
			"_free"
		};

		if (enable_threads)
		{
			// https://github.com/dotnet/runtime/blob/6c3a197c4e01bb40c58e7c88370f92acbd53d81c/src/mono/wasm/wasm.proj#L265
			exportedFunctions.Add("_emscripten_main_runtime_thread_id");
		}

		var exportedFunctionsValue = string.Join(",", exportedFunctions.Distinct());
		emcc_link_flags.Add($"-s EXPORTED_FUNCTIONS={exportedFunctionsValue}");

		//  workaround for https://github.com/emscripten-core/emscripten/issues/18034
		emcc_link_flags.Add($"-s TEXTDECODER=0");

		// Align with https://github.com/dotnet/runtime/blob/0a57a9b20905b1e14993dc4604bad3bdf0b57fa2/src/mono/wasm/wasm.proj#L279
		emcc_link_flags.Add("-s EXPORT_ES6=1");
		emcc_link_flags.Add("-s ALLOW_MEMORY_GROWTH=1");
		emcc_link_flags.Add("-s NO_EXIT_RUNTIME=1");
		emcc_link_flags.Add("-s FORCE_FILESYSTEM=1");

		emcc_link_flags.Add("--source-map-base http://example.com");
		emcc_link_flags.Add("-s EXPORT_NAME=\"'createDotnetRuntime'\"");
		emcc_link_flags.Add("-s MODULARIZE=1");
		emcc_link_flags.Add("-s ENVIRONMENT=\"web,webview,worker,node,shell\"");

		emcc_link_flags.Add("-s ALLOW_TABLE_GROWTH=1");
		emcc_link_flags.Add($"-s STACK_SIZE={wasmStackSize}");

		emcc_link_flags.Add("-s WASM_BIGINT=1");
		emcc_link_flags.Add("-s ERROR_ON_UNDEFINED_SYMBOLS=1");
		emcc_link_flags.Add("-s \"DEFAULT_LIBRARY_FUNCS_TO_INCLUDE=[\'memset\']\"");

		// https://github.com/dotnet/runtime/blob/0a57a9b20905b1e14993dc4604bad3bdf0b57fa2/src/mono/wasm/wasm.proj#L303
		emcc_link_flags.Add("-Wno-limited-postlink-optimizations");

		var failOnError = is_windows
			? "; if ($$LastExitCode -ne 0) { exit 1; }"
			: "";

		if (enable_simd) {
			aot_args += "mattr=simd,";
			emcc_flags += "-msimd128 ";
			emcc_flags += "-DCONFIGURATION_COMPILE_OPTIONS=\"-msimd128\" -DCONFIGURATION_INTERPSIMDTABLES_LIB=\"simd\" ";
		}

		if (print_skipped_aot_methods)
		{
			aot_args += "print-skipped,";
		}

		if (is_netcore) {
			emcc_flags += $"-DGEN_PINVOKE -I{src_prefix} ";

			// No need to emit LLVM, we're not using LTO options
			// emcc_flags += $"-emit-llvm ";
		}
		if (!use_release_runtime)
			// -s ASSERTIONS=2 is very slow
			emcc_flags += "-s ASSERTIONS=1 ";

		if (!string.IsNullOrEmpty(extra_emccflags)) {
			emcc_flags += " " + extra_emccflags + " ";
		}

		var ninja = File.CreateText (Path.Combine (builddir, "build.ninja"));
		var linkerResponse = Path.Combine (builddir, "linker.rsp");

		// Defines
		ninja.WriteLine ($"mono_sdkdir = {sdkdir}");
		ninja.WriteLine ($"emscripten_sdkdir = {emscripten_sdkdir}");
		ninja.WriteLine ($"tool_prefix = {tool_prefix}");
		ninja.WriteLine ($"appdir = {out_prefix}");
		ninja.WriteLine ($"builddir = .");
		if (is_netcore)
			ninja.WriteLine ($"runtimepack_dir = {runtimepack_dir}");
		ninja.WriteLine ($"wasm_runtime_dir = {wasm_runtime_dir}");
		ninja.WriteLine ($"runtime_libdir = {runtime_libdir}");
		ninja.WriteLine ($"deploy_prefix = {assembly_root}");
		ninja.WriteLine ($"bcl_dir = {bcl_prefix}");
		ninja.WriteLine ($"bcl_facades_dir = {bcl_facades_prefix}");
		ninja.WriteLine ($"framework_dir = {framework_prefix}");
		ninja.WriteLine ($"tools_dir = {bcl_tools_prefix}");
		ninja.WriteLine ($"linker_dir = {illinker_path}");

		if (!is_windows)
		{
			// emsdk is setup from the bootstrapper to avoid concurrency issues
			// in the emsdk environment setup tooling
			ninja.WriteLine($"emsdk_env = $builddir/emsdk_env.sh");
		}
		else
		{
			ninja.WriteLine($"emsdk_env = emsdkenv.cmd");
		}

		if (add_binding) {
			ninja.WriteLine ($"wasm_core_bindings = $builddir/{BINDINGS_MODULE}");
			ninja.WriteLine ($"wasm_core_support = {wasm_core_support}");
			ninja.WriteLine ($"wasm_core_support_library = {wasm_core_support_library}");
		} else {
			ninja.WriteLine ("wasm_core_bindings =");
			ninja.WriteLine ("wasm_core_support =");
			ninja.WriteLine ("wasm_core_support_library =");
		}

		if (is_netcore)
		{
			var ext = is_windows ? ".exe" : "";
			ninja.WriteLine ($"cross = $runtimepack_dir/native/cross/browser-wasm/mono-aot-cross{ext}");
		}
		else
		{
			ninja.WriteLine ("cross = $mono_sdkdir/wasm-cross-release/bin/wasm32-unknown-none-mono-sgen");
		}

		if (Environment.OSVersion.Platform != PlatformID.Win32NT)
		{
			ninja.WriteLine ("emcc = source $emsdk_env && PYTHONUTF8=1 LC_ALL=C.UTF-8 emcc");
		}
		else
		{
			ninja.WriteLine("emcc = $$env:PYTHONUTF8=1; emcc ");
		}

		ninja.WriteLine ("wasm_opt = $emscripten_sdkdir/upstream/bin/wasm-opt");
		ninja.WriteLine ($"emcc_flags = -DENABLE_METADATA_UPDATE=1 {emcc_flags} ");
		ninja.WriteLine ($"aot_base_args = llvmonly,asmonly,no-opt,static,direct-icalls,deterministic,nodebug,{aot_args}");

		var environment = new List<string>
		{
			"MONO_PATH=$mono_path"
		};

		if (enable_threads)
		{
			// environment.Add("MONO_THREADS_SUSPEND=coop");
		}

		var response_prefix = is_windows
			? "`@"
			: "@";

		var aot_cross_prefix = is_windows
			? $"cmd /c set \"MONO_PATH=$mono_path\" &&" + string.Join(" ", environment.Select(e => $"set \"{e}\" &&"))
			: string.Join(" ", environment);

		// Rules
		ninja.WriteLine ("rule aot");
		ninja.WriteLine ($"  command = {aot_cross_prefix} $cross --debug {profiler_aot_args} {aot_compiler_options} --aot=$aot_args,$aot_base_args,depfile=$depfile,llvm-outfile=$outfile $src_file");
		ninja.WriteLine ("  description = [AOT] $src_file -> $outfile");

		ninja.WriteLine ("rule aot-instances");
		ninja.WriteLine ($"  command = {aot_cross_prefix} $cross  --response=$builddir/aot-instances.rsp");
		ninja.WriteLine ($"  rspfile = $builddir/aot-instances.rsp");
		ninja.WriteLine ($"  rspfile_content = --debug {profiler_aot_args} {aot_compiler_options} --aot=$aot_base_args,llvm-outfile=$outfile,dedup-include=$dedup_image $src_files");
		ninja.WriteLine ("  description = [AOT-INSTANCES] $outfile");

		ninja.WriteLine ("rule mkdir");
		if (Environment.OSVersion.Platform != PlatformID.Win32NT)
		{
			ninja.WriteLine("  command = mkdir -p $out");
		}
		else
		{
			ninja.WriteLine("  command = powershell mkdir -Force -p '$out' | Out-Null");
		}

		var cpCommand = Environment.OSVersion.Platform == PlatformID.Win32NT ? "copy" : "cp";
		var commandPrefix = Environment.OSVersion.Platform == PlatformID.Win32NT ? "powershell " : "";

		if (Environment.OSVersion.Platform != PlatformID.Win32NT)
		{
			ninja.WriteLine ("rule cp");
			ninja.WriteLine ($"  command = {commandPrefix} {cpCommand} $in $out");
			ninja.WriteLine ($"  description = [CP] $in -> $out");
			ninja.WriteLine("rule cpifdiff");
			// Copy $in to $out only if it changed
			ninja.WriteLine ($"  command = /bin/bash -c \"if cmp -s $in $out ; then : ; else {cpCommand} $in $out ; fi\"");

			ninja.WriteLine ("rule cpifdiffex");
			ninja.WriteLine ($"  command = /bin/bash -c \"if [ -f $in ] && [ `cmp -s $in $out` ] ; then : ; else {cpCommand} $in $out ; fi\"");

			ninja.WriteLine ("  restat = true");
			ninja.WriteLine ("  description = [CPIFDIFFEX] $in -> $out");
		}
		else
		{
			ninja.WriteLine ("rule cp");
			ninja.WriteLine ($"  command = cmd /V:ON /c set \"in1=$in\" & set \"in2=!in1:/=\\!\" & set \"out1=$out\" & set \"out2=!out1:/=\\!\" & copy /y !in2! !out2! 1> NUL");
			ninja.WriteLine ($"  description = [CP] $in -> $out");

			ninja.WriteLine ("rule cpifdiff");
			ninja.WriteLine ($"  command = cmd /V:ON /c set \"in1=$in\" & set \"in2=!in1:/=\\!\" & set \"out1=$out\" & set \"out2=!out1:/=\\!\" & copy /y !in2! !out2! 1> NUL");
			ninja.WriteLine ($"  description = [CPIFDIFF] $in -> $out");

			ninja.WriteLine ("rule cpifdiffex");
			ninja.WriteLine ($"  command = cmd /V:ON /c set \"in1=$in\" & set \"in2=!in1:/=\\!\" & set \"out1=$out\" & set \"out2=!out1:/=\\!\" & if exist !in2! copy /y !in2! !out2! 1> NUL");
			ninja.WriteLine ( "  restat = true");
			ninja.WriteLine ( "  description = [CPIFDIFFEX] $in -> $out");
		}

		var emcc_shell_prefix = is_windows
			? "powershell"
			: "bash -c";

		var tools_shell_prefix = is_windows
			? "powershell"
			: "";

		ninja.WriteLine("rule create-emsdk-env");

		if (is_windows)
		{
			ninja.WriteLine($"  command = cmd /c \"$emscripten_sdkdir/emsdk.bat\" construct_env > $out");
		}
		else
		{
			ninja.WriteLine($"  command = {tools_shell_prefix} \"$emscripten_sdkdir/emsdk\" construct_env > $out");
		}

		ninja.WriteLine ("rule emcc");
		ninja.WriteLine ($"  command = {emcc_shell_prefix} \"$emcc $emcc_flags $flags -Oz -c -o $out $in\"");
		ninja.WriteLine ("  description = [EMCC] $in -> $out");

		var src_prefix_es6 = Path.Combine(src_prefix, "es6") + Path.DirectorySeparatorChar;

		// Additional parameters are not supported in the same way between linux and windows.
		var jsAdditionals =
			$"--pre-js {src_prefix_es6}dotnet.es6.pre.js  " +
			$"--js-library {src_prefix_es6}dotnet.es6.lib.js " +
			$"--extern-post-js {src_prefix_es6}dotnet.es6.extpost.js " +
			wasm_core_support_library;

		var emcc_link_additionals_command = is_windows ? jsAdditionals : "";
		var emcc_link_additionals_response = is_windows ? "" : jsAdditionals;

		// Prevents https://github.com/emscripten-core/emscripten/blob/347262aec9c4450e34b6af617d1420dbda2f6662/src/preamble.js#L945 to remove
		// the `env` member: https://github.com/emscripten-core/emscripten/blob/347262aec9c4450e34b6af617d1420dbda2f6662/emcc.py#L2534
		// which is being used here: https://github.com/dotnet/runtime/blob/e131899322693dff60b835a83bbf02f7916e3991/src/mono/wasm/runtime/startup.ts#LL446C1-L446C10
		emcc_link_flags.Add("-s ASSERTIONS=1");

		ninja.WriteLine("rule emcc-link");
		ninja.WriteLine($"  command = {emcc_shell_prefix} \"$emcc {response_prefix}$builddir/emcc_link.rsp {emcc_link_additionals_command} {failOnError} \"");
		ninja.WriteLine($"  rspfile = $builddir/emcc_link.rsp");
		ninja.WriteLine($"  rspfile_content = $emcc_flags {string.Join(" ", emcc_link_flags)} -v -o \"$out_js\" -s MODULARIZE=1 {emcc_link_additionals_response} $in");
		ninja.WriteLine($"  description = [EMCC-LINK] $in -> $out");

		ninja.WriteLine ("rule linker");
		var linkerBin = "dotnet \'$linker_dir/illink.dll\'";

		var linkerSearchPaths = root_search_paths.Concat(bcl_prefixes).Distinct().Select(p => $"-d \"{p}\" ");

		var tunerBinary = string.IsNullOrEmpty(wasm_tuner_path)
				? $"$tools_dir{Path.DirectorySeparatorChar}wasm-tuner.dll"
				: wasm_tuner_path;

		var tunerCommand = $"dotnet '{tunerBinary}'";

		var exitCommand = is_windows ? failOnError : "|| exit 1";

		linker_args.Add($"-out ./linker-out --deterministic --disable-opt unreachablebodies");
		linker_args.Add($"--strip-link-attributes");
		linker_args.Add(extra_linkerflags);
		linker_args.AddRange(linkerSearchPaths);

		ninja.WriteLine ($"  command = {tools_shell_prefix} {linkerBin} \'@{linkerResponse}\' {exitCommand}; {tunerCommand} --gen-empty-assemblies \'@$builddir/tuner.rsp\'");
		ninja.WriteLine ("  rspfile = $builddir/tuner.rsp");
		ninja.WriteLine ("  rspfile_content = $out");
		ninja.WriteLine ("  description = [IL-LINK]");
		ninja.WriteLine ("rule aot-instances-dll");

		if (is_windows)
		{
			ninja.WriteLine($"  command = cmd /c \"dotnet new classlib -o aot-instances && del aot-instances\\*.cs && dotnet build aot-instances\\aot-instances.csproj /r -p:Deterministic=true -p:ImplicitUsings=false -p:TargetFramework={target_framework} -p:UseSharedCompilation=false /p:OutputPath=..\\linker-out\"");
		}
		else
		{
			ninja.WriteLine($"  command = dotnet new classlib -o aot-instances; rm aot-instances/*.cs; dotnet build aot-instances/aot-instances.csproj /r -p:Deterministic=true -p:ImplicitUsings=false -p:TargetFramework={target_framework} -p:UseSharedCompilation=false /p:OutputPath=../linker-out/");
		}

		ninja.WriteLine ("rule gen-runtime-icall-table");
		ninja.WriteLine ($"  command = {aot_cross_prefix} $cross --print-icall-table > $out");
		ninja.WriteLine ("rule gen-icall-table");
		ninja.WriteLine ($"  command = {tools_shell_prefix} {tunerCommand} --gen-icall-table $out $runtime_table $in");
		ninja.WriteLine ("rule gen-pinvoke-table");
		ninja.WriteLine ($"  command = {tools_shell_prefix} {tunerCommand} --gen-pinvoke-table \'@$builddir/gen-pinvoke.rsp\'");
		ninja.WriteLine ($"  rspfile = $builddir/gen-pinvoke.rsp");
		ninja.WriteLine ($"  rspfile_content = $out $pinvoke_libs $in");
		ninja.WriteLine ("rule ilstrip");
		ninja.WriteLine ($"  command = {commandPrefix} {cpCommand} $in $out; mono $tools_dir/mono-cil-strip.exe -q $out");
		ninja.WriteLine ("  description = [IL-STRIP]");

		// Targets
		ninja.WriteLine ("build $appdir: mkdir");
		ninja.WriteLine ("build $appdir/$deploy_prefix: mkdir");
		ninja.WriteLine ("build $appdir/runtime.js: cpifdiff $builddir/runtime.js");
		ninja.WriteLine ("build $appdir/mono-config.json: cpifdiff $builddir/mono-config.json");
		if (build_wasm) {
			var source_file = Path.GetFullPath(Path.Combine(src_prefix, "driver.c"));
			ninja.WriteLine($"build $builddir/driver.c: cpifdiff {EscapePath(source_file)}");
			ninja.WriteLine($"build $builddir/driver-gen.c: cpifdiff $builddir/driver-gen.c.in");
			source_file = Path.GetFullPath(Path.Combine(src_prefix, "pinvoke.c"));
			ninja.WriteLine($"build $builddir/pinvoke.c: cpifdiff {EscapePath(source_file)}");
			source_file = Path.GetFullPath(Path.Combine(src_prefix, "pinvoke.h"));
			ninja.WriteLine($"build $builddir/pinvoke.h: cpifdiff {EscapePath(source_file)}");

			if (!is_netcore)
			{
				var pinvoke_file_name = is_netcore ? "pinvoke-table.h" : "pinvoke-tables-default.h";
				var pinvoke_file = Path.GetFullPath(Path.Combine(src_prefix, pinvoke_file_name));
				ninja.WriteLine($"build $builddir/pinvoke-tables-default.h: cpifdiff {EscapePath(pinvoke_file)}");
				driver_deps += $" $builddir/pinvoke-tables-default.h";
			}

			var driver_cflags = enable_aot ? "-DENABLE_AOT=1" : "";

			if (add_binding) {
				var bindings_source_file = Path.GetFullPath (Path.Combine (src_prefix, "corebindings.c"));
				ninja.WriteLine ($"build $builddir/corebindings.c: cpifdiff {EscapePath(bindings_source_file)}");

				ninja.WriteLine ($"build $builddir/corebindings.o: emcc $builddir/corebindings.c | $emsdk_env");
				ninja.WriteLine ($"  flags = -I{runtime_dir}/include/mono-2.0");
				driver_cflags += " -DCORE_BINDINGS ";
			}
			if (gen_pinvoke)
				driver_cflags += " -DGEN_PINVOKE ";
			if (is_netcore)
				driver_cflags += " -DENABLE_NETCORE ";

			ninja.WriteLine ("build $emsdk_env: create-emsdk-env");
			ninja.WriteLine ($"build $builddir/driver.o: emcc $builddir/driver.c | $emsdk_env $builddir/driver-gen.c {driver_deps}");
			ninja.WriteLine ($"  flags = {driver_cflags} -DDRIVER_GEN=1 -I{runtime_dir}/include/mono-2.0");
			ninja.WriteLine ($"build $builddir/pinvoke.o: emcc $builddir/pinvoke.c | $emsdk_env {driver_deps}");
			ninja.WriteLine ($"  flags = {driver_cflags} -DDRIVER_GEN=1 -I{runtime_dir}/include/mono-2.0");

			foreach (var nativeCompile in native_compile)
			{
				var fileName = Path.GetFileName(nativeCompile);
				var fileNameWithoutExt = Path.GetFileNameWithoutExtension(nativeCompile);

				ninja.WriteLine($"build $builddir/{fileName}: cpifdiff {EscapePath(source_file)}");
				ninja.WriteLine($"build $builddir/{fileNameWithoutExt}.o: emcc {EscapePath(nativeCompile)} | $emsdk_env {driver_deps}");
				ninja.WriteLine($"  flags = {driver_cflags} -DDRIVER_GEN=1 -I{runtime_dir}/include/mono-2.0");
			}

			if (enable_zlib) {
				var zlib_source_file = Path.GetFullPath (Path.Combine (src_prefix, "zlib-helper.c"));
				ninja.WriteLine ($"build $builddir/zlib-helper.c: cpifdiff {EscapePath(zlib_source_file)}");

				ninja.WriteLine ($"build $builddir/zlib-helper.o: emcc $builddir/zlib-helper.c | $emsdk_env");
				ninja.WriteLine ($"  flags = -s USE_ZLIB=1 -I{runtime_dir}/include/mono-2.0");
			}
		} else {
			ninja.WriteLine ("build $appdir/dotnet.native.js: cpifdiff $wasm_runtime_dir/dotnet.native.js");
			ninja.WriteLine ("build $appdir/dotnet.native.wasm: cpifdiff $wasm_runtime_dir/dotnet.native.wasm");
			if (enable_threads) {
				ninja.WriteLine ("build $appdir/dotnet.native.worker.js: cpifdiff $wasm_runtime_dir/dotnet.native.worker.js");
			}
		}
		if (enable_aot)
			ninja.WriteLine ("build $builddir/aot-in: mkdir");
		{
			var list = new List<string>();

			if (!is_netcore)
			{
				list.Add("linker-preserves.xml");
				list.Add("linker-subs.xml");
				list.Add("linker-disable-collation.xml");
			}

			foreach (var file in list) {
				var source_file = Path.GetFullPath (Path.Combine (src_prefix, file));
				ninja.WriteLine ($"build $builddir/{file}: cpifdiff {EscapePath(source_file)}");
			}
		}
		var ofiles = "";
		var bc_files = "";
		string linker_infiles = "";
		string linker_ofiles = "";
		string linker_ofiles_dedup = "";
		string dedup_infiles = "";
		if (enable_linker) {
			string path = Path.Combine (builddir, "linker-in");
			if (!Directory.Exists (path))
				Directory.CreateDirectory (path);
		}
		string aot_in_path = enable_linker ? Path.Combine("$builddir","linker-out") : "$builddir";
		foreach (var a in assemblies) {
			var assembly = a.src_path;
			if (assembly == null)
				continue;
			string filename = Path.GetFileName (assembly);

			if(a.culture is not null)
			{
				filename = Path.Combine(a.culture, filename);
			}

			var filename_noext = Path.GetFileNameWithoutExtension (filename);
			string filename_pdb = Path.ChangeExtension (filename, "pdb");
			var source_file_path = Path.GetFullPath (assembly);
			var source_file_path_pdb = Path.ChangeExtension (source_file_path, "pdb");
			string infile = "";
			string infile_pdb = "";
			bool emit_pdb = assemblies_with_dbg_info.Contains (source_file_path_pdb);
			if (enable_linker) {
				a.linkin_path = Path.Combine("$builddir", "linker-in", filename);
				a.linkin_pdb_path = Path.Combine("$builddir", "linker-in", filename_pdb);
				a.linkout_path = Path.Combine("$builddir", "linker-out", filename);
				a.linkout_pdb_path = Path.Combine("$builddir", "linker-out", filename_pdb);
				linker_infiles += $"{a.linkin_path} ";
				linker_ofiles += $" {a.linkout_path}";

				if (a.aot) {
					linker_ofiles_dedup += $" {a.linkout_path}";
				}
				ninja.WriteLine ($"build {a.linkin_path}: cp {EscapePath(source_file_path)}");
				if (File.Exists(source_file_path_pdb)) {
					ninja.WriteLine($"build {a.linkin_pdb_path}: cp {EscapePath(source_file_path_pdb)}");
					linker_ofiles += $" {a.linkout_pdb_path}";
					infile_pdb = a.linkout_pdb_path;
				}
				a.aotin_path = a.linkout_path;
				infile = $"{a.aotin_path}";
			} else {
				infile = Path.Combine("$builddir", filename);
				ninja.WriteLine ($"build $builddir/{filename}: cpifdiff {EscapePath(source_file_path)}");
				a.linkout_path = infile;
				if (emit_pdb) {
					ninja.WriteLine ($"build $builddir/{filename_pdb}: cpifdiffex {EscapePath(source_file_path_pdb)}");
					infile_pdb = $"$builddir/{filename_pdb}";
				}
			}

			a.final_path = infile;
			if (il_strip) {
				ninja.WriteLine ($"build $builddir/ilstrip-out/{filename} : ilstrip {infile}");
				a.final_path = $"$builddir/ilstrip-out/{filename}";
			}

			ninja.WriteLine ($"build $appdir/$deploy_prefix/{filename}: cpifdiff {EscapePath(a.final_path)}");
			if (emit_pdb && infile_pdb != "")
				ninja.WriteLine ($"build $appdir/$deploy_prefix/{filename_pdb}: cpifdiffex {EscapePath(infile_pdb)}");
			if (a.aot) {
				a.bc_path = $"$builddir/{filename}.bc";
				a.o_path = $"$builddir/{filename}.o";
				a.aot_depfile_path = $"$builddir/linker-out/{filename}.depfile";

				if (filename == "mscorlib.dll") {
					// mscorlib has no dependencies so we can skip the aot step if the input didn't change
					// The other assemblies depend on their references
					infile = "$builddir/aot-in/mscorlib.dll";
					a.aotin_path = infile;
					ninja.WriteLine ($"build {a.aotin_path}: cpifdiff {EscapePath(a.linkout_path)}");
				}
				ninja.WriteLine ($"build {a.bc_path}.tmp: aot {infile}");
				ninja.WriteLine ($"  src_file={infile}");
				ninja.WriteLine ($"  outfile={a.bc_path}.tmp");
				if (is_windows)
				{
					ninja.WriteLine($"  mono_path={aot_in_path}");
				}
				else
				{
					ninja.WriteLine($"  mono_path=$builddir/aot-in:{aot_in_path}");
				}
				ninja.WriteLine ($"  depfile={a.aot_depfile_path}");
				if (enable_dedup)
					ninja.WriteLine ($"  aot_args=dedup-skip");

				ninja.WriteLine ($"build {a.bc_path}: cpifdiff {EscapePath(a.bc_path)}.tmp");
				ninja.WriteLine ($"build {a.o_path}: emcc {a.bc_path} | $emsdk_env");

				ofiles += " " + $"{a.o_path}";
				bc_files += " " + $"{a.bc_path}";
				dedup_infiles += $" {a.aotin_path}";
			}
		}
		if (enable_dedup) {
			/*
			 * Run the aot compiler in dedup mode:
			 * mono --aot=<args>,dedup-include=aot-instances.dll <assemblies> aot-instances.dll
			 * This will process all assemblies and emit all instances into the aot image of aot-instances.dll
			 */
			var a = dedup_asm;
			/*
			 * The dedup process will read in the .dedup files created when running with dedup-skip, so add all the
			 * .bc files as dependencies.
			 */
			ninja.WriteLine ($"build {a.bc_path}.tmp: aot-instances | {bc_files} {a.linkout_path}");
			ninja.WriteLine ($"  dedup_image={a.filename}");
			ninja.WriteLine ($"  src_files={dedup_infiles} {a.linkout_path}");
			ninja.WriteLine ($"  outfile={a.bc_path}.tmp");

			if (is_windows)
			{
				ninja.WriteLine($"  mono_path={aot_in_path}");
			}
			else
			{
				ninja.WriteLine($"  mono_path=$builddir/aot-in:{aot_in_path}");
			}

			ninja.WriteLine ($"build {a.app_path}: cpifdiff {EscapePath(a.linkout_path)}");
			ninja.WriteLine ($"build {a.linkout_path}: aot-instances-dll");
			// The dedup image might not have changed
			ninja.WriteLine ($"build {a.bc_path}: cpifdiff {EscapePath(a.bc_path)}.tmp");
			ninja.WriteLine ($"build {a.o_path}: emcc {a.bc_path} | $emsdk_env");
			ofiles += $" {a.o_path}";
		}

		ninja.WriteLine ("build $builddir/icall-table.json: gen-runtime-icall-table");

		if (link_icalls) {
			string icall_assemblies = "";
			foreach (var a in assemblies.Where(a => a.culture is null)) {
				if (a.name == "mscorlib" || a.name == "System")
					icall_assemblies += $"{a.linkout_path} ";
			}
			ninja.WriteLine ($"build $builddir/icall-table.h: gen-icall-table {icall_assemblies}");
			ninja.WriteLine ($"  runtime_table=$builddir/icall-table.json");
		}
		if (gen_pinvoke) {
			string pinvoke_assemblies = "";
			foreach (var a in assemblies.Where(a => a.culture is null))
				pinvoke_assemblies += $"{a.linkout_path} ";

			ninja.WriteLine ($"build $builddir/pinvoke-table.h: cpifdiff $builddir/pinvoke-table.h.tmp");
			ninja.WriteLine ($"build $builddir/pinvoke-table.h.tmp: gen-pinvoke-table $builddir/icall-table.json {pinvoke_assemblies}");

			if (is_netcore)
			{
				ninja.WriteLine($"  pinvoke_libs=libSystem.Native,libSystem.IO.Compression.Native,libSystem.Globalization.Native,QCall,{pinvoke_libs}");
			}
			else
			{
				ninja.WriteLine($"  pinvoke_libs=System.Native,{pinvoke_libs}");
			}
		}
		if (build_wasm) {
			string zlibhelper = enable_zlib ? "$builddir/zlib-helper.o" : "";

			var native_compile_params = string.Join("", native_compile.Select(f => $"$builddir/{Path.GetFileNameWithoutExtension(f)}.o"));

			ninja.WriteLine ($"build $appdir/dotnet.native.js $appdir/dotnet.native.wasm: emcc-link $builddir/driver.o $builddir/pinvoke.o {native_compile_params} {zlibhelper} {wasm_core_bindings} {ofiles} {profiler_libs} {extra_link_libs} {runtime_libs} | {EscapePath(src_prefix)}/es6/dotnet.es6.lib.js {wasm_core_support} $emsdk_env");
			ninja.WriteLine ($"  out_wasm=$appdir/dotnet.native.wasm");
			ninja.WriteLine ($"  out_js=$appdir/dotnet.native.js");
		}
		if (enable_linker) {
			switch (linkMode) {
			case LinkMode.SdkOnly:
				usermode = "copy";
				break;
			case LinkMode.All:
				usermode = "link";
				break;
			default:
				usermode = "link";
				break;
			}

			// Removed because of https://github.com/dotnet/runtime/issues/65325
			//if (enable_aot)
			//	// Only used by the AOT compiler
			//	linker_args.Add("--explicit-reflection ");

			// disabled to align with the .NET SDK default behavior (https://github.com/dotnet/runtime/issues/90745)
			// linker_args.Add("--used-attrs-only true ");

			if (!is_netcore)
			{
				linker_args.Add("--substitutions linker-subs.xml ");
				linker_infiles += "| linker-subs.xml ";
				linker_args.Add("-x linker-preserves.xml ");
				linker_infiles += "linker-preserves.xml ";
			}

			if (opts.LinkerExcludeDeserialization && !is_netcore)
				linker_args.Add("--exclude-feature deserialization ");

			if (!opts.EnableCollation && !is_netcore) {
				linker_args.Add("--substitutions linker-disable-collation.xml ");
				linker_infiles += "linker-disable-collation.xml";
			}
			if (opts.Debug) {
				linker_args.Add("-b true ");
			}
			if (!string.IsNullOrEmpty (linkDescriptor)) {
				linker_args.Add($"-x {linkDescriptor} ");
				foreach (var assembly in root_assemblies) {
					string filename = Path.GetFileName (assembly);
					linker_args.Add($"-p {usermode} {filename} -r linker-in/{filename} ");
				}
			} else {
				foreach (var assembly in root_assemblies) {
					string filename = Path.GetFileName (assembly);
					linker_args.Add($"-a linker-in/{filename} {(IsSupportAssembly(filename) ? string.Empty : "entrypoint")} ");
				}
			}

			if (linker_verbose) {
				linker_args.Add("--verbose ");
			}
			linker_args.Add($"-d linker-in -d {bcl_prefix} -d {bcl_facades_prefix} -d {bcl_facades_prefix} ");

			// Metadata linking https://github.com/mono/linker/commit/fafb6cf6a385a8c753faa174b9ab7c3600a9d494
			linker_args.Add($"--keep-metadata all ");

			linker_args.Add($" --verbose ");

			ninja.WriteLine ("build $builddir/linker-out: mkdir");
			ninja.WriteLine ($"build {linker_ofiles}: linker {linker_infiles}");

			File.WriteAllLines(linkerResponse, linker_args);
		}
		if (il_strip)
			ninja.WriteLine ("build $builddir/ilstrip-out: mkdir");

		foreach(var asset in assets) {
			var filename = Path.GetFileName (asset);
			var abs_path = Path.GetFullPath (asset);
			ninja.WriteLine ($"build $appdir/{filename}: cpifdiff {abs_path}");
		}

		ninja.Close ();

		return 0;
	}

	private bool IsResourceAssembly(string f, out string culture)
	{
		if (f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
		{
			var originalAssembly = Path.GetFileName(f.Replace(".resources.dll", ".dll", StringComparison.OrdinalIgnoreCase));

			var resourceAssemblyDirectory = Path.GetDirectoryName(Path.GetDirectoryName(f));
			if (File.Exists(Path.Combine(resourceAssemblyDirectory, originalAssembly)))
			{
				culture = Path.GetFileName(Path.GetDirectoryName(f));

				return true;
			}
		}

		culture = null;
		return false;
	}

	private static bool IsSupportAssembly(string filename)
		=>
			filename switch
			{
				"Uno.Wasm.AotProfiler.dll" => true,
				"Uno.Wasm.LogProfiler.dll" => true,
				"Uno.Wasm.MetadataUpdater.dll" => true,
				_ => false
			};

	static void CopyFile(string sourceFileName, string destFileName, CopyType copyType, string typeFile = "")
	{
		Console.WriteLine($"{typeFile}cp: {copyType} - {sourceFileName} -> {destFileName}");

		Directory.CreateDirectory(Path.GetDirectoryName(destFileName));

		switch (copyType)
		{
			case CopyType.Always:
				File.Copy(sourceFileName, destFileName, true);
				break;
			case CopyType.IfNewer:
				if (!File.Exists(destFileName))
				{
					File.Copy(sourceFileName, destFileName);
				}
				else
				{
					var srcInfo = new FileInfo (sourceFileName);
					var dstInfo = new FileInfo (destFileName);

					if (srcInfo.LastWriteTime.Ticks > dstInfo.LastWriteTime.Ticks || srcInfo.Length > dstInfo.Length)
						File.Copy(sourceFileName, destFileName, true);
					else
						Console.WriteLine($"    skipping: {sourceFileName}");
				}
				break;
			default:
				File.Copy(sourceFileName, destFileName);
				break;
		}

	}

	private string EscapePath(string path)
		=> path.Replace(" ", "$ ").Replace(":", "$:");
}
