const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

test('switching barcode modes hides and disables manual input while preserving typed value', () => {
    let change;
    const elements = {
        GenerateBarcode: { value: 'false', addEventListener: (name, callback) => { assert.equal(name, 'change'); change = callback; } },
        CopyCode: { value: 'MANUAL-123' }, manualBarcodeField: {}, automaticBarcodeHelp: {}
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/book-copy-barcode-mode.js'), 'utf8'), {
        document: { getElementById: id => elements[id] }
    });
    for (const automatic of [false, true, false, true, false]) {
        elements.GenerateBarcode.value = String(automatic);
        change();
        assert.equal(elements.manualBarcodeField.hidden, automatic);
        assert.equal(elements.automaticBarcodeHelp.hidden, !automatic);
        assert.equal(elements.CopyCode.disabled, automatic);
        assert.equal(elements.CopyCode.required, !automatic);
        assert.equal(elements.CopyCode.value, 'MANUAL-123');
    }
});
