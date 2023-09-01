// ******************************************************************
// Copyright � 2015-2018 nventive inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// ******************************************************************
using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace Uno.Wasm.Sample
{ 
    public static class Program
    {
		private static Timer _t;
		private static Uno.Wasm.Sample.Library.ClassLibrary01 _unused;

		static void Main(string[] args)  
		{
			Console.WriteLine($"RuntimeVersion: {RuntimeInformation.FrameworkDescription}");
			Console.WriteLine($"Mono Runtime Mode: " + Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_MODE"));
			Console.WriteLine($"args: " + args.Length);

			var i = 42; 
			var now = DateTime.Now.ToString();
			Console.WriteLine($"Main! {i} {now}");

#if NET7_0_OR_GREATER
			Imports.TestCallback();
#else
			Interop.Runtime.InvokeJS($"testCallback()", out var _);
#endif

			var idbFSEnabled = Interop.Runtime.InvokeJS($"typeof IDBFS !== 'undefined'", out var _);
			Console.WriteLine($"IDBFS: {idbFSEnabled}");

			var requireAvailable = Interop.Runtime.InvokeJS($"typeof require.config !== 'undefined'", out var _);
			Console.WriteLine($"requireJSAvailable: {requireAvailable}");

			Console.WriteLine($"Timezone: {TimeZoneInfo.Local.StandardName}");

			Console.WriteLine(typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger));

			var r = new System.Resources.ResourceManager("FxResources.System.Web.Services.Description.SR", typeof(System.Web.Services.Description.Binding).Assembly);
			Console.WriteLine($"Res(en): {r.GetString("WebDescriptionMissing", new CultureInfo("en-US"))}");
			Console.WriteLine($"Res(fr): {r.GetString("WebDescriptionMissing", new CultureInfo("fr-CA"))}");

			_t = new Timer(_ => {
				Console.WriteLine("message");
			}, null, 5000, 5000);
		}
	}

#if NET7_0_OR_GREATER
	public static partial class Imports
	{
		[System.Runtime.InteropServices.JavaScript.JSImport("globalThis.testCallback")]
		public static partial void TestCallback();
	}
#endif

	public static partial class Exports
	{
#if NET7_0_OR_GREATER
#pragma warning disable IDE0022 // Use expression body for methods (Will be fixed in net7 in RC2)
		[System.Runtime.InteropServices.JavaScript.JSExport()]
		public static void MyExportedMethod1()
		{
			Console.WriteLine($"Exported method invoked 1");
		}
#pragma warning restore IDE0022 // Use expression body for methods
#endif
		public static void MyExportedMethod2()
			=> Console.WriteLine($"Exported method invoked 2");
	}
}
