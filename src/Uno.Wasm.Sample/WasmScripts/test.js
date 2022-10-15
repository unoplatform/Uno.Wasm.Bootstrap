define(() => {
    var txt = document.createTextNode("'test' dependency loaded successfully. Open F12 console to see output.");
    var parent = document.getElementById('uno-body');
    parent.append(txt);
});

// Needs to be initialized early
async function initializeExports() {
    if (Module.getAssemblyExports !== undefined) {
        try {
            globalThis.samplesNetExports = await Module.getAssemblyExports("Uno.Wasm.SampleNet");
        }
        catch (e) {
            log.error(e);
        }
    }
}

initializeExports();

function testCallback() {
    try {
        if (Module.getAssemblyExports !== undefined && samplesNetExports.hasOwnProperty('Uno')) {
            return samplesNetExports.Uno.Wasm.Sample.Exports.MyExportedMethod1();
        }
        else {
            return Module.mono_bind_static_method("[Uno.Wasm.SampleNet] Uno.Wasm.Sample.Exports:MyExportedMethod2")();
        }
    }
    catch (e) {
        console.log(e);
        throw e;
    }
}
