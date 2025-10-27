// Název cache
const CACHE_NAME = 'appka-pro-honzika-cache-v1';
// Seznam souborù, které se mají cachovat
const urlsToCache = [
    '/',
    '/css/site.css',
    '/js/site.js',
    // Pøidej další dùleité soubory (obrázky, fonty, atd.)
];

// Instalace Service Workera
self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => {
                console.log('Opened cache');
                return cache.addAll(urlsToCache);
            })
    );
});

// Aktivace Service Workera
self.addEventListener('activate', event => {
    var cacheWhitelist = [CACHE_NAME];
    event.waitUntil(
        caches.keys().then(cacheNames => {
            return Promise.all(
                cacheNames.map(cacheName => {
                    if (cacheWhitelist.indexOf(cacheName) === -1) {
                        return caches.delete(cacheName);
                    }
                })
            );
        })
    );
});

// Fetch event - zachytávání requestù a vracení odpovìdí z cache
self.addEventListener('fetch', event => {
    event.respondWith(
        caches.match(event.request)
            .then(response => {
                // Pokud je odpovìï v cache, vrátíme ji
                if (response) {
                    return response;
                }
                // Jinak provedeme request na sí
                return fetch(event.request);
            }
            )
    );
});