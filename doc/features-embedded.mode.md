---
uid: UnoWasmBootstrap.Features.EmbeddedMode
---

# Browser Embedded mode

By default, the project is launched with a HTML page (`index.html`). This mode is used for SPAs (Single Page Applications), but does not allow embedding into an existing webpage or application.

It is possible to use the Browser Embedded mode to allow the launching using JavaScript instead.

1. Add this line in your project file:

   ```xml
   <WasmShellMode>BrowserEmbedded</WasmShellMode>
   ```

   The `embedded.js` file will be generated instead of `index.html`, containing the required code to launch the application.

2. In the HTML where you want to host the application, add the following:
   Using HTML:

   ```html
   <div id="uno-body" />
   <script src="https://path.to/your/wasm/app/embedded.js" />
   ```

   Using a script:

   ```javascript
   // you must ensure there's a <div id="uno-body" /> present in the DOM before calling this:
   import("https://path.to/your/wasm/app/embedded.js");
   ```

## Important notes about Browser Embedded mode

- There is no script isolation mechanisms, meaning that the application will have access to the same context and global objects.
- Loading more than one Uno bootstrapped application in the same page will conflict and produce unwanted results. A workaround would be to use a `<iframe>`.
