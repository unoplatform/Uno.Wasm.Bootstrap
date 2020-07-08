#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Uno.Wasm.TimezoneData
{
	public class TimezoneHelper
	{
		private const string AssemblyPrefix = "Uno.Wasm.TimezoneData.zoneinfo.";

		public static void Setup(string currentTimezone)
		{
#if DEBUG
			Console.WriteLine($"currentTimezone: {currentTimezone}");
#endif

			if (string.IsNullOrEmpty(currentTimezone))
			{
				foreach (var file in typeof(TimezoneHelper).Assembly.GetManifestResourceNames().Where(m => m.Contains("zoneinfo")))
				{
					PersistTimezoneFile(file);
				}
			}
			else
			{
				PersistTimezoneFile($"{AssemblyPrefix}{currentTimezone.Replace("/", ".")}");
			}
		}

		private static void PersistTimezoneFile(string file)
		{
			var timezoneResource = typeof(TimezoneHelper).Assembly.GetManifestResourceStream(file);

			if (timezoneResource != null)
			{
				using (var input = new BinaryReader(timezoneResource))
				{
					var baseZonePath = file.Replace(AssemblyPrefix, "").Replace(".", "/");

					// Relies on the path defined in TimeZoneInfo:
					// https://github.com/mono/mono/blob/eb468076a79982568e3bc66ae236fa1d2949ee1d/mcs/class/corlib/System/TimeZoneInfo.cs#L283
					var path = Path.Combine("/zoneinfo", baseZonePath);

					Directory.CreateDirectory(Path.GetDirectoryName(path));
					File.WriteAllBytes(path, input.ReadBytes((int)input.BaseStream.Length));
				}
			}
			else
			{
				Console.Error.WriteLine($"Unable to find timezone resource {file}, using UTC as fallback");
			}
		}
	}
}
