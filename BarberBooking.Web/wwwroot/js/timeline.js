const details = document.getElementById('appointmentDetails');
document.querySelectorAll('[data-appointment]').forEach(button => button.addEventListener('click', () => {
    const record = JSON.parse(button.dataset.appointment);
    details.querySelectorAll('[data-detail]').forEach(node => node.textContent = record[node.dataset.detail] || '—');
    details.querySelectorAll('input[name="id"]').forEach(input => input.value = record.id);
    details.querySelectorAll('input[name="version"]').forEach(input => input.value = record.version);
    const date = details.querySelector('input[name="date"]'); if (date) date.value = record.date.slice(0,10);
    const time = details.querySelector('input[name="time"]'); if (time) time.value = record.startTime.slice(0,5);
    const allowed = record.status === 0 ? [1,3] : record.status === 1 ? [2,3,4] : [];
    details.querySelectorAll('[data-transition]').forEach(button => button.hidden = !allowed.includes(Number(button.dataset.transition)));
    const section = details.querySelector('[data-reschedule-section]'); if(section) section.hidden = ![0,1].includes(record.status);
    bootstrap.Modal.getOrCreateInstance(details).show();
}));
const quick = document.getElementById('quick-form');
if(quick) {
    let sequence = 0;
    const load = async () => {
        const request = ++sequence;
        const target = quick.elements.TimeSlot; target.replaceChildren(new Option('Загружаем…','')); target.disabled = true;
        const info = quick.querySelector('[data-quick-status]'); info.textContent = '';
        const args = new URLSearchParams({barberId:quick.elements.BarberId.value,serviceId:quick.elements.ServiceId.value,date:quick.elements.Date.value});
        try {
            const response = await fetch('/api/booking/slots?'+args); const body = await response.json(); if(request!==sequence) return;
            target.replaceChildren(new Option('Выберите время',''));
            for(const slot of body.slots||[]) if(slot.isAvailable) target.add(new Option(slot.time,slot.time));
            target.disabled = false; info.textContent = !response.ok ? body.message || 'Не удалось получить время.' : target.options.length===1 ? 'Свободных слотов нет.' : 'Актуальность времени проверяется при сохранении.';
        } catch { if(request===sequence) { target.replaceChildren(new Option('Загрузка не удалась','')); info.textContent='Проверьте соединение и измените дату для повтора.'; } }
    };
    for(const key of ['BarberId','ServiceId','Date']) quick.elements[key].addEventListener('change',load);
    document.getElementById('quickBooking').addEventListener('shown.bs.modal',load);
}
