
# Debugging and contributing to the Uno WebAssembly Bootstrapper

The [src/Uno.Wasm.Bootstrap.sln](src/Uno.Wasm.Bootstrap.sln) solution is a good way to build the bootstrapper itself, as well as sample solutions that validate the different features of the bootstrapper.

## Debugging in Visual Studio for Windows

- Select a sample application, such as the `Uno.Wasm.Sample` project, and press `Ctrl+F5` or run **without debugger**.
- The bootstrapper will be built as part of the process, and will generate a new webassembly site layout.
- Once the application has built, it will run in the selected browser in the Visual Studio debug location toolbar

Some tips:

- If you make modifications to the `Uno.Wasm.Bootstrap`, you may have to terminate all `msbuild.exe` processes, as they may lock files of that project.
- If you make modifications to the `Uno.Wasm.Bootstrap.Cli` project, you may have to terminate the `dotnet.exe` processes that link to your solution's subfolders, as they may lock files of that project.

Once the processes have been terminated, restart your build.

Debugging the bootstrapper task can be done by adding a `Debugger.Launch()` statement in the `Run` method of `ShellTask.cs`.

## Testing the bootstrapper through GitPod

You can also make contributions through GitPod, and validate that your changes are appropriate.

Building and debugging samples is done through the command line.

1. Build a sample using :

   ```shell
   cd src/Uno.Wasm.Sample
   msbuild /r /bl
   ```

1. Start the web server to serve the sample on port 8000:

   ```shell
   cd bin/Debug/net5.0/dist
   python3 server.py
   ```

1. The GitPod IDE will open a preview window with the content of the site. You may need to open the browser debugger window to see the results of the sample's execution.

Click on the button below to try this out!

[![Open in Gitpod](https://gitpod.io/button/open-in-gitpod.svg)](https://gitpod.io/#https://github.com/unoplatform/Uno.Wasm.Bootstrap)

## Overriding the .NET WebAssembly SDK build

The msbuild property `NetCoreWasmSDKUri` allow the override of the default SDK path. The path can be a local file or remote file.

To select a different sdk build:

- For `net5` projects:

  - Generate a build from the `https://github.com/unoplatform/Uno.DotnetRuntime.WebAssembly` project
  - Copy the `dotnet-runtime-wasm-XX-XX-Release.zip` uri or local zip file path to the `NetCoreWasmSDKUri` property

> [!NOTE]
> Override properties require a zip file as the source, not an uncompressed folder.
