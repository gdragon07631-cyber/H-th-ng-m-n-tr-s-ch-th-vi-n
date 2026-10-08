const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function element(extra = {}) {
    const listeners = {};
    const classes = new Set();
    return {
        value: '', hidden: true, textContent: '', listeners,
        classList: { add: name => classes.add(name), remove: name => classes.delete(name), contains: name => classes.has(name) },
        addEventListener: (name, callback) => { listeners[name] = callback; },
        ...extra
    };
}

function setup() {
    const submitted = [];
    const elements = {
        loanLookupForm: element({ requestSubmit: submitter => submitted.push(submitter) }),
        loanLookupFrom: element(), loanLookupTo: element(), loanLookupStatus: element(),
        loanLookupApply: element(), loanLookupFilterError: element()
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../frontend/wwwroot/js/loan-lookup-filters.js'), 'utf8'), {
        document: { getElementById: id => elements[id] }
    });
    const submit = submitter => {
        let prevented = false;
        elements.loanLookupForm.listeners.submit({ submitter, preventDefault: () => { prevented = true; } });
        return prevented;
    };
    return { elements, submitted, submit };
}

test('applying a reversed date range is blocked with the expected message', () => {
    const { elements, submit } = setup();
    elements.loanLookupFrom.value = '2026-09-10';
    elements.loanLookupTo.value = '2026-09-01';

    assert.equal(submit(elements.loanLookupApply), true);
    assert.equal(elements.loanLookupFilterError.hidden, false);
    assert.equal(elements.loanLookupFilterError.textContent, 'Từ ngày không được lớn hơn đến ngày.');
    assert.equal(elements.loanLookupFrom.classList.contains('is-invalid'), true);
    // Dữ liệu nhập được giữ nguyên.
    assert.equal(elements.loanLookupFrom.value, '2026-09-10');
    assert.equal(elements.loanLookupTo.value, '2026-09-01');
});

test('valid, one-sided and equal ranges are submitted', () => {
    for (const [from, to] of [['2026-09-01', '2026-09-10'], ['2026-09-05', '2026-09-05'], ['2026-09-05', ''], ['', '2026-09-05'], ['', '']]) {
        const { elements, submit } = setup();
        elements.loanLookupFrom.value = from;
        elements.loanLookupTo.value = to;
        assert.equal(submit(elements.loanLookupApply), false, `${from}..${to}`);
    }
});

test('search and clear buttons are never blocked by the unapplied date range', () => {
    const { elements, submit } = setup();
    elements.loanLookupFrom.value = '2026-09-10';
    elements.loanLookupTo.value = '2026-09-01';

    assert.equal(submit({ name: 'op', value: 'search' }), false);
    assert.equal(submit({ name: 'op', value: 'clear' }), false);
});

test('fixing the range hides the error and Enter in a filter field applies the filter', () => {
    const { elements, submit, submitted } = setup();
    elements.loanLookupFrom.value = '2026-09-10';
    elements.loanLookupTo.value = '2026-09-01';
    submit(elements.loanLookupApply);

    elements.loanLookupTo.value = '2026-09-30';
    elements.loanLookupTo.listeners.change();
    assert.equal(elements.loanLookupFilterError.hidden, true);
    assert.equal(elements.loanLookupFrom.classList.contains('is-invalid'), false);

    let prevented = false;
    elements.loanLookupStatus.listeners.keydown({ key: 'Enter', preventDefault: () => { prevented = true; } });
    assert.equal(prevented, true);
    assert.deepEqual(submitted, [elements.loanLookupApply]);
});
