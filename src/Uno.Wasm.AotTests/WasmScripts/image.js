define(() => {
    document.body.innerHTML =
        "<h2>This sample is running <a href='https://github.com/nventive/Uno.Wasm.Bootstrap/blob/master/src/Uno.Wasm.AotTests/Benchmark.cs#L218'>this C# ray tracer</a> " +
        "using Mono's experimental WebAssembly AOT Runtime</h2>" +
        "<h3>This experimental App has been built using the <a href='https://github.com/nventive/Uno.Wasm.Bootstrap'>Uno.Wasm.Bootstrap</a> nuget package</h3>" +
        "<div id='resultImage' /><div id='results' />";
});

var Interop = {
    setImageRawData: function (dataPtr, width, height) {
        var img = document.createElement("img");
        var parent = document.getElementById('resultImage');
        parent.insertBefore(img, parent.lastChild);

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
