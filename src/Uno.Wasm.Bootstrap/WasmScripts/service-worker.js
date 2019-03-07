console.debug("[ServiceWorker] Initializing");

let config = {};

self.addEventListener('install', function (e) {
    console.debug('[ServiceWorker] Installing offline worker');
    e.waitUntil(
        fetch("/uno-config.js")
            .then(r => r.text()
                .then(configStr => {
                    eval(configStr);
                    caches.open(config.uno_remote_managedpath).then(function (cache) {
                        console.debug('[ServiceWorker] Caching app binaries and content');
                        return cache.addAll(config.offline_files);
                    });
                }
                )
            )
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', event => {
    event.respondWith(
        caches.match(event.request, { ignoreSearch: true }).then(response => {
            return response || fetch(event.request);
        })
    );
});
