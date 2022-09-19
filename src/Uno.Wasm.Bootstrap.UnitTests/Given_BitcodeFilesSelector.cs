using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Uno.Wasm.Bootstrap.UnitTests
{
	[TestClass]
	public class Given_BitcodeFilesSelector
	{
		[TestMethod]
		[DataRow("0.0.0", "st", new[] { @"myBitcode.bc" }, new[] { @"myBitcode.bc" })]
		[DataRow("0.0.0", "st", new[] { @"myBitcode\myBitcode.bc" }, new[] { @"myBitcode\myBitcode.bc" })]
		[DataRow("0.0.0", "st", new[] { @"myNuget\myBitcode\myBitcode.bc" }, new[] { @"myNuget\myBitcode\myBitcode.bc" })]

		[DataRow("0.0.0", "st", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc" }, new[] { @"myBitcode.bc\1.2.3\myBitcode.bc" })]

		[DataRow("2.0.0", "st", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\2.0.0\myBitcode.bc" }, new[] { @"myBitcode.bc\2.0.0\myBitcode.bc" })]
		[DataRow("1.0.0", "st", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\2.0.0\myBitcode.bc" }, new[] { @"myBitcode.bc\1.2.3\myBitcode.bc" })]

		[DataRow("2.0.0", "st", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\2.0.0\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc" }, new[] { @"myBitcode.bc\2.0.0\myBitcode.bc" })]
		[DataRow("3.0.0", "st", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\2.0.0\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc" }, new[] { @"myBitcode.bc\3.0.0\myBitcode.bc" })]
		[DataRow("4.0.0", "st", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\2.0.0\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc" }, new[] { @"myBitcode.bc\3.0.0\myBitcode.bc" })]

		[DataRow("1.0.0", "st", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc", @"myBitcode2.bc\1.2.3\myBitcode2.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" }, new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode2.bc\1.2.3\myBitcode2.bc" })]
		[DataRow("3.0.0", "st", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc", @"myBitcode2.bc\1.2.3\myBitcode2.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" }, new[] { @"myBitcode.bc\3.0.0\myBitcode.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" })]
		[DataRow("4.0.0", "st", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc", @"myBitcode2.bc\1.2.3\myBitcode2.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" }, new[] { @"myBitcode.bc\3.0.0\myBitcode.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" })]

		[DataRow("3.1.7", "st", new[] { @"myBitcode.bc\1.3\myBitcode.bc", @"myBitcode.bc\2.0\myBitcode.bc", @"myBitcode.bc\3.1\myBitcode.bc", @"myBitcode.bc\5.0\myBitcode.bc" }, new[] { @"myBitcode.bc\3.1\myBitcode.bc" })]
		[DataRow("5.0.0", "st", new[] { @"myBitcode.bc\1.3\myBitcode.bc", @"myBitcode.bc\2.0\myBitcode.bc", @"myBitcode.bc\3.1\myBitcode.bc", @"myBitcode.bc\5.0\myBitcode.bc" }, new[] { @"myBitcode.bc\5.0\myBitcode.bc" })]
		[DataRow("5.0.1", "st", new[] { @"myBitcode.bc\1.3\myBitcode.bc", @"myBitcode.bc\2.0\myBitcode.bc", @"myBitcode.bc\3.1\myBitcode.bc", @"myBitcode.bc\5.0\myBitcode.bc" }, new[] { @"myBitcode.bc\5.0\myBitcode.bc" })]

		[DataRow("4.0.0", "st", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\4.0.0\myBitcode.bc", @"myBitcode2.bc\1.2.3\myBitcode2.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" }, new[] { @"myBitcode.bc\4.0.0\myBitcode.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" })]


		[DataRow(
			"3.1.12",
			"st",
			new[] { @"myBitcode.bc\1.3\myBitcode.bc", @"myBitcode.bc\2.0\myBitcode.bc", @"myBitcode.bc\3.1\myBitcode.bc", @"myBitcode.bc\5.0\myBitcode.bc" },
			new[] { @"myBitcode.bc\3.1\myBitcode.bc" })]
		[DataRow(
			"3.1.12",
			"mt",
			new[] { @"myBitcode.bc\1.3\st\myBitcode.bc", @"myBitcode.bc\2.0\st\myBitcode.bc", @"myBitcode.bc\3.1\st\myBitcode.bc", @"myBitcode.bc\5.0\st\myBitcode.bc" },
			new string[0])]
		[DataRow(
			"3.1.12",
			"st",
			new[] { @"myBitcode.bc\1.3\mt\myBitcode.bc", @"myBitcode.bc\3.1\mt\myBitcode.bc", @"myBitcode.bc\3.1\st\myBitcode.bc", @"myBitcode.bc\3.1\mt,simd\myBitcode.bc" },
			new[] { @"myBitcode.bc\3.1\st\myBitcode.bc" })]
		[DataRow(
			"3.1.12",
			"mt",
			new[] { @"myBitcode.bc\1.3\mt\myBitcode.bc", @"myBitcode.bc\3.1\mt\myBitcode.bc", @"myBitcode.bc\3.1\st\myBitcode.bc", @"myBitcode.bc\3.1\mt,simd\myBitcode.bc" },
			new[] { @"myBitcode.bc\3.1\mt\myBitcode.bc" })]
		[DataRow(
			"3.1.12",
			"simd,mt",
			new[] { @"myBitcode.bc\1.3\mt\myBitcode.bc", @"myBitcode.bc\3.1\mt\myBitcode.bc", @"myBitcode.bc\3.1\st\myBitcode.bc", @"myBitcode.bc\3.1\mt,simd\myBitcode.bc" },
			new[] { @"myBitcode.bc\3.1\mt,simd\myBitcode.bc" })]
		[DataRow(
			"3.1.12",
			"mt",
			new[] { @"myBitcode.bc\1.3\mt\myBitcode.bc", @"myBitcode.bc\2.0\mt\myBitcode.bc", @"myBitcode.bc\3.1\mt\myBitcode.bc", @"myBitcode.bc\5.0\mt\myBitcode.bc" },
			new[] { @"myBitcode.bc\3.1\mt\myBitcode.bc" })]
		[DataRow(
			"3.1.12",
			"mt,simd",
			new[] { @"myBitcode.bc\1.3\mt,simd\myBitcode.bc", @"myBitcode.bc\2.0\mt,simd\myBitcode.bc", @"myBitcode.bc\3.1\mt,simd\myBitcode.bc", @"myBitcode.bc\5.0\mt,simd\myBitcode.bc" },
			new[] { @"myBitcode.bc\3.1\mt,simd\myBitcode.bc" })]
		[DataRow(
			"3.1.12",
			"mt,simd",
			new[] { @"myBitcode.bc\1.3\mt,simd\myBitcode.bc", @"myBitcode.bc\2.0\myBitcode.bc", @"myBitcode.bc\3.1\myBitcode.bc", @"myBitcode.bc\5.0\myBitcode.bc" },
			new[] { @"myBitcode.bc\1.3\mt,simd\myBitcode.bc" })]
		[DataRow(
			"3.1.12",
			"mt,simd",
			new[] { @"myBitcode.bc\1.3\mt,simd\myBitcode.bc", @"myBitcode.bc\2.0\mt\myBitcode.bc", @"myBitcode.bc\3.1\mt\myBitcode.bc", @"myBitcode.bc\5.0\mt\myBitcode.bc" },
			new[] { @"myBitcode.bc\1.3\mt,simd\myBitcode.bc" })]

		public void When_NormalList(string version, string features, string[] input, string[] expected)
		{
			var filteredInput = BitcodeFilesSelector.Filter(Version.Parse(version), features.Split(","), input);
			Assert.AreEqual(string.Join(",", expected), string.Join(",", filteredInput));
		}
	}
}
