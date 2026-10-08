(() => {
    const form = document.getElementById('loanLookupForm');
    const from = document.getElementById('loanLookupFrom');
    const to = document.getElementById('loanLookupTo');
    const status = document.getElementById('loanLookupStatus');
    const apply = document.getElementById('loanLookupApply');
    const error = document.getElementById('loanLookupFilterError');
    if (!form || !from || !to || !status || !apply || !error) return;

    const reversedMessage = 'Từ ngày không được lớn hơn đến ngày.';
    // Ô date luôn gửi yyyy-MM-dd nên so sánh chuỗi đúng thứ tự ngày.
    const isReversed = () => from.value !== '' && to.value !== '' && from.value > to.value;
    const showReversed = () => {
        error.textContent = reversedMessage;
        error.hidden = false;
        from.classList.add('is-invalid');
        to.classList.add('is-invalid');
    };
    const clearReversed = () => {
        if (error.textContent !== reversedMessage) return;
        error.textContent = '';
        error.hidden = true;
        from.classList.remove('is-invalid');
        to.classList.remove('is-invalid');
    };

    // Chặn áp dụng khoảng ngày đảo ngược ngay trên giao diện; server vẫn kiểm tra lại.
    form.addEventListener('submit', event => {
        if (event.submitter !== apply || !isReversed()) return;
        event.preventDefault();
        showReversed();
    });

    for (const field of [from, to]) {
        field.addEventListener('change', () => { if (!isReversed()) clearReversed(); });
    }

    // Enter trong ô lọc là "Áp dụng bộ lọc"; Enter trong ô mã vẫn là "Tìm kiếm" (nút mặc định của form).
    for (const field of [from, to, status]) {
        field.addEventListener('keydown', event => {
            if (event.key !== 'Enter') return;
            event.preventDefault();
            form.requestSubmit(apply);
        });
    }
})();
