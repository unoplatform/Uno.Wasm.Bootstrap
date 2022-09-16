define(() => {
    var txt = document.createTextNode("'test' dependency loaded successfully. Open F12 console to see output.");
    var parent = document.getElementById('uno-body');
    parent.append(txt);
});

async function testCallback() {
    try {
        if (Module.getAssemblyExports !== undefined) {
            var exports = await Module.getAssemblyExports("Uno.Wasm.SampleNet");

            exports.Uno.Wasm.Sample.Exports.MyExportedMethod1();
        }
        else {
            Module.mono_bind_static_method("[Uno.Wasm.SampleNet] Uno.Wasm.Sample.Program:MyExportedMethod2")();
        }
    }
    catch (e) {
        console.log(e);
        throw e;
    }
}
