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

using System.Collections;
using System.Collections.Generic;
using Microsoft.Build.Framework;

namespace Uno.Wasm.Bootstrap.UnitTests;

/// <summary>
/// A build engine that records what tasks log, to run them in tests.
/// </summary>
internal sealed class RecordingBuildEngine : IBuildEngine
{
	public List<BuildErrorEventArgs> Errors { get; } = [];

	public List<BuildWarningEventArgs> Warnings { get; } = [];

	public List<BuildMessageEventArgs> Messages { get; } = [];

	public bool ContinueOnError => false;

	public int LineNumberOfTaskNode => 0;

	public int ColumnNumberOfTaskNode => 0;

	public string ProjectFileOfTaskNode => "";

	public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;

	public void LogCustomEvent(CustomBuildEventArgs e)
	{
	}

	public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

	public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);

	public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);
}
