self.addEventListener('install', (event) => {
    console.log('Service Worker installing.');
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    console.log('Service Worker activated.');
});

self.addEventListener('fetch', (event) => {
    event.respondWith(
        caches.open('photoapp-cache').then((cache) =>
            cache.match(event.request).then((response) =>
                response || fetch(event.request).then((fetchedResponse) => {
                    try {
                        if (event.request.method === 'GET' && fetchedResponse && fetchedResponse.ok) {
                            cache.put(event.request, fetchedResponse.clone());
                        }
                    } catch (e) {}
                    return fetchedResponse;
                })
            )
        )
    );
});
