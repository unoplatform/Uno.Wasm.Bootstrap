using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_VersionCheckTarget
{
	[TestMethod]
	[Description("Regression guard: verifies absolute HTTPS URLs are accepted and normalized with a trailing slash.")]
	public void When_InputIsAbsoluteHttpsUrl_Then_TargetIsAccepted()
	{
		var result = VersionCheckTarget.TryParse("https://example.com/app", out var target, out var error);

		Assert.IsTrue(result);
		Assert.IsNull(error);
		Assert.IsNotNull(target);
		Assert.AreEqual("https://example.com/app/", target.SiteUri.ToString());
	}

	[TestMethod]
	[Description("Regression guard: verifies bare hostnames default to HTTPS.")]
	public void When_InputIsHostnameWithoutScheme_Then_HttpsIsAssumed()
	{
		var result = VersionCheckTarget.TryParse("example.com", out var target, out var error);

		Assert.IsTrue(result);
		Assert.IsNull(error);
		Assert.IsNotNull(target);
		Assert.AreEqual("https://example.com/", target.SiteUri.ToString());
	}

	[TestMethod]
	[Description("Regression guard: verifies paths are preserved during target normalization.")]
	public void When_InputIncludesPath_Then_PathIsPreserved()
	{
		var result = VersionCheckTarget.TryParse("example.com/myapp", out var target, out _);

		Assert.IsTrue(result);
		Assert.IsNotNull(target);
		Assert.AreEqual("https://example.com/myapp/", target.SiteUri.ToString());
	}

	[TestMethod]
	[Description("Regression guard: verifies unsupported schemes are rejected up front.")]
	public void When_SchemeIsUnsupported_Then_ParseFails()
	{
		var result = VersionCheckTarget.TryParse("ftp://example.com", out var target, out var error);

		Assert.IsFalse(result);
		Assert.IsNull(target);
		StringAssert.Contains(error, "Only http(s) URLs are allowed.");
	}

	[TestMethod]
	[Description("Regression guard: verifies empty input is rejected with a useful message.")]
	public void When_InputIsEmpty_Then_ParseFails()
	{
		var result = VersionCheckTarget.TryParse("", out var target, out var error);

		Assert.IsFalse(result);
		Assert.IsNull(target);
		StringAssert.Contains(error, "required");
	}

	[TestMethod]
	[Description("Regression guard: verifies invalid ports fail gracefully instead of throwing from UriBuilder.")]
	public void When_PortIsInvalid_Then_ParseFails()
	{
		var result = VersionCheckTarget.TryParse("https://example.com:99999", out var target, out var error);

		Assert.IsFalse(result);
		Assert.IsNull(target);
		StringAssert.Contains(error, "Unable to parse target");
	}
}
