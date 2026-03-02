// Auth cookie helpers — called from Blazor via IJSRuntime
window.sitimAuth = {
    setCookie: async function (token, expiresInSeconds) {
        await fetch('/auth/cookie/set', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token, expiresInSeconds })
        });
    },
    clearCookie: async function () {
        await fetch('/auth/cookie/clear', { method: 'POST' });
    }
};
