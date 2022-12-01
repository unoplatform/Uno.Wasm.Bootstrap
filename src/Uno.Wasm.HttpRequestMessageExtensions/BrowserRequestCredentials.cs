// Imported from https://github.com/dotnet/aspnetcore/blob/0ee742c53f2669fd7233df6da89db5e8ab944585/src/Components/WebAssembly/WebAssembly/src/Http/BrowserRequestCredentials.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Uno.WebAssembly.Net.Http;

/// <summary>
/// Specifies a value for the 'credentials' option on outbound HTTP requests.
/// </summary>
public enum BrowserRequestCredentials
{
	/// <summary>
	/// Advises the browser never to send credentials (such as cookies or HTTP auth headers).
	/// </summary>
	Omit,

	/// <summary>
	/// Advises the browser to send credentials (such as cookies or HTTP auth headers)
	/// only if the target URL is on the same origin as the calling application.
	/// </summary>
	SameOrigin,

	/// <summary>
	/// Advises the browser to send credentials (such as cookies or HTTP auth headers)
	/// even for cross-origin requests.
	/// </summary>
	Include,
}
