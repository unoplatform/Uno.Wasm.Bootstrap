using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;
using AwesomeAssertions;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_DotnetScriptCandidates
{
	[TestMethod]
	[Description("Verifies package-scoped _framework paths are preferred before the site-root fallback.")]
	public void When_ManagedPathIsPackageBased_Then_PackageCandidateIsTriedFirst()
	{
		var siteUri = new Uri("https://sl-dev.unoplatform.net/");
		var managedPath = new Uri("https://sl-dev.unoplatform.net/package_hash/_framework/");

		var candidates = VersionCheckService.BuildDotnetScriptCandidates(siteUri, managedPath, "dotnet.abc.js");

		candidates.Length.Should().Be(2);
		candidates[0].ToString().Should().Be("https://sl-dev.unoplatform.net/package_hash/_framework/dotnet.abc.js");
		candidates[1].ToString().Should().Be("https://sl-dev.unoplatform.net/_framework/dotnet.abc.js");
	}

	[TestMethod]
	[Description("Verifies candidate generation deduplicates identical managed-path and fallback URLs.")]
	public void When_ManagedPathMatchesRoot_Then_CandidatesAreDeduplicated()
	{
		var siteUri = new Uri("https://example.com/");
		var managedPath = new Uri("https://example.com/_framework/");

		var candidates = VersionCheckService.BuildDotnetScriptCandidates(siteUri, managedPath, "dotnet.js");

		candidates.Length.Should().Be(1);
		candidates[0].ToString().Should().Be("https://example.com/_framework/dotnet.js");
	}
}
