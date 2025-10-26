async function downloadBackup() {
    const resp = await fetch('/api/admin/db/backup', { method: 'GET', credentials: 'include' });
    if (!resp.ok) {
        const t = await resp.text();
        alert('Chyba při vytváření zálohy: ' + t);
        return;
    }
    const blob = await resp.blob();
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'photoapp-backup.sqlite';
    document.body.appendChild(a);
    a.click();
    a.remove();
    window.URL.revokeObjectURL(url);
}

async function uploadRestore(fileInput) {
    const file = fileInput.files[0];
    if (!file) return alert('Vyber soubor.');

    if (file.size > 200 * 1024 * 1024) return alert('Soubor je příliš velký.');

    const form = new FormData();
    form.append('file', file);

    const resp = await fetch('/api/admin/db/restore', {
        method: 'POST',
        body: form,
        credentials: 'include'
    });

    const txt = await resp.text();
    if (!resp.ok) {
        alert('Restore failed: ' + txt);
    } else {
        alert('Restore OK: ' + txt);
    }
}