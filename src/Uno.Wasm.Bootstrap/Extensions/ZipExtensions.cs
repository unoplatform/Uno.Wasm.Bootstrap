using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Uno.Wasm.Bootstrap.Extensions
{
	public static class ZipExtensions
	{
		internal static void ExtractRelativeToDirectory(this ZipArchiveEntry source, string destinationDirectoryName, bool overwrite)
		{
			// Note that this will give us a good DirectoryInfo even if destinationDirectoryName exists:
			DirectoryInfo di = Directory.CreateDirectory(destinationDirectoryName);
			string destinationDirectoryFullPath = di.FullName;
			if (!destinationDirectoryFullPath.EndsWith(new string(Path.DirectorySeparatorChar, 1)))
			{
				destinationDirectoryFullPath += Path.DirectorySeparatorChar;
			}

			string fileDestinationPath = Path.GetFullPath(Path.Combine(destinationDirectoryFullPath, source.FullName));

			if (!fileDestinationPath.StartsWith(destinationDirectoryFullPath))
			{
				throw new IOException("ExtractingResultsInOutside");
			}

			if (Path.GetFileName(fileDestinationPath).Length == 0)
			{
				// If it is a directory:

				if (source.Length != 0)
				{
					throw new IOException("DirectoryNameWithData");
				}

				Directory.CreateDirectory(fileDestinationPath);
			}
			else
			{
				// If it is a file:
				// Create containing directory:
				Directory.CreateDirectory(Path.GetDirectoryName(fileDestinationPath));
				source.ExtractToFile(fileDestinationPath, overwrite: overwrite);
			}
		}

	}
}
