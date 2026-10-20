(() => {
    const filters = [...document.querySelectorAll('[data-filter]')];
    const rows = [...document.querySelectorAll('tbody [data-status]')];
    const audits = [...document.querySelectorAll('details[data-status]')];
    const search = document.querySelector('#search');
    const counter = document.querySelector('#visible-count');
    let selected = '';
    function apply() {
        const query = search.value.trim().toLocaleLowerCase('es');
        for (const item of [...rows, ...audits]) {
            item.hidden = (selected !== '' && item.dataset.status !== selected)
                || !item.dataset.search.toLocaleLowerCase('es').includes(query);
        }
        const visible = rows.filter(row => !row.hidden).length;
        counter.textContent = `${visible} de ${rows.length} archivos`;
        document.querySelector('#empty').hidden = visible !== 0;
    }
    for (const button of filters) {
        button.addEventListener('click', () => {
            selected = button.dataset.filter;
            filters.forEach(item => item.setAttribute('aria-pressed', String(item === button)));
            apply();
        });
    }
    search.addEventListener('input', apply);
    for (const link of document.querySelectorAll('[data-audit]')) {
        link.addEventListener('click', () => {
            const detail = document.getElementById(link.dataset.audit);
            if (detail) detail.open = true;
        });
    }
    document.querySelector('[data-print]').addEventListener('click', () => window.print());
    let printState;
    window.addEventListener('beforeprint', () => {
        printState = audits.map(item => ({item, open: item.open, hidden: item.hidden}));
        audits.forEach(item => {item.open = true; item.hidden = false;});
        rows.forEach(item => {item.hidden = false;});
        counter.textContent = `${rows.length} de ${rows.length} archivos`;
        document.querySelector('#empty').hidden = true;
    });
    window.addEventListener('afterprint', () => {
        for (const state of printState || []) {state.item.open = state.open; state.item.hidden = state.hidden;}
        apply();
    });
})();
