using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Transactions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Profiler.Aot;

namespace Uno.Wasm.Bootstrap
{
    public class AOTProfileIssuesTask_v0 : Task
	{
		[Required]
		public string AotProfile { get; set; } = "";

		[Required]
		public ITaskItem[] ILTrimmedAssemblies { get; set; } = [];

		public ITaskItem[] ExcludedMethods { get; set; } = [];

		public ITaskItem[] ExcludedAssemblies { get; set; } = [];

		[Required]
		public bool FailOnErrors { get; set; }

		public bool ShowDebug { get; set; }

		public override bool Execute()
		{
			var reader = new Mono.Profiler.Aot.ProfileReader();
			using FileStream stream = File.OpenRead(AotProfile);

			var methodFilters = ExcludedMethods
				.Select(m => new Regex(m.ItemSpec, RegexOptions.IgnoreCase | RegexOptions.Compiled))
				.ToArray();

			var profile = reader.ReadAllData(stream);

			foreach (var asm in ILTrimmedAssemblies)
			{
				if (ExcludedAssemblies.Any(a => Regex.IsMatch(asm.GetMetadata("FileName"), a.ItemSpec, RegexOptions.IgnoreCase)))
				{
					LogDebug($"Skipping filtered assembly {asm.ItemSpec})");
					continue;
				}

				var assembly = AssemblyDefinition.ReadAssembly(asm.ItemSpec);

				LogDebug($"Processing assembly {assembly.FullName})");

				var asmTypes = assembly.MainModule.Types.ToDictionary(t => t.FullName, t => t);
				var profileTypes= profile.Methods.GroupBy(m => m.Type.FullName);

				foreach(var profileType in profileTypes)
				{
					if (!asmTypes.TryGetValue(profileType.Key, out var asmType))
					{
						continue;
					}

					Dictionary<string, List<MethodDefinition>> typeMethods = new();

					foreach(var method in asmType.Methods)
					{
						if (!typeMethods.TryGetValue(method.Name, out var methods))
						{
							methods = new List<MethodDefinition>();
							typeMethods[method.Name] = methods;
						}
						methods.Add(method);
					}

					foreach (var profileMethod in profileType)
					{
						if (profileMethod.Type.Module.Name != assembly.Name.Name)
						{
							continue;
						}

						if (methodFilters.Any(f => f.IsMatch(profileMethod.Name)))
						{
							LogDebug($"Skipping filtered method {profileMethod.Type}.{profileMethod.Name})");
							continue;
						}

						if (profileMethod.GenericInst is not null)
						{
							continue;
						}

						if (typeMethods.TryGetValue(profileMethod.Name, out var methods))
						{
							LogDebug($"Processing profile method {profileMethod.Type}.{profileMethod.Name} ({profileMethod.Signature}, generic: {profileMethod.GenericInst?.Id ?? -1})");

							var found = false;
							var genericMethodsCount = 0;
							foreach (var method in methods)
							{
								if(method.HasGenericParameters)
								{
									genericMethodsCount++;
									continue;
								}

								var s = GetProfileMethodCompatibleSignature(method);

								LogDebug($"Try matching method {s} with {profileMethod.Signature}");

								if (s == profileMethod.Signature)
								{
									found = true;
									if (!IsEmptyBody(method.Body))
									{
										LogError("UNOW0001", $"Method {method.FullName} from {asm.GetMetadata("FileName")} has not been AOTed, even if present in the AOT profile.");
									}
									else
									{
										LogDebug($"The method {method.FullName} from {asm.GetMetadata("FileName")} was AOTed properly");
									}
								}
							}

							if (!found)
							{
								if (genericMethodsCount != methods.Count)
								{
									LogError("UNOW0002", $"The method {profileMethod.Type}.{profileMethod.Name} ({profileMethod.Signature}) from {asm.GetMetadata("FileName")} is not present in the assembly.");
								}
								else
								{
									LogDebug($"Skipped all generic methods for {profileMethod.Name} in {assembly.FullName}");
								}
							}
						}
						else
						{
							LogDebug($"Method {profileMethod.Name} cannot be found in {assembly.FullName} assembly");
						}
					}
				}
			}

			return true;
		}

		private bool IsEmptyBody(MethodBody body)
		{
			if (body.Instructions.Count == 1)
			{
				// Some methods are empty, but have a single `ret` instruction that optimized away by the AOT compiler.
				var instruction = body.Instructions[0];
				if (instruction.OpCode == OpCodes.Ret)
				{
					return true;
				}
			}

			// check if only invokes the default object class ctor
			if (body.Instructions.Count == 3)
			{
				var instruction = body.Instructions[0];
				if (instruction.OpCode == OpCodes.Ldarg_0)
				{
					instruction = body.Instructions[1];
					if (instruction.OpCode == OpCodes.Call)
					{
						var method = (MethodReference)instruction.Operand;
						if (method.DeclaringType.FullName == "System.Object" && method.Name == ".ctor")
						{
							instruction = body.Instructions[2];
							if (instruction.OpCode == OpCodes.Ret)
							{
								return true;
							}
						}
					}
				}
			}

			return body.CodeSize == 0;
		}

		private string GetProfileMethodCompatibleSignature(MethodDefinition method)
		{
			string GetLiteralType(TypeReference type)
			{
				var originalType = type;
				var typeName = type.FullName;

				if (type is RequiredModifierType modType)
				{
					type = modType.ElementType;
					typeName = type.FullName;
				}

				if (type is ByReferenceType refType)
				{
					type = refType.ElementType;
					typeName = type.FullName;
				}

				if (type is PointerType ptrType)
				{
					type = ptrType.ElementType;
					typeName = type.FullName;
				}

				if (type.IsGenericParameter)
				{
					typeName = type.Name;
				}
				else if (type.IsArray)
				{
					typeName = GetLiteralType(((ArrayType)type).ElementType) + "[]";
				}
				else if (type.IsGenericInstance)
				{
					var gi = (GenericInstanceType)type;
					var index = type.FullName.IndexOf('`');

					typeName = type.FullName.Substring(0, index + 2) + "<" + string.Join(", ", gi.GenericArguments.Select(GetLiteralType)) + ">";
				}
				else
				{
					typeName = type.FullName.TrimEnd('&','*') switch
					{
						"System.Void" => "void",
						"System.Int32" => "int",
						"System.Int64" => "long",
						"System.Single" => "single",
						"System.Double" => "double",
						"System.Boolean" => "bool",
						"System.String" => "string",
						"System.Byte" => "byte",
						"System.Char" => "char",
						"System.UInt32" => "uint",
						"System.UInt64" => "ulong",
						"System.SByte" => "sbyte",
						"System.UInt16" => "uint16",
						"System.Int16" => "int16",
						"System.Object" => "object",
						"System.IntPtr" => "intptr",
						"System.UIntPtr" => "uintptr",
						_ => type.FullName,
					};
				}

				string RestorePtrAndRefs(TypeReference type)
				{
					if (type is RequiredModifierType modType)
					{
						return $" modreq({modType.ModifierType.FullName})" + RestorePtrAndRefs(modType.ElementType);
					}
					if (type is ByReferenceType byRef)
					{
						return RestorePtrAndRefs(byRef.ElementType) + "&";
					}
					else if (type is PointerType ptrRef)
					{
						return RestorePtrAndRefs(ptrRef.ElementType) + "*";
					}
					else
					{
						return "";
					}
				}

				if(typeName != originalType.FullName)
				{
					typeName += RestorePtrAndRefs(originalType);
				}

				return typeName;
			}


			var sb = new StringBuilder();
			sb.Append(GetLiteralType(method.ReturnType));
			sb.Append("(");
			foreach (var p in method.Parameters)
			{
				sb.Append(GetLiteralType(p.ParameterType));
				sb.Append(",");
			}
			if (method.Parameters.Count > 0)
			{
				sb.Length--;
			}
			sb.Append(")");
			return sb.ToString();
		}

		private void LogDebug(string message)
		{
			if (ShowDebug)
			{
				Log.LogMessage(MessageImportance.Low, message);
			}
		}

		private void LogError(string code, string message)
		{
			if (FailOnErrors)
			{
				Log.LogMessage(MessageImportance.Low, message);
				Log.LogError(null, code, "", null, 0, 0, 0, 0, message);
			}
			else
			{
				Log.LogMessage(MessageImportance.Low, message);
				Log.LogWarning(null, code, "", null, 0, 0, 0, 0, message);
			}
		}
	}
}
