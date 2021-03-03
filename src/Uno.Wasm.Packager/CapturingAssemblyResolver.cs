using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Mono.Cecil;

/// <summary>
/// An IAssemblyResolver which assumes the files list will not change, to perform faster lookups.
/// </summary>
class CapturingAssemblyResolver : DefaultAssemblyResolver
{
	private List<Dictionary<string, string>> _files;

	protected override AssemblyDefinition SearchDirectory(AssemblyNameReference name, IEnumerable<string> directories, ReaderParameters parameters)
	{
		if (_files == null)
		{
			// Create a cache of the files found in search directories to avoid
			// paying the cost of File.Exists

			_files = new List<Dictionary<string, string>>();
			string[] extensions = new[] { ".winmd", ".dll", ".exe", ".dll" };

			foreach (var directory in directories.Where(Directory.Exists))
			{
				var map = new Dictionary<string, string>();

				foreach (var file in Directory.GetFiles(directory))
				{
					if (extensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
					{
						if (Path.GetFileNameWithoutExtension(file) is { } asmName)
						{
							if (!map.ContainsKey(asmName))
							{
								map.Add(asmName, file);
							}
						}
					}
				}

				_files.Add(map);
			}
		}

		foreach (var directory in _files)
		{
			if (directory.TryGetValue(name.Name, out var filePath))
			{
				try
				{
					return GetAssembly(filePath, parameters);
				}
				catch (System.BadImageFormatException)
				{
					continue;
				}
			}
		}

		return null;
	}

	AssemblyDefinition GetAssembly(string file, ReaderParameters parameters)
	{
		if (parameters.AssemblyResolver == null)
		{
			parameters.AssemblyResolver = this;
		}

		return ModuleDefinition.ReadModule(file, parameters).Assembly;
	}
}
