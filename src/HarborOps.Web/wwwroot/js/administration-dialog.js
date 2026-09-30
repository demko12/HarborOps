const handlers = new WeakMap();

export function open(dialog, receiver) {
    if (!handlers.has(dialog)) {
        const cancel = event => {
            event.preventDefault();
            receiver.invokeMethodAsync("CloseEditorAsync");
        };
        dialog.addEventListener("cancel", cancel);
        handlers.set(dialog, cancel);
    }
    if (!dialog.open) dialog.showModal();
}

export function close(dialog) {
    if (dialog.open) dialog.close();
}
