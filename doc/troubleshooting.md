---
uid: UnoWasmBootstrap.Features.Troubleshooting
---

### Windows Long Path support
The bootstrapper supports Windows 10 long paths by default, but there may be cases where the `\\?\` [path format](https://web.archive.org/web/20160818035729/https://blogs.msdn.microsoft.com/jeremykuhne/2016/06/21/more-on-new-net-path-handling/) may not be supported.

In such a case, setting the `<WasmShellEnableLongPathSupport>false</WasmShellEnableLongPathSupport>` in the project file can disable this feature.

