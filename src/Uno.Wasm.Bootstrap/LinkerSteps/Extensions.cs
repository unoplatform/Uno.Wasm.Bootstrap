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
using System.Text;
using Mono.Cecil;

namespace Uno.Wasm.Bootstrap
{
    internal static class Extensions
	{
		public static bool Inherits(this TypeReference self, string className)
		{
			if (className == null)
			{
				throw new ArgumentNullException("className");
			}

			if (self == null)
			{
				return false;
			}

			TypeReference current = self.Resolve();

			while (current != null)
			{
				string fullname = current.FullName;

				if (fullname == className)
				{
					return true;
				}

				if (fullname == "System.Object")
				{
					return false;
				}

				if (current.Resolve() == null)
				{
					return false;
				}

				current = current.Resolve()?.BaseType;
			}

			return false;
		}

		public static bool Is(this TypeReference type, string @namespace, string name) 
			=> type != null && type.Name == name && type.Namespace == @namespace;

		public static bool Inherits(this TypeReference self, string @namespace, string name)
		{
			if (@namespace == null)
			{
				throw new ArgumentNullException("namespace");
			}

			if (name == null)
			{
				throw new ArgumentNullException("name");
			}

			if (self == null)
			{
				return false;
			}

			TypeDefinition typeDefinition;

			for (TypeReference typeReference = self.Resolve(); typeReference != null; typeReference = typeDefinition.BaseType)
			{
				if (typeReference.Is(@namespace, name))
				{
					return true;
				}
				if (typeReference.Is("System", "Object"))
				{
					return false;
				}
				typeDefinition = typeReference.Resolve();
				if (typeDefinition == null)
				{
					return false;
				}
			}
			return false;
		}

	}
}
