// ******************************************************************
// Copyright © 2015-2026 Uno Platform inc. All rights reserved.
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.Wasm.Bootstrap.AOTWrapperSeeding;

namespace Uno.Wasm.Bootstrap.UnitTests;

[TestClass]
public class Given_ValidateAOTWrapperSeedsTask
{
	private static string _directory = "";
	private static string _expectedWrappers = "";

	[ClassInitialize]
	public static void Initialize(TestContext context)
	{
		_directory = Path.Combine(Path.GetTempPath(), "uno-aot-wrapper-check-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);

		_expectedWrappers = Path.Combine(_directory, "expected.txt");
		File.WriteAllText(_expectedWrappers, "");
	}

	[ClassCleanup]
	public static void Cleanup()
	{
		try
		{
			Directory.Delete(_directory, true);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
		}
	}

	// The dotnet host that runs the tests, a tool that exists on every machine they run on.
	private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";

	private static ValidateAOTWrapperSeedsTask CreateTask(RecordingBuildEngine engine, string llvmNm) => new()
	{
		BuildEngine = engine,
		ExpectedWrappers = _expectedWrappers,
		BitcodeFile = Path.Combine(_directory, "aot-instances.dll.bc"),
		LlvmNm = llvmNm,
		DocumentationUrl = "https://docs.example",
		IssuesUrl = "https://issues.example",
	};

	[TestMethod]
	public void When_LlvmNm_Is_Missing_Then_UNOWASM007_Is_Logged()
	{
		var engine = new RecordingBuildEngine();
		var task = CreateTask(engine, Path.Combine(_directory, "missing", "llvm-nm.exe"));

		Assert.IsFalse(task.Execute());
		Assert.AreEqual("UNOWASM007", engine.Errors.Single().Code);
		StringAssert.Contains(engine.Errors.Single().Message, "couldn't start");
		StringAssert.Contains(engine.Errors.Single().Message, "Please report this error at https://issues.example.");
	}

	// dotnet --defined-only <file> fails, and writes why to its standard error, in the language of the SDK.
	[TestMethod]
	public void When_LlvmNm_Fails_Then_UNOWASM007_Has_Its_Error_Output()
	{
		var engine = new RecordingBuildEngine();
		var task = CreateTask(engine, Dotnet);

		Assert.IsFalse(task.Execute());
		Assert.AreEqual("UNOWASM007", engine.Errors.Single().Code);
		StringAssert.Matches(engine.Errors.Single().Message, new Regex(Regex.Escape($"reading {Path.GetFullPath(task.BitcodeFile)}: ") + @"\S"));
	}

	[TestMethod]
	public void When_Canceled_Then_It_Stops_Without_An_Error()
	{
		var engine = new RecordingBuildEngine();
		var task = CreateTask(engine, Dotnet);
		task.Cancel();

		Assert.IsFalse(task.Execute());
		Assert.AreEqual(0, engine.Errors.Count);
	}

	// A tool that lists no function: every expected wrapper is missing, and those past the first 50 are only counted.
	[TestMethod]
	public void When_More_Than_50_Wrappers_Are_Missing_Then_The_Others_Are_Counted()
	{
		string expectedWrappers = Path.Combine(_directory, "many.txt");
		File.WriteAllLines(expectedWrappers, Enumerable.Range(0, 60).Select(i => $"gsharedvt_in_sig_void_i{i}\tSample::Method{i} [Sample]"));

		var engine = new RecordingBuildEngine();
		var task = CreateTask(engine, WriteSilentTool());
		task.ExpectedWrappers = expectedWrappers;

		Assert.IsFalse(task.Execute());
		Assert.AreEqual(51, engine.Errors.Count(e => e.Code == "UNOWASM006"), string.Join(Environment.NewLine, engine.Errors.Select(e => e.Message)));
		StringAssert.Contains(engine.Errors[49].Message, "Sample::Method49 [Sample] needs, so the app may crash when it reaches that method. Please report "
			+ "this error at https://issues.example.");
		StringAssert.Contains(engine.Errors[50].Message, "lacks the code of 10 more methods, which aren't listed.");
	}

	// A script that exits without output, in a single process.
	private static string WriteSilentTool()
	{
		if (OperatingSystem.IsWindows())
		{
			string script = Path.Combine(_directory, "silent.cmd");
			File.WriteAllText(script, "@exit /b 0\r\n");
			return script;
		}

		string shellScript = Path.Combine(_directory, "silent.sh");
		File.WriteAllText(shellScript, "#!/bin/sh\nexit 0\n");
		File.SetUnixFileMode(shellScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		return shellScript;
	}

	[TestMethod]
	public void When_The_Expected_Wrappers_Cannot_Be_Read_Then_UNOWASM007_Is_Logged()
	{
		var engine = new RecordingBuildEngine();
		var task = CreateTask(engine, Dotnet);
		task.ExpectedWrappers = Path.Combine(_directory, "missing", "expected.txt");

		Assert.IsFalse(task.Execute());
		Assert.AreEqual("UNOWASM007", engine.Errors.Single().Code);
		StringAssert.Contains(engine.Errors.Single().Message, "couldn't read " + task.ExpectedWrappers);
	}

	[TestMethod]
	public void When_A_Tool_Runs_Longer_Than_The_Timeout_Then_It_Is_Stopped()
	{
		var run = ToolRunner.Run(Dotnet, "--info", _ => { }, TimeSpan.FromMilliseconds(1), default);

		Assert.IsTrue(run.TimedOut);
	}

	// A tool that would run for a minute, in a single process, stopped from another thread.
	[TestMethod]
	public void When_Canceled_Then_A_Running_Tool_Is_Stopped()
	{
		var (tool, arguments) = OperatingSystem.IsWindows() ? ("powershell.exe", "-NoProfile -Command Start-Sleep 60") : ("sleep", "60");
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
		var stopwatch = Stopwatch.StartNew();

		var run = ToolRunner.Run(tool, arguments, _ => { }, TimeSpan.FromMinutes(5), cancellation.Token);

		Assert.IsNull(run.StartFailure);
		Assert.IsFalse(run.TimedOut);
		Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"The tool was stopped after {stopwatch.Elapsed}.");
	}

	[TestMethod]
	public void When_A_Tool_Runs_Then_Each_Output_Line_Is_Passed_On()
	{
		int lines = 0;
		var run = ToolRunner.Run(Dotnet, "--version", _ => lines++, TimeSpan.FromMinutes(1), default);

		Assert.AreEqual(0, run.ExitCode);
		Assert.IsTrue(lines > 0);
	}
}
