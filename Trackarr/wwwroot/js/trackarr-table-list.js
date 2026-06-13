window.trackarrTable = {
    instances: {},

    init(tableId, searchInputId, pageCountId, pageSize, valueNames) {
        const root = document.getElementById(tableId);
        const searchInput = document.getElementById(searchInputId);
        const pageCount = document.getElementById(pageCountId);
        const normalizedValueNames = valueNames.map(value => {
            if (value === "sort-score" ||
                value === "sort-progress" ||
                value === "sort-activity" ||
                value === "sort-added") {
                return {
                    name: value,
                    attr: "data-sort"
                };
            }

            return value;
        });

        if (!root || !window.List) {
            return;
        }

        const pagination = root.querySelector(".pagination");
        const size = Number(pageSize) || 10;

        if (this.instances[tableId]) {
            const existing = this.instances[tableId];
            existing.list.reIndex();
            existing.currentPage = 1;
            existing.showPage(1);
            return;
        }

        const list = new List(tableId, {
            sortClass: "table-sort",
            listClass: "table-tbody",
            page: size,
            valueNames: normalizedValueNames
        });

        const state = {
            list,
            currentPage: 1,
            showPage: null,
            activeFilters: {}
        };

        this.instances[tableId] = state;

        const prevIcon = `<svg xmlns="http://www.w3.org/2000/svg" class="icon icon-1" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 6l-6 6l6 6"></path></svg>`;
        const nextIcon = `<svg xmlns="http://www.w3.org/2000/svg" class="icon icon-1" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 6l6 6l-6 6"></path></svg>`;

        const getItemCount = () => {
            if (Array.isArray(list.matchingItems)) {
                return list.matchingItems.length;
            }

            return list.items.length;
        };

        const getTotalPages = () => {
            return Math.max(1, Math.ceil(getItemCount() / size));
        };

        const button = (content, page, disabled = false) => {
            return `<li class="page-item${disabled ? " disabled" : ""}">
                <button type="button" class="page-link" data-page="${page}" ${disabled ? "disabled" : ""}>${content}</button>
            </li>`;
        };

        const renderPagination = () => {
            const totalItems = getItemCount();
            const totalPages = getTotalPages();

            state.currentPage = Math.min(Math.max(state.currentPage, 1), totalPages);

            const start = totalItems === 0 ? 0 : ((state.currentPage - 1) * size) + 1;
            const end = Math.min(state.currentPage * size, totalItems);

            if (pageCount) {
                pageCount.textContent = `${start} - ${end} of ${totalItems}`;
            }

            if (!pagination) {
                return;
            }

            pagination.innerHTML = [
                button(prevIcon, state.currentPage - 1, state.currentPage === 1),
                button(nextIcon, state.currentPage + 1, state.currentPage === totalPages)
            ].join("");

            pagination.querySelectorAll("[data-page]").forEach(element => {
                element.addEventListener("click", () => {
                    state.showPage(Number(element.dataset.page));
                });
            });
        };

        state.showPage = page => {
            const totalPages = getTotalPages();

            state.currentPage = Math.min(Math.max(page, 1), totalPages);

            list.show(((state.currentPage - 1) * size) + 1, size);

            renderPagination();
        };

        state.applyFilters = () => {
            const filters = state.activeFilters;
            const hasFilters = Object.values(filters).some(v => v !== null && v !== undefined);

            if (!hasFilters) {
                list.filter();
            } else {
                list.filter(item => {
                    return Object.entries(filters).every(([key, value]) => {
                        if (value === null || value === undefined) return true;
                        return item.values()[key] === value;
                    });
                });
            }

            state.currentPage = 1;
            state.showPage(1);
        };

        if (searchInput) {
            searchInput.oninput = () => {
                state.currentPage = 1;
                list.search(searchInput.value);
                state.showPage(1);
            };
        }

        root.querySelectorAll(".table-sort").forEach(element => {
            element.addEventListener("click", () => {
                state.currentPage = 1;

                window.requestAnimationFrame(() => {
                    state.showPage(1);
                });
            }, true);
        });

        list.on("sortStart", () => {
            state.currentPage = 1;
        });

        list.on("sortComplete", () => {
            state.showPage(1);
        });

        list.on("updated", () => {
            const totalPages = getTotalPages();

            if (state.currentPage > totalPages) {
                state.currentPage = totalPages;
            }

            renderPagination();
        });

        state.showPage(1);
    },

    filter(tableId, key, value) {
        const state = this.instances[tableId];
        if (!state) return;
        state.activeFilters[key] = value || null;
        state.applyFilters();
    },

    clearFilters(tableId) {
        const state = this.instances[tableId];
        if (!state) return;
        state.activeFilters = {};
        state.applyFilters();
    }
};