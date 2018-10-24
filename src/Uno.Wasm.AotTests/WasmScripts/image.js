define(() => {

});

var Interop = {
        setImageRawData: function (dataPtr, width, height) {
            var img = document.createElement("img");
            var parent = document.getElementById('uno-body');
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
        }
    };
