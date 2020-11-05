define(() => {
    var txt = document.createTextNode("'test' dependency loaded successfully. Open F12 console to see output.");
    var parent = document.getElementById('uno-body');
    parent.append(txt);
});
