// Unlike ReconnectModal.razor.js, this module exports and is imported from C# via IJSRuntime.
let handler = null;

export function register() {
    if (handler) {
        return;
    }
    handler = (event) => {
        // The browser shows its own generic wording; the on-page line is what actually explains
        // what is lost.
        event.preventDefault();
        event.returnValue = '';
    };
    window.addEventListener('beforeunload', handler);
}

export function unregister() {
    if (!handler) {
        return;
    }
    window.removeEventListener('beforeunload', handler);
    handler = null;
}
