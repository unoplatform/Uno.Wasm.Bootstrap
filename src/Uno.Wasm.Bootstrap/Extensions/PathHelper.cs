using System;
using System.IO;

namespace Uno.Wasm.Bootstrap.Extensions;

internal class PathHelper
{
	private static readonly char OtherDirectorySeparatorChar
		= Path.DirectorySeparatorChar == '/' ? '\\' : '/';

	/// <summary>
	/// Align paths to fix issues with mixed path
	/// </summary>
	public static string FixupPath(string path)
		=> path.Replace(OtherDirectorySeparatorChar, Path.DirectorySeparatorChar);

	/// <summary>
	/// Recursively deletes a path, including files with the readonly attribute
	/// </summary>
	public static void DeleteDirectory(string path)
	{
		if (Directory.Exists(path))
		{
			if (Environment.OSVersion.Platform == PlatformID.Win32NT)
			{
				// Some files may have been copied over from the source
				// files with a readonly attribute, let's remove it before deleting

				foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
				{
					var attributes = File.GetAttributes(file);

					if ((attributes & FileAttributes.ReadOnly) != 0)
					{
						File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
					}
				}
			}

			// Delete all files and folders recursively
			Directory.Delete(path, true);
		}
	}
}
