// Based on https://github.com/dotnet/runtime/commit/7b4b23269b0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

internal class Utils
{
	public static bool CopyIfDifferent(string src, string dst, bool useHash)
	{
		if (!File.Exists(src))
			throw new ArgumentException($"Cannot find {src} file to copy", nameof(src));

		bool areDifferent = !File.Exists(dst) ||
								(useHash && ComputeHash(src) != ComputeHash(dst)) ||
								(File.ReadAllText(src) != File.ReadAllText(dst));

		if (areDifferent)
			File.Copy(src, dst, true);

		return areDifferent;
	}

	public static string ComputeHash(string filepath)
	{
		using var stream = File.OpenRead(filepath);
		using HashAlgorithm hashAlgorithm = SHA512.Create();

		byte[] hash = hashAlgorithm.ComputeHash(stream);
		return Convert.ToBase64String(hash);
	}
}
