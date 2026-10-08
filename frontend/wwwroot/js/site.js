// Nút ẩn/hiện mật khẩu
document.addEventListener('click', event => {
    const button = event.target.closest('[data-password-toggle]');
    if (!button) return;
    const input = document.getElementById(button.dataset.passwordToggle);
    if (!input) return;
    const reveal = input.type === 'password';
    input.type = reveal ? 'text' : 'password';
    button.setAttribute('aria-pressed', String(reveal));
    button.setAttribute('aria-label', reveal ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');

    const icon = button.querySelector('i');
    if (icon) {
        icon.className = reveal ? 'bi bi-eye-slash' : 'bi bi-eye';
    } else {
        button.textContent = reveal ? 'Ẩn' : 'Hiện';
    }
});

