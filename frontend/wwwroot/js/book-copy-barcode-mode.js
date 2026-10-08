(() => {
    const mode = document.getElementById('GenerateBarcode');
    const input = document.getElementById('CopyCode');
    const field = document.getElementById('manualBarcodeField');
    const help = document.getElementById('automaticBarcodeHelp');
    if (!mode || !input || !field || !help) return;
    const sync = () => {
        const automatic = mode.value === 'true';
        field.hidden = automatic;
        help.hidden = !automatic;
        input.disabled = automatic;
        input.required = !automatic;
    };
    mode.addEventListener('change', sync);
    sync();
})();
