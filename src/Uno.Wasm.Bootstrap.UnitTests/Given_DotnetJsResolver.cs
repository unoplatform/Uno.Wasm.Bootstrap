// ******************************************************************
// Copyright © 2015-2026 Uno Platform inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// ******************************************************************
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.Wasm.Bootstrap;

namespace Uno.Wasm.Bootstrap.UnitTests
{
	[TestClass]
	public class Given_DotnetJsResolver
	{
		private static readonly DateTime _t0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private static DotnetJsResolver.Candidate File(string name, int minutes) => new(name, _t0.AddMinutes(minutes));

		// Shape of <App>.staticwebassets.endpoints.json: the unfingerprinted route maps to the current file
		// (and to its compressed variants, which come first).
		private static string Manifest(string fingerprint, string routePrefix = "") =>
			"{\"Version\":1,\"ManifestType\":\"Publish\",\"Endpoints\":["
			+ $"{{\"Route\":\"{routePrefix}_framework/dotnet.{fingerprint}.js\",\"AssetFile\":\"_framework/dotnet.{fingerprint}.js\"}},"
			+ $"{{\"Route\":\"{routePrefix}_framework/dotnet.js\",\"AssetFile\":\"_framework/dotnet.{fingerprint}.js.br\"}},"
			+ $"{{\"Route\":\"{routePrefix}_framework/dotnet.js\",\"AssetFile\":\"_framework/dotnet.{fingerprint}.js\"}},"
			+ "{\"Route\":\"_framework/dotnet.native.js\",\"AssetFile\":\"_framework/dotnet.native.abcdefghij.js\"}"
			+ "]}";

		[TestMethod]
		[DataRow("dotnet.abc123xyz0.js", "abc123xyz0")]
		[DataRow("dotnet.js", null)]
		[DataRow("dotnet.native.js", null)]
		[DataRow("dotnet.runtime.js", null)]
		[DataRow("dotnet.native.abc123xyz0.js", null)]
		[DataRow("dotnet.abc123xyz0.js.br", null)]
		public void When_GetFingerprint(string fileName, string? expected)
			=> Assert.AreEqual(expected, DotnetJsResolver.GetFingerprint(fileName));

		[TestMethod]
		public void When_Manifest_Maps_Dotnet_Js_Then_Uncompressed_Asset_Wins()
			=> Assert.AreEqual("newfprint0", DotnetJsResolver.GetFingerprintFromEndpointsManifest(Manifest("newfprint0")));

		[TestMethod]
		public void When_Manifest_Route_Has_Base_Path()
			=> Assert.AreEqual("newfprint0", DotnetJsResolver.GetFingerprintFromEndpointsManifest(Manifest("newfprint0", routePrefix: "app/")));

		[TestMethod]
		public void When_Manifest_Has_No_Dotnet_Js_Route()
			=> Assert.IsNull(DotnetJsResolver.GetFingerprintFromEndpointsManifest("{\"Version\":1,\"Endpoints\":[]}"));

		[TestMethod]
		[DataRow("{\"Endpoints\":[")]
		[DataRow("[]")]
		[DataRow("")]
		public void When_Manifest_Is_Malformed_Then_It_Is_Unreadable(string json)
		{
			Assert.IsFalse(DotnetJsResolver.TryReadEndpointsManifest(json, out var fingerprint));
			Assert.IsNull(fingerprint);
			Assert.IsNull(DotnetJsResolver.GetFingerprintFromEndpointsManifest(json));
		}

		[TestMethod]
		public void When_Project_Manifest_Maps_Dotnet_Js_Then_Other_Manifests_Are_Ignored()
		{
			// A renamed project leaves its old manifest (and dotnet.js) behind
			var manifests = DotnetJsResolver.SelectManifests(
				Manifest("current000"),
				[new(Manifest("stale00000"), _t0.AddMinutes(10))]);

			CollectionAssert.AreEqual(new[] { Manifest("current000") }, manifests.ToArray());
		}

		[TestMethod]
		public void When_Project_Manifest_Is_Missing_Then_Others_Newest_First()
		{
			var manifests = DotnetJsResolver.SelectManifests(
				null,
				[new(Manifest("older00000"), _t0), new(Manifest("newer00000"), _t0.AddMinutes(5))]);

			CollectionAssert.AreEqual(new[] { Manifest("newer00000"), Manifest("older00000") }, manifests.ToArray());
		}

		[TestMethod]
		public void When_Project_Manifest_Has_No_Dotnet_Js_Route_Then_Others_Are_Used()
		{
			var manifests = DotnetJsResolver.SelectManifests(
				"{\"Version\":1,\"Endpoints\":[]}",
				[new(Manifest("client0000"), _t0)]);

			CollectionAssert.AreEqual(new[] { Manifest("client0000") }, manifests.ToArray());
		}

		[TestMethod]
		public void When_Project_Manifest_Points_To_Missing_File_Then_Stale_Manifest_Is_Not_Used()
		{
			var fingerprint = DotnetJsResolver.Resolve(
				DotnetJsResolver.SelectManifests(Manifest("missing000"), [new(Manifest("stale00000"), _t0)]),
				[File("dotnet.stale00000.js", 0)],
				out var source,
				out _);

			Assert.IsNull(fingerprint);
			Assert.AreEqual(DotnetJsResolver.Source.ManifestAssetMissing, source);
		}

		[TestMethod]
		public void When_Stale_Files_Are_Newer_Than_Manifest_Wins()
		{
			// A revert: the current file was skipped by an incremental copy and kept its old timestamp.
			var fingerprint = DotnetJsResolver.Resolve(
				[Manifest("current000")],
				[File("dotnet.current000.js", 0), File("dotnet.stale00000.js", 10), File("dotnet.native.js", 20)],
				out var source,
				out var stale);

			Assert.AreEqual("current000", fingerprint);
			Assert.AreEqual(DotnetJsResolver.Source.EndpointsManifest, source);
			CollectionAssert.AreEqual(new[] { "dotnet.stale00000.js" }, stale.ToArray());
		}

		[TestMethod]
		public void When_Manifest_Points_To_Missing_File_Then_Publish_Is_Incomplete()
		{
			var fingerprint = DotnetJsResolver.Resolve([Manifest("missing000")], [File("dotnet.stale00000.js", 0)], out var source, out _);

			Assert.IsNull(fingerprint);
			Assert.AreEqual(DotnetJsResolver.Source.ManifestAssetMissing, source);
		}

		[TestMethod]
		public void When_First_Manifest_Is_Unrelated_Then_Matching_Later_Manifest_Wins()
		{
			var fingerprint = DotnetJsResolver.Resolve(
				[Manifest("other00000"), Manifest("current000")],
				[File("dotnet.current000.js", 0), File("dotnet.stale00000.js", 10)],
				out var source,
				out _);

			Assert.AreEqual("current000", fingerprint);
			Assert.AreEqual(DotnetJsResolver.Source.EndpointsManifest, source);
		}

		[TestMethod]
		public void When_No_Manifest_And_Several_Candidates_Then_Newest_Is_Reported()
		{
			var fingerprint = DotnetJsResolver.Resolve([], [File("dotnet.older00000.js", 0), File("dotnet.newer00000.js", 5)], out var source, out var stale);

			Assert.AreEqual("newer00000", fingerprint);
			Assert.AreEqual(DotnetJsResolver.Source.NewestCandidate, source);
			CollectionAssert.AreEqual(new[] { "dotnet.older00000.js" }, stale.ToArray());
		}

		[TestMethod]
		public void When_Only_Unfingerprinted_Files()
		{
			// WasmFingerprintAssets=false: nothing to resolve.
			var fingerprint = DotnetJsResolver.Resolve([], [File("dotnet.js", 0), File("dotnet.native.js", 0), File("dotnet.runtime.js", 0)], out var source, out var stale);

			Assert.IsNull(fingerprint);
			Assert.AreEqual(DotnetJsResolver.Source.None, source);
			Assert.AreEqual(0, stale.Count);
		}
	}
}
