using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_VersionCheckTarget
{
	[TestMethod]
	public void When_InputIsAbsoluteHttpsUrl_Then_TargetIsAccepted()
	{
		var result = VersionCheckTarget.TryParse("https://example.com/app", out var target, out var error);

		Assert.IsTrue(result);
		Assert.IsNull(error);
		Assert.IsNotNull(target);
		Assert.AreEqual("https://example.com/app/", target.SiteUri.ToString());
	}

	[TestMethod]
	public void When_InputIsHostnameWithoutScheme_Then_HttpsIsAssumed()
	{
		var result = VersionCheckTarget.TryParse("example.com", out var target, out var error);

		Assert.IsTrue(result);
		Assert.IsNull(error);
		Assert.IsNotNull(target);
		Assert.AreEqual("https://example.com/", target.SiteUri.ToString());
	}

	[TestMethod]
	public void When_InputIncludesPath_Then_PathIsPreserved()
	{
		var result = VersionCheckTarget.TryParse("example.com/myapp", out var target, out _);

		Assert.IsTrue(result);
		Assert.IsNotNull(target);
		Assert.AreEqual("https://example.com/myapp/", target.SiteUri.ToString());
	}

	[TestMethod]
	public void When_SchemeIsUnsupported_Then_ParseFails()
	{
		var result = VersionCheckTarget.TryParse("ftp://example.com", out var target, out var error);

		Assert.IsFalse(result);
		Assert.IsNull(target);
		StringAssert.Contains(error, "Only http(s) URLs are allowed.");
	}

	[TestMethod]
	public void When_InputIsEmpty_Then_ParseFails()
	{
		var result = VersionCheckTarget.TryParse("", out var target, out var error);

		Assert.IsFalse(result);
		Assert.IsNull(target);
		StringAssert.Contains(error, "required");
	}
}
