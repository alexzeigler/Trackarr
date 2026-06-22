window.trackarrQueueCharts = {
    _charts: {},

    _destroy: function (key) {
        if (this._charts[key]) {
            this._charts[key].destroy();
            delete this._charts[key];
        }
    },

    renderActivity: function (id, data) {
        const el = document.getElementById(id);
        if (!el || typeof ApexCharts === "undefined") return;

        if (this._charts.activity) {
            this._charts.activity.updateOptions({
                xaxis: {categories: data.labels},
                series: [
                    {name: "Added", data: data.added},
                    {name: "Completed", data: data.completed},
                    {name: "Failed", data: data.failed},
                    {name: "Removed", data: data.removed}
                ]
            }, false, false);
            return;
        }

        this._charts.activity = new ApexCharts(el, {
            chart: {type: "line", height: 260, toolbar: {show: false}, background: "transparent"},
            theme: {mode: document.documentElement.getAttribute("data-bs-theme") || "dark"},
            stroke: {width: 2, curve: "smooth"},
            dataLabels: {enabled: false},
            markers: {size: 0},
            series: [
                {name: "Added", data: data.added},
                {name: "Completed", data: data.completed},
                {name: "Failed", data: data.failed},
                {name: "Removed", data: data.removed}
            ],
            colors: ["#4299e1", "#2fb344", "#d63939", "#f76707"],
            xaxis: {
                categories: data.labels,
                labels: {
                    rotate: 0,
                    hideOverlappingLabels: true,
                    style: {fontSize: "11px"},
                    formatter: function (val, timestamp, opts) {
                        if (!val) return "";
                        if (opts && opts.i !== undefined && opts.i % 8 !== 0) return "";
                        return val;
                    }
                },
                axisTicks: {show: false},
                tooltip: {enabled: false}
            },
            yaxis: {labels: {style: {fontSize: "11px"}}},
            legend: {show: false},
            grid: {borderColor: "rgba(255,255,255,.08)"},
            tooltip: {shared: true, intersect: false}
        });
        this._charts.activity.render();
    },

    renderStatusBreakdown: function (id, data) {
        const el = document.getElementById(id);
        if (!el || typeof ApexCharts === "undefined") return;

        var colorMap = {
            "Downloading": "#2fb344",
            "Importing": "#4299e1",
            "Queued": "#8d57ef",
            "Stalled": "#f4b83f",
            "Failed": "#d63939",
            "Warning": "#f76707",
            "Paused": "#94a3b8",
            "Fetching Metadata": "#4299e1",
            "Other": "#6c757d"
        };
        var colors = data.labels.map(function (label) {
            return colorMap[label] || "#94a3b8";
        });

        if (this._charts.breakdown) {
            this._charts.breakdown.updateOptions({
                labels: data.labels,
                series: data.series,
                colors: colors
            }, false, false);
            return;
        }

        this._charts.breakdown = new ApexCharts(el, {
            chart: {type: "donut", height: 260, background: "transparent"},
            theme: {mode: document.documentElement.getAttribute("data-bs-theme") || "dark"},
            labels: data.labels,
            series: data.series,
            colors: colors,
            legend: {show: false},
            plotOptions: {pie: {donut: {size: "70%"}}}
        });
        this._charts.breakdown.render();
    },

    renderHealthDistribution: function (id, data) {
        const el = document.getElementById(id);
        if (!el || typeof ApexCharts === "undefined") return;

        if (this._charts.health) {
            this._charts.health.updateOptions({
                xaxis: {categories: data.labels},
                series: [{name: "Downloads", data: data.series}]
            }, false, false);
            return;
        }

        this._charts.health = new ApexCharts(el, {
            chart: {type: "bar", height: 260, toolbar: {show: false}, background: "transparent"},
            theme: {mode: document.documentElement.getAttribute("data-bs-theme") || "dark"},
            plotOptions: {bar: {borderRadius: 4, horizontal: false, columnWidth: "55%", distributed: true}},
            dataLabels: {enabled: false},
            series: [{name: "Downloads", data: data.series}],
            colors: ["#d63939", "#f76707", "#f4b83f", "#4299e1", "#2fb344"],
            xaxis: {categories: data.labels, labels: {style: {fontSize: "11px"}}},
            yaxis: {labels: {style: {fontSize: "11px"}}},
            legend: {show: false},
            grid: {borderColor: "rgba(255,255,255,.08)"},
            tooltip: {
                y: {
                    formatter: function (val) {
                        return val + " download" + (val !== 1 ? "s" : "");
                    }
                }
            }
        });
        this._charts.health.render();
    },

    destroyAll: function () {
        Object.keys(this._charts).forEach(key => this._destroy(key));
    }
};
