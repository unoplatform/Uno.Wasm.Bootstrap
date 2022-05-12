
## Publishing the build results
The easiest way to publish the build results is to use the Visual Studio publish menu on your project. This will allow to use all the features provided by the standard experience, as described in the [Deploy to Azure App Service](https://docs.microsoft.com/en-us/visualstudio/deployment/quickstart-deploy-to-azure?view=vs-2017).

The publication of the application must be done in .NET Framework hosting (and not .NET Core), as the app uses the `web.config` file for the server configuration, and to enable the use of pre-compression.

For deeper integration in the publishing pipeline, the `WasmShellOutputPackagePath` property is defined by the bootstrapper after the `BuildDist` target, which contains the path to the generated `package_XXX` content.

## Serve the Wasm app through Windows Linux Subsystem
Using Windows 10, serving the app through a small Web Server is done through WSL.

Here's how to install it:
- Search for Ubuntu in the Windows Store: https://www.microsoft.com/en-us/search/result.aspx?q=ubuntu
- Install Ubuntu 18.04 or later, and follow the instructions during the first run
	- If you have another distribution installed make sure that the 18.04 is the default using `wslconfig /s "Ubuntu-20.04"`. You can list your active distributions with `wslconfig /l`
	- Note that WSL 2 is considerably slower than WSL 1 for the boostrapper scenario. You will need to set your distribution to version 1 using `wsl --set-version "Ubuntu-20.04" 1`.
- Once you've built your project, you should see a path to the project dll
- In the Ubuntu shell, type ``cd `wslpath "[the_path_to_your_bin_folder]\dist"` ``
- Type `python3 server.py`
    - If this command does not exist, run the following `sudo apt-get install python3`
