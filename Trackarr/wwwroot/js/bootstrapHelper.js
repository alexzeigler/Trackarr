window.modalHelper = {
    show: function (id) {
        const el = document.getElementById(id);
        if (el) {
            const modal = new bootstrap.Modal(el);
            modal.show();
        }
        console.log("modalHelper.show");
    },
    hide: function (id) {
        const el = document.getElementById(id);
        if (el) {
            const modal = bootstrap.Modal.getInstance(el);
            if (modal) modal.hide();
        }
        console.log("modalHelper.hide");
    },
    attachRestoreOnClose: function (historyId, originalId) {
        const el = document.getElementById(historyId);
        if (!el) return;

        el.addEventListener('hidden.bs.modal', function (event) {
            const modalEl = bootstrap.Modal.getInstance(el);
            if (!modalEl) return;
            const origEl = document.getElementById(originalId);
            if (origEl) {
                const origModal = new bootstrap.Modal(origEl);
                origModal.show();
            }
        }, {once: true});
        console.log("modalHelper.attachRestoreOnClose");
    }
};
window.dropdownPortal = {
    init() {
        document.addEventListener('show.bs.dropdown', e => {
            const menu = e.target.parentElement.querySelector('.dropdown-menu');
            if (!menu) return;

            menu._origin = e.target.parentElement;
            menu.setAttribute('data-portaled', '');
            document.body.appendChild(menu);

            const r = e.target.getBoundingClientRect();
            menu.style.position = 'fixed';
            menu.style.inset = 'auto';
            menu.style.top = `${r.bottom}px`;
            menu.style.left = `${r.right - menu.offsetWidth}px`;
            menu.style.margin = '0';
            menu.style.zIndex = '1100';
        });

        document.addEventListener('hide.bs.dropdown', e => {
            const menu = document.body.querySelector(':scope > .dropdown-menu[data-portaled]');
            if (!menu || !menu._origin) return;

            menu.removeAttribute('style');
            menu.removeAttribute('data-portaled');
            menu._origin.appendChild(menu);
            menu._origin = null;
        });
    }
};
window.initTooltips = () => {
    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(el => {
        const existingInstance = bootstrap.Tooltip.getInstance(el);

        if (existingInstance) {
            existingInstance.dispose();
        }

        bootstrap.Tooltip.getOrCreateInstance(el, {
            customClass: 'trackarr-tooltip',
            placement: 'top',
            container: '#tooltip-container'
        });
    });
    console.log("initToolTips")
};
window.tomSelectHelper = {
    init: function (id, dotnetRef) {
        const el = document.getElementById(id);
        if (!el) return;
        if (el.tomselect) el.tomselect.destroy();
        const ts = new TomSelect(el, {
            create: false,
            dropdownParent: 'body',
            onChange: v => dotnetRef.invokeMethodAsync('OnFallbackChanged', v)
        });
        document.body.appendChild(ts.dropdown);
        ts.dropdown.style.position = 'absolute';
        ts.on('dropdown_open', () => {
            console.log('parent:', ts.dropdown.parentElement.tagName, 'z:', ts.dropdown.style.zIndex);
            const r = ts.control.getBoundingClientRect();
            ts.dropdown.style.left = `${r.left + window.scrollX}px`;
            ts.dropdown.style.top = `${r.bottom + window.scrollY}px`;
            ts.dropdown.style.width = `${r.width}px`;
            ts.dropdown.style.zIndex = '1060';
        });
    },
    destroy: function (id) {
        const el = document.getElementById(id);
        if (el?.tomselect) el.tomselect.destroy();
    }
};
window.disposeTooltip = function (selector) {
    const el = document.querySelector(selector);
    if (!el) return;
    const t = bootstrap.Tooltip.getInstance(el);
    if (t) t.dispose();
};
dropdownPortal.init();
window.initTooltips();