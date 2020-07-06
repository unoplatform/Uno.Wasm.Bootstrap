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
