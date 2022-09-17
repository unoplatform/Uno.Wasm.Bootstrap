define(() => {
    var txt = document.createTextNode("'test' dependency loaded successfully. Open F12 console to see output.");
    var parent = document.getElementById('uno-body');
    parent.append(txt);
});

// Needs to be
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
