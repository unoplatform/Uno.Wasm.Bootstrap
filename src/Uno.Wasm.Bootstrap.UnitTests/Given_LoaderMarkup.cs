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
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Uno.Wasm.Bootstrap;

namespace Uno.Wasm.Bootstrap.UnitTests
{
	[TestClass]
	public class Given_LoaderMarkup
	{
		private const string Loader =
			"<div id=\"uno-body\"><div class=\"uno-loader\" loading-position=\"bottom\" data-phase=\"connect\">"
			+ "<img class=\"logo\" src=\"\" alt=\"\" /><div class=\"bar\"><progress></progress></div></div></div>";

		private const string LegacyLoader =
			"<div id=\"uno-body\"><div class=\"uno-loader\" loading-position=\"bottom\" loading-alert=\"none\">"
			+ "<img class=\"logo\" src=\"\" /><progress></progress><span class=\"alert\"></span></div></div>";

		private const string CustomLoader =
			"<div id=\"uno-body\"><div class=\"uno-loader my-brand\" data-uno-loader=\"custom\"><svg></svg></div></div>";

		private static Dictionary<string, string> Manifest(params string[] pairs)
		{
			Dictionary<string, string> manifest = new();
			for (var i = 0; i < pairs.Length; i += 2)
			{
				manifest[pairs[i]] = pairs[i + 1];
			}

			return manifest;
		}

		[TestMethod]
		public void When_Parse_Resizetizer_Output()
		{
			var manifest = LoaderMarkup.ParseAppManifest(
				"var UnoAppManifest = {\n    splashScreenImage: \"Assets/Splash.scale-200.png\",\n    splashScreenColor: \"#2E2E2E\",\n    displayName: \"My, \\\"App\\\": 2\"\n}");

			Assert.AreEqual("Assets/Splash.scale-200.png", manifest["splashScreenImage"]);
			Assert.AreEqual("#2E2E2E", manifest["splashScreenColor"]);
			Assert.AreEqual("My, \"App\": 2", manifest["displayName"]);
		}

		[TestMethod]
		public void When_Parse_Quoted_Keys_Single_Quotes_And_Comments()
		{
			var manifest = LoaderMarkup.ParseAppManifest(
				"// generated\nvar UnoAppManifest = { 'accentColor': '#f00', /* old: 'x' */ \"darkThemeBackgroundColor\": \"#000\", };");

			Assert.AreEqual("#f00", manifest["accentColor"]);
			Assert.AreEqual("#000", manifest["darkThemeBackgroundColor"]);
			Assert.IsFalse(manifest.ContainsKey("old"));
		}

		[TestMethod]
		public void When_Parse_Garbage_Then_Empty()
			=> Assert.AreEqual(0, LoaderMarkup.ParseAppManifest("not a manifest { at all").Count);

		[TestMethod]
		public void When_Bake_Colors_Then_Loader_Is_Styled_And_Marked()
		{
			var html = LoaderMarkup.BakeAppManifest(Loader, Manifest(
				"lightThemeBackgroundColor", "#fafafa",
				"darkThemeBackgroundColor", "#101010",
				"accentColor", "#00f",
				"darkThemeAccentColor", "#0ff"));

			StringAssert.Contains(html, "data-manifest=\"baked\"");
			StringAssert.Contains(html, "--light-theme-bg-color: #fafafa");
			StringAssert.Contains(html, "--dark-theme-bg-color: #101010");
			StringAssert.Contains(html, "--accent-color: #00f");
			StringAssert.Contains(html, "--dark-theme-accent-color: #0ff");
		}

		[TestMethod]
		public void When_Single_Theme_Color_Then_Background_Is_Inline()
			=> StringAssert.Contains(LoaderMarkup.BakeAppManifest(Loader, Manifest("splashScreenColor", "#123456")), "background-color: #123456");

		[TestMethod]
		public void When_Per_Theme_Colors_Then_SplashScreenColor_Is_Ignored()
		{
			var html = LoaderMarkup.BakeAppManifest(Loader, Manifest("splashScreenColor", "#123456", "darkThemeBackgroundColor", "#000"));

			Assert.IsFalse(html.Contains("background-color: #123456"));
		}

		[TestMethod]
		public void When_Transparent_SplashScreenColor_Then_Not_Applied()
			=> Assert.IsFalse(LoaderMarkup.BakeAppManifest(Loader, Manifest("splashScreenColor", "transparent")).Contains("background-color"));

		[TestMethod]
		public void When_Color_Is_Not_A_Color_Then_Skipped()
			=> Assert.IsFalse(LoaderMarkup.BakeAppManifest(Loader, Manifest("accentColor", "red; } body { display: none")).Contains("display"));

		[TestMethod]
		public void When_Image_Is_Local_Then_Relative_To_The_Package()
			=> StringAssert.Contains(LoaderMarkup.BakeAppManifest(Loader, Manifest("splashScreenImage", "Assets/Splash.png")), "src=\"./Assets/Splash.png\"");

		[TestMethod]
		public void When_Image_Is_Local_Then_Under_The_Given_Package_Url()
		{
			var html = LoaderMarkup.BakeAppManifest(Loader, Manifest("splashScreenImage", "Assets/Splash.png", "splashScreenImageDark", "Assets/Dark.png"), "/app/package_1/");

			StringAssert.Contains(html, "src=\"/app/package_1/Assets/Splash.png\"");
			StringAssert.Contains(html, "srcset=\"/app/package_1/Assets/Dark.png\"");
		}

		[TestMethod]
		public void When_Image_Is_Absolute_Url_Then_Kept()
			=> StringAssert.Contains(LoaderMarkup.BakeAppManifest(Loader, Manifest("splashScreenImage", "https://cdn.example.com/s.png")), "src=\"https://cdn.example.com/s.png\"");

		[TestMethod]
		public void When_Dark_Image_Then_Picture_Source_For_Dark_Scheme()
		{
			var html = LoaderMarkup.BakeAppManifest(Loader, Manifest("splashScreenImage", "light.png", "splashScreenImageDark", "dark.png"));

			StringAssert.Contains(html, "<picture><source media=\"(prefers-color-scheme: dark)\" srcset=\"./dark.png\" />");
			StringAssert.Contains(html, "src=\"./light.png\"");
			StringAssert.Contains(html, "</picture>");
		}

		[TestMethod]
		public void When_No_Image_Then_Default_Logo()
			=> StringAssert.Contains(LoaderMarkup.BakeAppManifest(Loader, Manifest("displayName", "App")), "src=\"https://uno-assets.platform.uno/logos/uno-splashscreen-light.png\"");

		[TestMethod]
		public void When_Script_Url_Then_Image_Skipped()
			=> Assert.IsFalse(LoaderMarkup.BakeAppManifest(Loader, Manifest("splashScreenImage", "javascript:alert(1)")).Contains("javascript:"));

		[TestMethod]
		public void When_Image_Has_Quotes_Then_Encoded()
			=> Assert.IsFalse(LoaderMarkup.BakeAppManifest(Loader, Manifest("splashScreenImage", "a\" onerror=\"x.png")).Contains("onerror=\"x"));

		[TestMethod]
		public void When_Custom_Loader_Then_Untouched()
			=> Assert.AreEqual(CustomLoader, LoaderMarkup.BakeAppManifest(CustomLoader, Manifest("splashScreenColor", "#123456")));

		[TestMethod]
		public void When_No_Loader_Then_Untouched()
			=> Assert.AreEqual("<div></div>", LoaderMarkup.BakeAppManifest("<div></div>", Manifest("splashScreenColor", "#123456")));

		[TestMethod]
		public void When_Custom_Attribute_Then_IsCustomLoader()
		{
			Assert.IsTrue(LoaderMarkup.IsCustomLoader(CustomLoader));
			Assert.IsFalse(LoaderMarkup.IsCustomLoader(Loader));
		}

		[TestMethod]
		public void When_Pre_Fluent_Markup_Then_Legacy()
		{
			Assert.IsTrue(LoaderMarkup.UsesLegacyMarkup(LegacyLoader));
			Assert.IsFalse(LoaderMarkup.UsesLegacyMarkup(Loader));
			Assert.IsFalse(LoaderMarkup.UsesLegacyMarkup(CustomLoader));
			Assert.IsFalse(LoaderMarkup.UsesLegacyMarkup("<div id=\"uno-body\"></div>"));
		}

		[TestMethod]
		[DataRow(".logo { background: url(logo.png); }")]
		[DataRow(".logo { background: url( './images/logo.png' ); }")]
		[DataRow(".logo { background: URL(\"../logo.svg\"); }")]
		[DataRow("@import 'theme.css';")]
		public void When_Stylesheet_Has_Relative_Urls(string css)
			=> Assert.IsTrue(LoaderMarkup.HasRelativeUrls(css));

		[TestMethod]
		[DataRow(".uno-loader { color: red; }")]
		[DataRow(".alert { --icon: url(\"data:image/svg+xml;base64,PHN2Zz4=\"); }")]
		[DataRow(".logo { background: url(https://example.com/logo.png); }")]
		[DataRow(".logo { background: url(/assets/logo.png); }")]
		[DataRow(".mask { mask: url(#shape); }")]
		[DataRow("@import url(https://fonts.example.com/font.css);")]
		public void When_Stylesheet_Has_No_Relative_Urls(string css)
			=> Assert.IsFalse(LoaderMarkup.HasRelativeUrls(css));
	}
}
