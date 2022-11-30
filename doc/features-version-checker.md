# Uno.VersionChecker

This is a tool to extract the version of _dotnet assemblies_ used on a Uno.UI application. Should also work with most applications built on _Uno Bootstrapper_.

## Installation
Install the executable using the following command:

``` shell
> dotnet tool install -g Uno.VersionChecker
```

## Usage of `uno-wasm-version`

Start the executable using the URI of your Uno application.

``` shell
> uno-wasm-version nuget.info
```

You should see the result as

```
Uno Version Checker v2.0.0
Checking website at address nuget.info/.
Trying to find Uno bootstrapper configuration...
Application found.
Configuration url is https://nuget.info/package_87a60034b14b4aae8011cc0b692a984ec34a9f7f/uno-config.js.
Starting assembly is PackageExplorer.
129 assemblies found. Downloading assemblies to read metadata...

Name                                                   Version                                                                                                                 File Version     Framework
-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
AuthenticodeExaminer                                   1.0.0                                                                                                                   1.0.0.0          .NETStandard,Version=v2.0
ColorCode.Core                                         1.0.0                                                                                                                   1.0.0.0          .NETStandard,Version=v2.0
ColorCode.UWP                                          1.0.0                                                                                                                   1.0.0.0          .NETStandard,Version=v2.0

[... many more lines ...]

System.Text.Json                                       6.0.0-dev                                                                                                               42.42.42.42424   .NETCoreApp,Version=v6.0
System.Text.RegularExpressions                         6.0.0-dev                                                                                                               42.42.42.42424   .NETCoreApp,Version=v6.0
System.Threading                                       6.0.0-dev                                                                                                               42.42.42.42424   .NETCoreApp,Version=v6.0
System.Web.HttpUtility                                 6.0.0-dev                                                                                                               42.42.42.42424   .NETCoreApp,Version=v6.0
Uno                                                    4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETStandard,Version=v2.0
Uno.Core.Extensions.Compatibility                      4.0.1+Branch.release-stable-4.0.Sha.22b308ce80cd950a755f49590bde3842b5c45843.22b308ce80cd950a755f49590bde3842b5c45843   1.0.0.0          .NETCoreApp,Version=v6.0
Uno.Core.Extensions.Disposables                        4.0.1+Branch.release-stable-4.0.Sha.22b308ce80cd950a755f49590bde3842b5c45843.22b308ce80cd950a755f49590bde3842b5c45843   1.0.0.0          .NETCoreApp,Version=v6.0
Uno.Core.Extensions.Logging                            4.0.1+Branch.release-stable-4.0.Sha.22b308ce80cd950a755f49590bde3842b5c45843.22b308ce80cd950a755f49590bde3842b5c45843   1.0.0.0          .NETCoreApp,Version=v6.0
Uno.Core.Extensions.Logging.Singleton                  4.0.1+Branch.release-stable-4.0.Sha.22b308ce80cd950a755f49590bde3842b5c45843.22b308ce80cd950a755f49590bde3842b5c45843   1.0.0.0          .NETCoreApp,Version=v6.0
Uno.Diagnostics.Eventing                               2.0.1+Branch.release-stable-2.0.Sha.219a53f37bfab968773eb7d563a9f93beedb63e2                                            255.255.255.255  .NETCoreApp,Version=v5.0
Uno.Extensions.Logging.WebAssembly.Console             1.1.0+Branch.release-stable-1.1.Sha.0a9f8024236c1fee96357291d17cf68a46ed0a3e.0a9f8024236c1fee96357291d17cf68a46ed0a3e   1.0.0.0          .NETCoreApp,Version=v5.0
Uno.Foundation                                         4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETStandard,Version=v2.0
Uno.Foundation.Logging                                 4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETCoreApp,Version=v6.0
Uno.Foundation.Runtime.WebAssembly                     4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETStandard,Version=v2.0
Uno.UI                                                 4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETStandard,Version=v2.0
Uno.UI.Adapter.Microsoft.Extensions.Logging            4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETCoreApp,Version=v6.0
Uno.UI.Composition                                     4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETStandard,Version=v2.0
Uno.UI.Dispatching                                     4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETStandard,Version=v2.0
Uno.UI.FluentTheme.v2                                  4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETStandard,Version=v2.0
Uno.UI.Runtime.WebAssembly                             4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETStandard,Version=v2.0
Uno.UI.Toolkit                                         4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETStandard,Version=v2.0
Uno.Wasm.TimezoneData                                  3.1.3+Branch.release-stable-3.1.Sha.a01173706452d6a3642c80df957b3b52c781fe91.a01173706452d6a3642c80df957b3b52c781fe91   1.0.0.0          .NETStandard,Version=v2.0
Uno.Xaml                                               4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72  255.255.255.255  .NETStandard,Version=v2.0


PackageExplorer version is 6.0.32+a7d8c67341
Uno.UI version is 4.0.11+Branch.release-stable-4.0.Sha.3a627c10121883bf42ef5b5a19ceb5468c4dcd72.3a627c10121883bf42ef5b5a19ceb5468c4dcd72
Runtime framework is .NETCoreApp,Version=v6.0 version 6.0.0-dev
```
