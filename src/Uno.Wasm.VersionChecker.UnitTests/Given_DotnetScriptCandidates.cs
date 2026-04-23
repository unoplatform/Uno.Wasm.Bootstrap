using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.VersionChecker;

namespace Uno.Wasm.VersionChecker.UnitTests;

[TestClass]
public class Given_DotnetScriptCandidates
{
	[TestMethod]
	public void When_ManagedPathIsPackageBased_Then_PackageCandidateIsTriedFirst()
	{
		var siteUri = new Uri("https://sl-dev.unoplatform.net/");
		var managedPath = new Uri("https://sl-dev.unoplatform.net/package_hash/_framework/");

		var candidates = VersionCheckService.BuildDotnetScriptCandidates(siteUri, managedPath, "dotnet.abc.js");

		Assert.AreEqual(2, candidates.Length);
		Assert.AreEqual("https://sl-dev.unoplatform.net/package_hash/_framework/dotnet.abc.js", candidates[0].ToString());
		Assert.AreEqual("https://sl-dev.unoplatform.net/_framework/dotnet.abc.js", candidates[1].ToString());
	}

	[TestMethod]
	public void When_ManagedPathMatchesRoot_Then_CandidatesAreDeduplicated()
	{
		var siteUri = new Uri("https://example.com/");
		var managedPath = new Uri("https://example.com/_framework/");

		var candidates = VersionCheckService.BuildDotnetScriptCandidates(siteUri, managedPath, "dotnet.js");

		Assert.AreEqual(1, candidates.Length);
		Assert.AreEqual("https://example.com/_framework/dotnet.js", candidates[0].ToString());
	}
}
