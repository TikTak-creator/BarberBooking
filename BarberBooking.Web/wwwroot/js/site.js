document.querySelector('[data-toggle-nav]')?.addEventListener('click', event => {
    const open = document.querySelector('.top-nav').classList.toggle('is-open');
    event.currentTarget.setAttribute('aria-expanded', String(open));
});
document.querySelectorAll('form[data-confirm]').forEach(form => form.addEventListener('submit', event => { if (!confirm(form.dataset.confirm)) event.preventDefault(); }));
document.querySelectorAll('.business-sidebar a').forEach(link => { if (new URL(link.href).pathname === location.pathname) link.classList.add('active'); });
document.querySelectorAll('.account-nav a').forEach(link => { if (new URL(link.href).pathname === location.pathname) link.classList.add('active'); });
document.querySelector('[data-download-codes]')?.addEventListener('click', () => {
    const value = document.querySelector('.recovery-box pre').textContent;
    const url = URL.createObjectURL(new Blob(['Bladez — одноразовые резервные коды\n\n' + value], {type:'text/plain;charset=utf-8'}));
    const link = document.createElement('a'); link.href = url; link.download = 'Bladez-recovery-codes.txt'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
});
window.bladezCsrf = () => document.querySelector('meta[name="csrf-token"]').content;

document.querySelector('[data-print]')?.addEventListener('click', () => window.print());
