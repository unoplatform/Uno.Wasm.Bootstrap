// Imported from https://github.com/dotnet/aspnetcore/blob/0ee742c53f2669fd7233df6da89db5e8ab944585/src/Components/WebAssembly/WebAssembly/src/Http/BrowserRequestMode.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Uno.WebAssembly.Net.Http;

/// <summary>
/// The mode of the request. This is used to determine if cross-origin requests lead to valid responses
/// </summary>
public enum BrowserRequestMode
{
	/// <summary>
	/// If a request is made to another origin with this mode set, the result is simply an error
	/// </summary>
	SameOrigin,

	/// <summary>
	/// Prevents the method from being anything other than HEAD, GET or POST, and the headers from
	/// being anything other than simple headers.
	/// </summary>
	NoCors,

	/// <summary>
	/// Allows cross-origin requests, for example to access various APIs offered by 3rd party vendors.
	/// </summary>
	Cors,

	/// <summary>
	/// A mode for supporting navigation.
	/// </summary>
	Navigate,
}
