window.modalHelper = {
    show: function (id) {
        const el = document.getElementById(id);
        if (el) {
            const modal = new bootstrap.Modal(el);
            modal.show();
        }
    },
    hide: function (id) {
        const el = document.getElementById(id);
        if (el) {
            const modal = bootstrap.Modal.getInstance(el);
            if (modal) modal.hide();
        }
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
    }
};