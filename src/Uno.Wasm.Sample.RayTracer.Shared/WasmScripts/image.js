define(() => {
    document.body.innerHTML =
        "<h2>This sample is running <a href='https://github.com/unoplatform/Uno.Wasm.Bootstrap/blob/master/src/Uno.Wasm.AotTests/Benchmark.cs#L218'>this C# ray tracer</a> " +
        "using .NET WebAssembly AOT Runtime</h2>" +
        "<h3>This App has been built using the <a href='https://github.com/unoplatform/Uno.Wasm.Bootstrap'>Uno.Wasm.Bootstrap</a> nuget package</h3>" +
        "<div style='display:flex; gap:20px;'>" +
        "  <div style='flex:1;'>" +
        "    <h3>Main Thread</h3>" +
        "    <div id='resultImage'></div>" +
        "    <div id='results'></div>" +
        "  </div>" +
        "  <div style='flex:1;'>" +
        "    <h3>Web Worker</h3>" +
        "    <div id='workerImage'></div>" +
        "    <div id='workerResults'>Starting worker...</div>" +
        "  </div>" +
        "</div>";

    // Start the worker (if available)
    try {
        var worker = new Worker('./_worker/worker.js');
        var workerResults = document.getElementById('workerResults');
        var workerReady = false;

        worker.addEventListener('message', function (e) {
            if (e.data && e.data.type === 'raytracer-result') {
                var width = e.data.width;
                var height = e.data.height;

                // Decode base64 BGRA data
                var raw = atob(e.data.base64);
                var bufferSize = width * height * 4;

                // Create canvas and render
                var rawCanvas = document.createElement("canvas");
                rawCanvas.width = width;
                rawCanvas.height = height;
                var ctx = rawCanvas.getContext("2d");
                var imgData = ctx.createImageData(width, height);

                // Convert BGRA → RGBA for canvas ImageData
                for (var i = 0; i < bufferSize; i += 4) {
                    imgData.data[i + 0] = raw.charCodeAt(i + 2); // R (from BGRA offset 2)
                    imgData.data[i + 1] = raw.charCodeAt(i + 1); // G (from BGRA offset 1)
                    imgData.data[i + 2] = raw.charCodeAt(i + 0); // B (from BGRA offset 0)
                    imgData.data[i + 3] = raw.charCodeAt(i + 3); // A (from BGRA offset 3)
                }

                ctx.putImageData(imgData, 0, 0);

                var img = document.createElement("img");
                img.src = rawCanvas.toDataURL();
                var parent = document.getElementById('workerImage');
                parent.appendChild(img);

                workerResults.appendChild(document.createTextNode('Total: ' + e.data.elapsed));

            } else if (e.data && e.data.type === 'raytracer-log') {
                if (!workerReady) {
                    // First log message clears the "Starting worker..." text
                    workerResults.textContent = '';
                    workerReady = true;
                }
                workerResults.appendChild(document.createTextNode(e.data.text));

            } else if (e.data && e.data.type === 'uno-worker-ready') {
                // Runtime initialized — don't clear results here since
                // raytracer-log and raytracer-result arrive before this.
            }
        });

        worker.addEventListener('error', function (e) {
            workerResults.textContent = 'Worker error: ' + e.message;
        });
    } catch (ex) {
        var wr = document.getElementById('workerResults');
        if (wr) wr.textContent = 'Worker not available: ' + ex.message;
    }
});

var Interop = {
    setImageRawData: function (dataPtr, width, height) {
        var img = document.createElement("img");
        var parent = document.getElementById('resultImage');
        parent.appendChild(img);

        var rawCanvas = document.createElement("canvas");
        rawCanvas.width = width;
        rawCanvas.height = height;

        var ctx = rawCanvas.getContext("2d");
        var imgData = ctx.createImageData(width, height);

        var bufferSize = width * height * 4;

        for (var i = 0; i < bufferSize; i += 4) {
            imgData.data[i + 0] = Module.HEAPU8[dataPtr + i + 2];
            imgData.data[i + 1] = Module.HEAPU8[dataPtr + i + 1];
            imgData.data[i + 2] = Module.HEAPU8[dataPtr + i + 0];
            imgData.data[i + 3] = Module.HEAPU8[dataPtr + i + 3];
        }

        ctx.putImageData(imgData, 0, 0);

        img.src = rawCanvas.toDataURL();
    },

    appendResult: function (str) {
        var img = document.createTextNode(str);
        var parent = document.getElementById('results');
        parent.appendChild(img, parent.lastChild);
    }
};
