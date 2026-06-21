window.trackarrHistoryCharts = {
    activity: null,
    breakdown: null,

    renderActivity: function (id, data) {
        const el = document.getElementById(id);
        if (!el || typeof ApexCharts === "undefined") return;

        if (this.activity) {
            this.activity.destroy();
            this.activity = null;
        }

        this.activity = new ApexCharts(el, {
            chart: {
                type: "line",
                height: 260,
                toolbar: {show: false},
                background: "transparent"
            },
            theme: {
                mode: document.documentElement.getAttribute("data-bs-theme") || "dark"
            },
            stroke: {
                width: 2,
                curve: "smooth"
            },
            series: [
                {name: "Completed", data: data.completed},
                {name: "Failed", data: data.failed},
                {name: "Removed", data: data.removed},
                {name: "Added", data: data.added}
            ],
            xaxis: {
                categories: data.labels
            },
            legend: {show: false},
            grid: {
                borderColor: "rgba(255,255,255,.08)"
            }
        });

        this.activity.render();
    },

    renderBreakdown: function (id, data) {
        const el = document.getElementById(id);
        if (!el || typeof ApexCharts === "undefined") return;

        if (this.breakdown) {
            this.breakdown.destroy();
            this.breakdown = null;
        }

        this.breakdown = new ApexCharts(el, {
            chart: {
                type: "donut",
                height: 260,
                background: "transparent"
            },
            theme: {
                mode: document.documentElement.getAttribute("data-bs-theme") || "dark"
            },
            labels: data.labels,
            series: data.series,
            colors: ["#2fb344", "#d63939", "#f76707", "#4299e1"],
            legend: {show: false},
            plotOptions: {
                pie: {
                    donut: {
                        size: "70%"
                    }
                }
            }
        });

        this.breakdown.render();
    }
};
window.trackarrHistoryCharts.renderSpark = function (id, type, data, color) {
    const el = document.getElementById(id);
    if (!el || typeof ApexCharts === "undefined") return;
    if (this._sparks && this._sparks[id]) this._sparks[id].destroy();
    this._sparks = this._sparks || {};

    const chart = new ApexCharts(el, {
        chart: {type: type, height: 40, sparkline: {enabled: true}},
        stroke: {width: type === "bar" ? 0 : 2, curve: "smooth"},
        fill: type === "area" ? {type: "gradient", gradient: {opacityFrom: 0.4, opacityTo: 0}} : {opacity: 1},
        series: [{data: data}],
        colors: [color],
        tooltip: {enabled: false}
    });
    chart.render();
    this._sparks[id] = chart;
};

window.trackarrHistoryCharts.renderSearchActivity = function (id, data) {
    const el = document.getElementById(id);
    if (!el || typeof ApexCharts === "undefined") return;

    if (this.searchActivity) {
        this.searchActivity.destroy();
        this.searchActivity = null;
    }

    this.searchActivity = new ApexCharts(el, {
        chart: {type: "line", height: 260, toolbar: {show: false}, background: "transparent"},
        theme: {mode: document.documentElement.getAttribute("data-bs-theme") || "dark"},
        stroke: {width: 2, curve: "smooth"},
        series: [
            {name: "Searches Pushed", data: data.searches},
            {name: "Cascaded", data: data.cascaded},
            {name: "Abandoned", data: data.abandoned},
            {name: "Resolved", data: data.resolved}
        ],
        colors: ["#7738e9", "#4299e1", "#d63939", "#2fb344"],
        xaxis: {categories: data.labels},
        legend: {show: false},
        grid: {borderColor: "rgba(255,255,255,.08)"}
    });

    this.searchActivity.render();
};