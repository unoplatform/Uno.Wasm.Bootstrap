### Dependency management
The Uno Bootstrapper uses RequireJS for dependency management, allowing for dependencies to be resolved in a stable manner. 

For instance, a script defined this way, placed in the `WasmScripts` folder:

```javascript
define(() => {
    var txt = document.createTextNode("Loaded !");
    var parent = document.getElementById('uno-body');
    parent.insertBefore(txt, parent.lastChild);
});
```

will be executed appropriately.

Dependencies can also be declared this way: 

```javascript
define([], function() { return MyModule; });
```

If you're taking a dependency on an [AMD](https://en.wikipedia.org/wiki/Asynchronous_module_definition) enabled library, you'll need to publish the library as it would outside of the normal require flow.

As an example, to be able to use [html2canvas](https://html2canvas.hertzen.com/):
- Add the `html2canvas.js` file as an `EmbeddedResource` in the `WasmScripts` folder
- Then create an additional file called `myapp.js`, also as an `EmbeddedResource`
  ```
  require([`${config.uno_app_base}/html2canvas`], c => window.html2canvas = c);
  ```
- You'll then be able to access `window.html2canvas` from C# using `eval()`.

### Dependency management for Emscripten

Emscripten modules initialization is performed in an asynchronous way and the Bootstrapper 
will ensure that a dependency that exposes a module will have finished its initialization 
for starting the `Main` method of the C# code.
