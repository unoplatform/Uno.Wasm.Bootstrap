using AwesomeAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_VersionCheckTarget
{
	[TestMethod]
	[Description("Verifies absolute HTTPS URLs are accepted and normalized with a trailing slash.")]
	public void When_InputIsAbsoluteHttpsUrl_Then_TargetIsAccepted()
	{
		var result = VersionCheckTarget.TryParse("https://example.com/app", out var target, out var error);

		result.Should().BeTrue();
		error.Should().BeNull();
		target.Should().NotBeNull();
		target.SiteUri.ToString().Should().Be("https://example.com/app/");
	}

	[TestMethod]
	[Description("Verifies bare hostnames default to HTTPS.")]
	public void When_InputIsHostnameWithoutScheme_Then_HttpsIsAssumed()
	{
		var result = VersionCheckTarget.TryParse("example.com", out var target, out var error);

		result.Should().BeTrue();
		error.Should().BeNull();
		target.Should().NotBeNull();
		target.SiteUri.ToString().Should().Be("https://example.com/");
	}

	[TestMethod]
	[Description("Verifies paths are preserved during target normalization.")]
	public void When_InputIncludesPath_Then_PathIsPreserved()
	{
		var result = VersionCheckTarget.TryParse("example.com/myapp", out var target, out _);

		result.Should().BeTrue();
		target.Should().NotBeNull();
		target.SiteUri.ToString().Should().Be("https://example.com/myapp/");
	}

	[TestMethod]
	[Description("Verifies unsupported schemes are rejected up front.")]
	public void When_SchemeIsUnsupported_Then_ParseFails()
	{
		var result = VersionCheckTarget.TryParse("ftp://example.com", out var target, out var error);

		result.Should().BeFalse();
		target.Should().BeNull();
		error.Should().Contain("Only http(s) URLs are allowed.");
	}

	[TestMethod]
	[Description("Verifies empty input is rejected with a useful message.")]
	public void When_InputIsEmpty_Then_ParseFails()
	{
		var result = VersionCheckTarget.TryParse("", out var target, out var error);

		result.Should().BeFalse();
		target.Should().BeNull();
		error.Should().Contain("required");
	}

	[TestMethod]
	[Description("Verifies invalid ports fail gracefully instead of throwing from UriBuilder.")]
	public void When_PortIsInvalid_Then_ParseFails()
	{
		var result = VersionCheckTarget.TryParse("https://example.com:99999", out var target, out var error);

		result.Should().BeFalse();
		target.Should().BeNull();
		error.Should().Contain("Unable to parse target");
	}

	[TestMethod]
	[Description("Verifies local and metadata network targets are rejected before inspection starts.")]
	public void When_TargetIsPrivateAddress_Then_ParseFails()
	{
		var result = VersionCheckTarget.TryParse("http://169.254.169.254/latest/meta-data/", out var target, out var error);

		result.Should().BeFalse();
		target.Should().BeNull();
		error.Should().Contain("private network");
	}

	[TestMethod]
	[Description("Verifies syntax-only parsing accepts private targets so command routing does not perform DNS or network validation.")]
	public void When_SyntaxOnlyParsingPrivateAddress_Then_TargetIsAccepted()
	{
		var result = VersionCheckTarget.TryParseSyntax("http://169.254.169.254/latest/meta-data/", out var target);

		result.Should().BeTrue();
		target.Should().NotBeNull();
		target.SiteUri.ToString().Should().Be("http://169.254.169.254/latest/meta-data/");
	}

	[TestMethod]
	[Description("Verifies userinfo is stripped from user-facing parse errors.")]
	public void When_InputContainsUserInfo_Then_ErrorIsSanitized()
	{
		var result = VersionCheckTarget.TryParse("ftp://user:secret@example.com", out var target, out var error);

		result.Should().BeFalse();
		target.Should().BeNull();
		error.Should().NotContain("secret");
		error.Should().NotContain("user:");
	}

	[TestMethod]
	[Description("Verifies special and mapped IP ranges are treated as non-public for SSRF protection.")]
	public void When_AddressIsNonRoutable_Then_IsPublicAddressReturnsFalse()
	{
		VersionCheckNetworkPolicy.IsPublicAddress(IPAddress.Parse("::")).Should().BeFalse();
		VersionCheckNetworkPolicy.IsPublicAddress(IPAddress.Parse("::ffff:127.0.0.1")).Should().BeFalse();
		VersionCheckNetworkPolicy.IsPublicAddress(IPAddress.Parse("100.64.0.1")).Should().BeFalse();
		VersionCheckNetworkPolicy.IsPublicAddress(IPAddress.Parse("224.0.0.1")).Should().BeFalse();
	}
}
