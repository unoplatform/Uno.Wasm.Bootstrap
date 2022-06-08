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

var DotNet = {
    invokeOnMainThread: function (str) {
        let getApplyUpdateCapabilitiesMethod = BINDING.bind_static_method("[Uno.Wasm.Threads] Uno.Wasm.Sample.Program:MainThreadCallback");
        getApplyUpdateCapabilitiesMethod();
    }
}
