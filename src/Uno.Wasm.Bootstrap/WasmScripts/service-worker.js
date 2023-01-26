import { config } from "$(REMOTE_WEBAPP_PATH)$(REMOTE_BASE_PATH)/uno-config.js";

console.debug("[ServiceWorker] Initializing");

self.addEventListener('install', function (e) {
    console.debug('[ServiceWorker] Installing offline worker');
    e.waitUntil(
        caches.open('$(CACHE_KEY)').then(async function (cache) {
            console.debug('[ServiceWorker] Caching app binaries and content');

            // Add files one by one to avoid failed downloads to prevent the
            // worker to fail installing.
            for (var i = 0; i < config.offline_files.length; i++) {
                try {
                    await cache.add(config.offline_files[i]);
                }
                catch (e) {
                    console.debug(`[ServiceWorker] Failed to fetch ${config.offline_files[i]}`);
                }
            }
        })
    );
});

self.addEventListener('activate', event => {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', event => {
    event.respondWith(async function () {
        try {
            // Network first mode to get fresh content every time, then fallback to
            // cache content if needed.
            return await fetch(event.request);
        } catch (err) {
            return caches.match(event.request).then(response => {
                return response || fetch(event.request);
            });
        }
    }());
});
