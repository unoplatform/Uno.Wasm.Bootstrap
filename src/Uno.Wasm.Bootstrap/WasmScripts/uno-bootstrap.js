
config.fetch_file_cb = asset => App.fetchFile(asset);

var Module = {
    onRuntimeInitialized: function () {
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

var App = {

    init: function () {
        this.loading = document.getElementById("loading");

        this.initializeRequire();
    },

    mainInit: function () {
        try {
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
            MonoRuntime.init();
        }
    }
};