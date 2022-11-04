## Support for HttpRequestMessage Extensions
As of .NET 7, the BCL [does not provide a way](https://github.com/dotnet/runtime/issues/77904) to set the `fetch` API options for a `HttpRequestMessage`. This feature is provided as part of Blazor, which cannot be integrated into Uno Bootstrapper Applications at this time.

To provide similar functionality, the Uno Bootstrapper provides the `Uno.Wasm.HttpRequestMessageExtensions` package, which contains the same methods as documented here, under the `Uno.WebAssembly.Net.Http` namespace.

For instance, these extensions can be used to allow for http-only cookies to be included in cross-domain requests:
```csharp
request.SetBrowserRequestOption("credentials", "include");
```

See the [official .NET documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.components.webassembly.http.webassemblyhttprequestmessageextensions?view=aspnetcore-7.0) for the full list of available extensions.