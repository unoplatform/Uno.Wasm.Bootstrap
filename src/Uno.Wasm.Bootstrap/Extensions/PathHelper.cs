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

}
