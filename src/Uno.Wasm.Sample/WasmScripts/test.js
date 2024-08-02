define(() => {
    var txt = document.createTextNode("'test' dependency loaded successfully. Open F12 console to see output.");
    var parent = document.getElementById('uno-body');
    parent.append(txt);
});

// Needs to be initialized early
async function initializeExports() {
    if (Module.getAssemblyExports !== undefined) {
        try {
            globalThis.samplesNetExports = await Module.getAssemblyExports("Uno.Wasm.SampleNet7");
        }
        catch (e) {
            console.error(e);
        }
    }
}

initializeExports();

function isIDBFSDefined() {
    return typeof IDBFS !== 'undefined';
}

function isRequireAvailable() {
    return typeof require.config !== 'undefined';
}

function testCallback() {

    try {
        if (Module.getAssemblyExports !== undefined && samplesNetExports.hasOwnProperty('Uno')) {
            return samplesNetExports.Uno.Wasm.Sample.Exports.MyExportedMethod1();
        }
        else {
            return Module.mono_bind_static_method("[Uno.Wasm.SampleNet7] Uno.Wasm.Sample.Exports:MyExportedMethod2")();
        }
    }
    catch (e) {
        console.log(e);
        throw e;
    }
}
