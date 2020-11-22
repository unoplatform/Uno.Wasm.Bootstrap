using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Uno.Wasm.Bootstrap.UnitTests
{
	[TestClass]
	public class Given_BitcodeFilesSelector
	{
		[TestMethod]
		[DataRow("0.0.0", new[] { @"myBitcode.bc" }, new[] { @"myBitcode.bc" })]
		[DataRow("0.0.0", new[] { @"myBitcode\myBitcode.bc" }, new[] { @"myBitcode\myBitcode.bc" })]
		[DataRow("0.0.0", new[] { @"myNuget\myBitcode\myBitcode.bc" }, new[] { @"myNuget\myBitcode\myBitcode.bc" })]

		[DataRow("0.0.0", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc" }, new[] { @"myBitcode.bc\1.2.3\myBitcode.bc" })]

		[DataRow("2.0.0", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\2.0.0\myBitcode.bc" }, new[] { @"myBitcode.bc\2.0.0\myBitcode.bc" })]
		[DataRow("1.0.0", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\2.0.0\myBitcode.bc" }, new[] { @"myBitcode.bc\1.2.3\myBitcode.bc" })]

		[DataRow("2.0.0", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\2.0.0\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc" }, new[] { @"myBitcode.bc\2.0.0\myBitcode.bc" })]
		[DataRow("3.0.0", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\2.0.0\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc" }, new[] { @"myBitcode.bc\3.0.0\myBitcode.bc" })]
		[DataRow("4.0.0", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\2.0.0\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc" }, new[] { @"myBitcode.bc\3.0.0\myBitcode.bc" })]

		[DataRow("1.0.0", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc", @"myBitcode2.bc\1.2.3\myBitcode2.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" }, new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode2.bc\1.2.3\myBitcode2.bc" })]
		[DataRow("3.0.0", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc", @"myBitcode2.bc\1.2.3\myBitcode2.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" }, new[] { @"myBitcode.bc\3.0.0\myBitcode.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" })]
		[DataRow("4.0.0", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\3.0.0\myBitcode.bc", @"myBitcode2.bc\1.2.3\myBitcode2.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" }, new[] { @"myBitcode.bc\3.0.0\myBitcode.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" })]

		[DataRow("4.0.0", new[] { @"myBitcode.bc\1.2.3\myBitcode.bc", @"myBitcode.bc\4.0.0\myBitcode.bc", @"myBitcode2.bc\1.2.3\myBitcode2.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" }, new[] { @"myBitcode.bc\4.0.0\myBitcode.bc", @"myBitcode2.bc\3.0.0\myBitcode2.bc" })]
		public void When_NormalList(string version, string[] input, string[] expected)
		{
			var filteredInput = BitcodeFilesSelector.Filter(Version.Parse(version), input);
			Assert.AreEqual(string.Join(",", expected), string.Join(",", filteredInput));
		}
	}
}
