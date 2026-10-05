// Nút ẩn/hiện mật khẩu: <button data-password-toggle="idCuaONhap">Hiện</button>
document.addEventListener('click', event => {
    const button = event.target.closest('[data-password-toggle]');
    if (!button) return;
    const input = document.getElementById(button.dataset.passwordToggle);
    if (!input) return;
    const reveal = input.type === 'password';
    input.type = reveal ? 'text' : 'password';
    button.textContent = reveal ? 'Ẩn' : 'Hiện';
    button.setAttribute('aria-pressed', String(reveal));
    button.setAttribute('aria-label', reveal ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
});
