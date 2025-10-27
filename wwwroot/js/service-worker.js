// N�zev cache
const CACHE_NAME = 'appka-pro-honzika-cache-v1';
// Seznam soubor�, kter� se maj� cachovat
const urlsToCache = [
    '/',
    '/css/site.css',
    '/js/site.js',
    // P�idej dal�� d�le�it� soubory (obr�zky, fonty, atd.)
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

// Fetch event - zachyt�v�n� request� a vracen� odpov�d� z cache
self.addEventListener('fetch', event => {
    event.respondWith(
        caches.match(event.request)
            .then(response => {
                // Pokud je odpov�� v cache, vr�t�me ji
                if (response) {
                    return response;
                }
                // Jinak provedeme request na s�
                return fetch(event.request);
            }
            )
    );
});