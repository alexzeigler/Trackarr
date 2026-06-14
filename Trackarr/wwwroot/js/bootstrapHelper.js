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
            document.body.appendChild(menu);

            const r = e.target.getBoundingClientRect();
            menu.style.position = 'fixed';
            menu.style.inset = 'auto';
            menu.style.top = `${r.bottom}px`;
            menu.style.left = `${r.right - menu.offsetWidth}px`;
            menu.style.margin = '0';
            menu.style.zIndex = '1100';
            console.log("show.bs.dropdown");
        });

        document.addEventListener('hide.bs.dropdown', e => {
            const menu = e.target.parentElement.querySelector('.dropdown-menu')
                || document.body.querySelector(':scope > .dropdown-menu[data-portaled]');
            const origin = menu && menu._origin;
            if (origin) {
                menu.removeAttribute('style');
                origin.appendChild(menu);
            }
            console.log("hide.bs.dropdown");
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
dropdownPortal.init();
window.initTooltips();