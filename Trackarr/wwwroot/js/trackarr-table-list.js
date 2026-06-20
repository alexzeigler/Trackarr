window.trackarrTable = {
    instances: {},

    init(tableId, searchInputId, pageCountId, pageSize, valueNames) {
        const root = document.getElementById(tableId);
        const searchInput = document.getElementById(searchInputId);
        const size = Number(pageSize) || 10;

        if (!root || !window.List) {
            return;
        }

        const discoveredValueNames = this.discoverValueNames(root, valueNames);
        const signature = JSON.stringify(discoveredValueNames);

        if (this.instances[tableId] && (this.instances[tableId].root !== root || this.instances[tableId].signature !== signature)) {
            delete this.instances[tableId];
        }

        if (this.instances[tableId]) {
            const existing = this.instances[tableId];

            this.bindSearch(searchInput, existing);
            this.bindSort(root, existing);

            existing.list.reIndex();
            existing.currentPage = 1;
            existing.applyFilters();

            return;
        }

        const list = new List(tableId, {
            sortClass: "table-sort",
            listClass: "table-tbody",
            page: size,
            valueNames: discoveredValueNames
        });

        const state = {
            root,
            signature,
            list,
            size,
            tableId,
            searchInputId,
            pageCountId,
            currentPage: 1,
            showPage: null,
            applyFilters: null,
            activeFilters: {}
        };

        this.instances[tableId] = state;

        const prevIcon = `<svg xmlns="http://www.w3.org/2000/svg" class="icon icon-1" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 6l-6 6l6 6"></path></svg>`;
        const nextIcon = `<svg xmlns="http://www.w3.org/2000/svg" class="icon icon-1" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 6l6 6l-6 6"></path></svg>`;

        const getRoot = () => document.getElementById(tableId);
        const getPagination = () => getRoot()?.querySelector(".pagination");
        const getPageCount = () => document.getElementById(pageCountId);

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
            const pagination = getPagination();
            const pageCount = getPageCount();
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
            const hasFilters = Object.values(filters).some(value => value !== null && value !== undefined && value !== "");

            if (!hasFilters) {
                list.filter();
            } else {
                list.filter(item => {
                    const values = item.values();

                    return Object.entries(filters).every(([key, value]) => {
                        if (value === null || value === undefined || value === "") {
                            return true;
                        }

                        return values[key] === value;
                    });
                });
            }

            state.currentPage = 1;
            state.showPage(1);
        };

        this.bindSearch(searchInput, state);
        this.bindSort(root, state);

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

    discoverValueNames(root, valueNames) {
        const map = new Map();

        (valueNames || []).forEach(value => {
            if (typeof value === "string") {
                map.set(value, value);
            } else if (value?.name) {
                map.set(value.name, value);
            }
        });

        root.querySelectorAll(".table-sort[data-sort]").forEach(element => {
            const name = element.dataset.sort;

            if (name) {
                map.set(name, name);
            }
        });

        root.querySelectorAll(".table-tbody [class]").forEach(element => {
            Array.from(element.classList)
                .filter(className => className.startsWith("sort-") || className.startsWith("filter-"))
                .forEach(className => {
                    if (!map.has(className)) {
                        map.set(className, className);
                    }
                });
        });

        return Array.from(map.values()).map(value => this.normalizeValueName(root, value));
    },

    normalizeValueName(root, value) {
        if (typeof value !== "string") {
            return value;
        }

        const header = root.querySelector(`.table-sort[data-sort="${this.escapeAttribute(value)}"]`);

        if (header?.dataset.sortAttr) {
            return {
                name: value,
                attr: header.dataset.sortAttr
            };
        }

        if (root.querySelector(`.${this.escapeClass(value)}[data-sort]`)) {
            return {
                name: value,
                attr: "data-sort"
            };
        }

        return value;
    },

    bindSearch(searchInput, state) {
        if (!searchInput) {
            return;
        }

        searchInput.oninput = () => {
            state.currentPage = 1;
            state.list.search(searchInput.value);
            state.showPage(1);
        };
    },

    bindSort(root, state) {
        root.querySelectorAll(".table-sort").forEach(element => {
            if (element.dataset.trackarrSortBound === state.tableId) {
                return;
            }

            element.dataset.trackarrSortBound = state.tableId;

            element.addEventListener("click", () => {
                state.currentPage = 1;

                window.requestAnimationFrame(() => {
                    state.showPage(1);
                });
            }, true);
        });
    },

    filter(tableId, key, value) {
        const state = this.instances[tableId];

        if (!state) {
            return;
        }

        state.activeFilters[key] = value || null;
        state.applyFilters();
    },

    clearFilters(tableId) {
        const state = this.instances[tableId];

        if (!state) {
            return;
        }

        state.activeFilters = {};
        state.applyFilters();
    },

    reindex(tableId) {
        const state = this.instances[tableId];

        if (!state) {
            return;
        }

        state.list.reIndex();
        state.applyFilters();
    },

    escapeClass(value) {
        if (window.CSS?.escape) {
            return window.CSS.escape(value);
        }

        return value.replace(/[^a-zA-Z0-9_-]/g, "\\$&");
    },

    escapeAttribute(value) {
        return String(value).replace(/\\/g, "\\\\").replace(/"/g, "\\\"");
    }
};