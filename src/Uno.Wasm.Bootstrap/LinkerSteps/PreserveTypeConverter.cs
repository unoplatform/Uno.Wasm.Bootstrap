//******************************************************************
// This file is part of the Xamarin.Android SDK

// The MIT License(MIT)

// Copyright(c) .NET Foundation Contributors
   
// All rights reserved.
   
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
   
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
   
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
//******************************************************************
using System;
using System.Linq;
using Mono.Linker;
using Mono.Cecil;
using Mono.Linker.Steps;

namespace Uno.Wasm.Bootstrap
{
	public class PreserveTypeConverters : ResolveStep
	{
		protected override void Process()
		{
			var asms = Context.GetAssemblies();
			foreach (var a in asms)
			{
				foreach (var m in a.Modules)
				{
					foreach (var t in m.Types.Where(IsTypeConverter))
					{
						PreserveTypeConverter(t);
					}
				}
			}
		}

		void PreserveTypeConverter(TypeDefinition type)
		{
			if (!type.HasMethods)
			{
				return;
			}

			foreach (var ctor in type.Methods.Where(m => m.IsConstructor))
			{
				// We only care about ctors with 0 or 1 params.
				if (ctor.HasParameters && ctor.Parameters.Count > 1)
				{
					continue;
				}

				Annotations.AddPreservedMethod(type, ctor);
			}
		}

		static bool IsTypeConverter(TypeDefinition type)
		{
			return type.Inherits("System.ComponentModel", "TypeConverter");
		}
	}
}