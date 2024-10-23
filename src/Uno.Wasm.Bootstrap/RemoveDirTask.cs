﻿// ******************************************************************
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
// 
// This file is based on the work from https://github.com/praeclarum/Ooui
// 
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Uno.Wasm.Bootstrap.Extensions;

namespace Uno.Wasm.Bootstrap;

public class RemoveDirTask_v0 : Microsoft.Build.Utilities.Task
{
	[Required]
	public string Path { get; private set; } = "";

	public override bool Execute()
	{
		var fixedPath = PathHelper.FixupPath(Path);
		PathHelper.DeleteDirectory(fixedPath);

		return true;
	}
}
