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

namespace Uno.Wasm.Sample
{ 
    public static class Program
    {
		private static Timer _t;

		static void Main(string[] args)  
		{
			Console.WriteLine($"RuntimeVersion: {RuntimeInformation.FrameworkDescription}");
			Console.WriteLine($"Mono Runtime Mode: " + Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_MODE"));
			Console.WriteLine($"args: " + args.Length);

			var i = 42; 
			var now = DateTime.Now.ToString();
			Console.WriteLine($"Main! {i} {now}");

			var idbFSEnabled = Interop.Runtime.InvokeJS($"typeof IDBFS !== 'undefined'", out var _);
			Console.WriteLine($"IDBFS: {idbFSEnabled}");

			var requireAvailable = Interop.Runtime.InvokeJS($"typeof require.config !== 'undefined'", out var _);
			Console.WriteLine($"requireJSAvailable: {requireAvailable}");

			Console.WriteLine($"Timezone: {TimeZoneInfo.Local.StandardName}");

			Console.WriteLine(typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger));

			_t = new Timer(_ => {
				Console.WriteLine("message");
			}, null, 5000, 5000);
		}
	}
}
