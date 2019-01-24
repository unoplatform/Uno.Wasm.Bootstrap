
config.fetch_file_cb = asset => App.fetchFile(asset);
config.environmentVariables = config.environmentVariables || { };

var Module = {
    onRuntimeInitialized: function () {

        if (config.environment) {
            for (var key in config.environment) {
                if (config.enable_debugging) console.log(`Setting ${key}=${config.environment[key]}`);
                ENV[key] = config.environmentVariables[key];
            }
        }

        MONO.mono_load_runtime_and_bcl(
            config.vfs_prefix,
            config.deploy_prefix,
            config.enable_debugging,
            config.file_list,
            function () {
                config.add_bindings();
                App.init();
            },
            config.fetch_file_cb
        );
    },
};

var MonoRuntime = {
    // This block is present for backward compatibility when "MonoRuntime" was provided by mono-wasm.

    init: function () {
        this.load_runtime = Module.cwrap('mono_wasm_load_runtime', null, ['string', 'number']);
        this.assembly_load = Module.cwrap('mono_wasm_assembly_load', 'number', ['string']);
        this.find_class = Module.cwrap('mono_wasm_assembly_find_class', 'number', ['number', 'string', 'string']);
        this.find_method = Module.cwrap('mono_wasm_assembly_find_method', 'number', ['number', 'string', 'number']);
        this.invoke_method = Module.cwrap('mono_wasm_invoke_method', 'number', ['number', 'number', 'number']);
        this.mono_string_get_utf8 = Module.cwrap('mono_wasm_string_get_utf8', 'number', ['number']);
        this.mono_string = Module.cwrap('mono_wasm_string_from_js', 'number', ['string']);
    },

    conv_string: function (mono_obj) {
        if (mono_obj === 0)
            return null;
        var raw = this.mono_string_get_utf8(mono_obj);
        var res = Module.UTF8ToString(raw);
        Module._free(raw);

        return res;
    },

    call_method: function (method, this_arg, args) {
        var args_mem = Module._malloc(args.length * 4);
        var eh_throw = Module._malloc(4);
        for (var i = 0; i < args.length; ++i)
            Module.setValue(args_mem + i * 4, args[i], "i32");
        Module.setValue(eh_throw, 0, "i32");

        var res = this.invoke_method(method, this_arg, args_mem, eh_throw);

        var eh_res = Module.getValue(eh_throw, "i32");

        Module._free(args_mem);
        Module._free(eh_throw);

        if (eh_res !== 0) {
            var msg = this.conv_string(res);
            throw new Error(msg);
        }

        return res;
    },
};

var App = {

    init: function () {
        this.loading = document.getElementById("loading");

        this.initializeRequire();
    },

    mainInit: function () {
        try {
            App.attachDebuggerHotkey(config.file_list);
            MonoRuntime.init();

            BINDING.call_static_method(config.uno_main, []);
        } catch (e) {
            console.error(e);
        }

        if (this.loading) {
            this.loading.hidden = true;
        }
    },

    fetchFile: function (asset) {

        if (asset.lastIndexOf(".dll") !== -1) {
            asset = asset.replace(".dll", "." + config.assemblyFileExtension);
        }

        asset = asset.replace("/managed/", "/" + config.uno_remote_managedpath + "/");

        return fetch(asset, { credentials: 'same-origin' });
    },

    initializeRequire: function () {
        if (config.enable_debugging) console.log("Done loading the BCL");

        if (config.uno_dependencies && config.uno_dependencies.length !== 0) {
            var pending = 0;

            var checkDone = (dependency) => {
                --pending;
                if (pending === 0) {
                    if (config.enable_debugging) console.log(`Loaded dependency (${dependency})`);
                    App.mainInit();
                }
            };

            config.uno_dependencies.forEach(function (dependency) {
                ++pending;
                if (config.enable_debugging) console.log(`Loading dependency (${dependency})`);

                require(
                    [dependency],
                    instance => {

                        // If the module is built on emscripten, intercept its loading.
                        if (instance && instance.HEAP8 !== undefined) {

                            var existingInitializer = instance.onRuntimeInitialized;

                            if (config.enable_debugging) console.log(`Waiting for dependency (${dependency}) initialization`);

                            instance.onRuntimeInitialized = () => {
                                checkDone(dependency);

                                if (existingInitializer)
                                    existingInitializer();
                            };
                        }
                        else {
                            checkDone(dependency);
                        }
                    }
                );
            });
        }
        else {
            App.mainInit();
        }
    },

    hasDebuggingEnabled: function () {
        return hasReferencedPdbs && App.currentBrowserIsChrome;
    },

    attachDebuggerHotkey: function (loadAssemblyUrls) {

        //
        // Imported from https://github.com/aspnet/Blazor/tree/release/0.7.0
        //
        // History:
        //  2019-01-14: Adjustments to make the debugger helper compatible with Uno.Bootstrap.
        //

        App.currentBrowserIsChrome = window.chrome
            && navigator.userAgent.indexOf('Edge') < 0; // Edge pretends to be Chrome

        hasReferencedPdbs = loadAssemblyUrls
            .some(function (url) { return /\.pdb$/.test(url); });

        // Use the combination shift+alt+D because it isn't used by the major browsers
        // for anything else by default
        var altKeyName = navigator.platform.match(/^Mac/i) ? 'Cmd' : 'Alt';

        if (App.hasDebuggingEnabled()) {
            console.info("Debugging hotkey: Shift+" + altKeyName + "+D (when application has focus)");
        }

        // Even if debugging isn't enabled, we register the hotkey so we can report why it's not enabled
        document.addEventListener('keydown', function (evt) {
            if (evt.shiftKey && (evt.metaKey || evt.altKey) && evt.code === 'KeyD') {
                if (!hasReferencedPdbs) {
                    console.error('Cannot start debugging, because the application was not compiled with debugging enabled.');
                }
                else if (!App.currentBrowserIsChrome) {
                    console.error('Currently, only Chrome is supported for debugging.');
                }
                else {
                    App.launchDebugger();
                }
            }
        });
    },

    launchDebugger: function () {

        //
        // Imported from https://github.com/aspnet/Blazor/tree/release/0.7.0
        //
        // History:
        //  2019-01-14: Adjustments to make the debugger helper compatible with Uno.Bootstrap.
        //

        // The noopener flag is essential, because otherwise Chrome tracks the association with the
        // parent tab, and then when the parent tab pauses in the debugger, the child tab does so
        // too (even if it's since navigated to a different page). This means that the debugger
        // itself freezes, and not just the page being debugged.
        //
        // We have to construct a link element and simulate a click on it, because the more obvious
        // window.open(..., 'noopener') always opens a new window instead of a new tab.
        var link = document.createElement('a');
        link.href = "_framework/debug?url=" + encodeURIComponent(location.href);
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
        link.click();
    }
};