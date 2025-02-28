// ******************************************************************
// Copyright � 2015-2022 Uno Platform inc. All rights reserved.
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
using System.Collections.Generic;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WebAssembly;

namespace Uno.Wasm.Sample
{ 
    public static class Program
    {
		private static ManualResetEvent _event = new ManualResetEvent(false);
		private static object _gate = new object();
		private static List<string> _messages = new List<string>();
		private static bool _mainThreadInvoked = false;
		private static Timer _timer;
		private static SynchronizationContext _jsContext = SynchronizationContext.Current;

		static void Main()
		{
			var runtimeMode = Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_MONO_RUNTIME_MODE");
			Console.WriteLine($"Runtime Version: " + RuntimeInformation.FrameworkDescription);
			Console.WriteLine($"Runtime Mode: " + runtimeMode);
			Console.WriteLine($"TID: {Thread.CurrentThread.ManagedThreadId}");
			Console.WriteLine($"SynchronizationContext: {_jsContext}");

			Runtime.InvokeJS("Interop.appendResult('Startup')");

			Run();

			_timer = new Timer(OnTick);
			_timer.Change(TimeSpan.FromSeconds(.1), TimeSpan.FromSeconds(.1));
		}

		private static void OnTick(object state)
		{
			if (_event.WaitOne(10) && _mainThreadInvoked)
			{
				var r = $"Done {_messages.Count} results (_mainThreadInvoked:{_mainThreadInvoked})";
				Runtime.InvokeJS($"Interop.appendResult('{r}')");
				Console.WriteLine($"[tid:{Thread.CurrentThread.ManagedThreadId}]: {r}");
				_timer.Dispose();
			}
			else
			{
				Console.WriteLine($"Working... (_mainThreadInvoked: {_mainThreadInvoked})");
			}
		}

		public static void MainThreadCallback()
		{
			Console.WriteLine($"[tid:{Thread.CurrentThread.ManagedThreadId}]: MainThreadCallback");
			_mainThreadInvoked = true;
		}

		private static async Task Run()
		{
			int counter = 0;
			var tcs = new TaskCompletionSource<bool>();

			void logMessage(string message)
			{
				int index = Interlocked.Increment(ref counter);
				lock (_gate)
				{
					_messages.Add($"[tid:{Thread.CurrentThread.ManagedThreadId}][ctx:{SynchronizationContext.Current?.ToString()}]: {index} / {message}");
				}
			}

			void DoWork(string name)
			{
				for (int i = 0; i < 10000; i++) {
					logMessage($"{name}: {i}");

					if ((i % 2000) == 0)
					{
						Console.WriteLine("InvokeOnMainThread1");
						_jsContext.Post(_ => MainThreadCallback(), null);
					}
				}
			}

#pragma warning disable CA1416 // Validate platform compatibility
			new Thread(_ =>
			{
				Console.WriteLine($"Starting thread [tid:{Thread.CurrentThread.ManagedThreadId}] [ctx:{SynchronizationContext.Current?.ToString()}]");

				_jsContext.Post(_ => MainThreadCallback(), null);
				Console.WriteLine("InvokeOnMainThread2");

				DoWork("thread1");
				tcs.TrySetResult(true);
				Console.WriteLine($"Stopping thread [tid:{Thread.CurrentThread.ManagedThreadId}]");
			}).Start();
#pragma warning restore CA1416 // Validate platform compatibility

			await tcs.Task;

			_event.Set();
		}
	}
}
