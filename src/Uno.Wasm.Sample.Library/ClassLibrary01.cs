using System.Reflection;

[assembly: AssemblyMetadata("IsTrimmable", "True")]

namespace Uno.Wasm.Sample.Library
{
	public class ClassLibrary01
	{
		private static void Unused()
			=> typeof(Microsoft.CodeAnalysis.Accessibility).ToString();
	}
}
