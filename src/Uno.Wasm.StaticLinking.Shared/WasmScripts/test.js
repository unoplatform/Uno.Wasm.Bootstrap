define(() => {
    document.body.innerHTML = "<div id='results' />";
});

var Interop = {
    appendResult: function (str) {
        var txt = document.createTextNode(str);
        var parent = document.getElementById('results');
        parent.appendChild(txt, parent.lastChild);
    }
};

var Validation = {
    validateEmAddFunction: function (str) {
        var fPtr = Module.addFunction((state) => { }, 'vi');

        if (!fPtr) {
            throw `Could not execute emscripten addFunction`;
        }

        return fPtr;
    }
};

// Needs to be initialized first
async function initializeExports() {
    if (Module.getAssemblyExports !== undefined) {
        globalThis.samplesNetExports = await Module.getAssemblyExports("Uno.Wasm.StaticLinking");
    }
}

initializeExports();

function testCallback() {
    try {
        if (Module.getAssemblyExports !== undefined) {
            return samplesNetExports.Uno.Wasm.Sample.Exports.MyExportedMethod();
        }
        else {
            return Module.mono_bind_static_method("[Uno.Wasm.Sample] Uno.Wasm.Sample.Program:MyExportedMethod")();
        }
    }
    catch (e) {
        console.log(e);
        throw e;
    }
}
