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
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Linker;
using Mono.Linker.Steps;

namespace Uno.Wasm.Bootstrap
{
	internal class PreserveUsingAttributesStep : ResolveStep
	{
		readonly HashSet<string> ignoreAsmNames;

		public PreserveUsingAttributesStep(IEnumerable<string> ignoreAsmNames)
		{
			this.ignoreAsmNames = new HashSet<string>(ignoreAsmNames.Where(n => n != "mscorlib"));
		}

		protected override void Process()
		{
			var asms = Context.GetAssemblies();
			foreach (var a in asms.Where(x => !ignoreAsmNames.Contains(x.Name.Name)))
			{
				foreach (var m in a.Modules)
				{
					foreach (var t in m.Types)
					{
						PreserveTypeIfRequested(t);
					}
				}
			}
		}

		void PreserveTypeIfRequested(TypeDefinition type)
		{
			var typePreserved = IsTypePreserved(type);
			if (IsTypePreserved(type))
			{
				MarkAndPreserveAll(type);
			}
			else
			{
				foreach (var m in type.Methods.Where(IsMethodPreserved))
				{
					Annotations.AddPreservedMethod(type, m);
				}
				foreach (var t in type.NestedTypes)
				{
					PreserveTypeIfRequested(t);
				}
			}
		}

		static bool IsTypePreserved(TypeDefinition typeDefinition)
		{
			// Exclude WasmRuntime to that timer can get called properly
			// https://github.com/mono/mono/blob/a49aa771c10889c6ac1974af81f9fcc375d391cb/mcs/class/corlib/System.Threading/Timer.cs#L56
			if (typeDefinition.Name.EndsWith("WasmRuntime"))
			{
				return true;
			}

			// System.String, otherwise "cannot find CreateString for .ctor" happens.
			// The linker shows a reflection dependency on some String.CreateString
			// method. Workaround for now is to link the whole System.String type.
			if (typeDefinition.FullName.EndsWith("System.String"))
			{
				return true;
			}

			return typeDefinition.CustomAttributes.FirstOrDefault(x => x.AttributeType.Name.StartsWith("Preserve", StringComparison.Ordinal)) != null;
		}

		static bool IsMethodPreserved(MethodDefinition m)
		{
			return m.CustomAttributes.FirstOrDefault(x => x.AttributeType.Name.StartsWith("Preserve", StringComparison.Ordinal)) != null;
		}

		void MarkAndPreserveAll(TypeDefinition type)
		{
			Annotations.MarkAndPush(type);
			Annotations.SetPreserve(type, TypePreserve.All);
			if (!type.HasNestedTypes)
			{
				Tracer.Pop();
				return;
			}
			foreach (TypeDefinition nested in type.NestedTypes)
			{
				MarkAndPreserveAll(nested);
			}

			Tracer.Pop();
		}
	}
}
