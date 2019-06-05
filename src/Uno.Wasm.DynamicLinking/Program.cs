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
using System.IO;
using System.Runtime.InteropServices;
using WebAssembly;

namespace Uno.Wasm.Sample
{ 
    public static class Program
    {
		[DllImport("side")]
		private static extern int test_add(int a, int b);
		[DllImport("side", EntryPoint = "test_add_float")]
		private static extern float test_add_float1(float a, float b);
		[DllImport("side")]
		private static extern double test_add_double(double a, double b);
		[DllImport("side")]
		private static extern int test_exception();

		static void Main(string[] args)
		{
			Console.WriteLine($"Mono Runtime Mode: " + Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_MODE"));

			Console.WriteLine($"test_add:{test_add(21, 21)}");
			Console.WriteLine($"test_float:{test_add_float1(21, 21)}");
			Console.WriteLine($"test_add_double:{test_add_double(21, 21)}");

			var res = $"{test_add(21, 21)};{test_add_float1(21.1f, 21.2f)};{test_add_double(21.3, 21.4)};e{test_exception()}";

			var r = Runtime.InvokeJS($"Interop.appendResult(\"{res}\")", out var result);
		}
	}
}
