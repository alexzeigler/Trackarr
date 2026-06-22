window.trackarrMissingCharts = {
    _charts: {},

    _destroy: function (key) {
        if (this._charts[key]) {
            this._charts[key].destroy();
            delete this._charts[key];
        }
    },

    renderActivity: function (id, data) {
        var el = document.getElementById(id);
        if (!el || typeof ApexCharts === "undefined") return;

        if (this._charts.activity) {
            this._charts.activity.updateOptions({
                xaxis: {categories: data.labels},
                series: [
                    {name: "Pushed", data: data.pushed},
                    {name: "Cascaded", data: data.cascaded},
                    {name: "Resolved", data: data.resolved},
                    {name: "Failed", data: data.failed}
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
                {name: "Pushed", data: data.pushed},
                {name: "Cascaded", data: data.cascaded},
                {name: "Resolved", data: data.resolved},
                {name: "Failed", data: data.failed}
            ],
            colors: ["#7738e9", "#4299e1", "#2fb344", "#d63939"],
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

    renderStateBreakdown: function (id, data) {
        var el = document.getElementById(id);
        if (!el || typeof ApexCharts === "undefined") return;

        var colorMap = {
            "New": "#4299e1",
            "In Queue": "#8d57ef",
            "Search Pushed": "#7738e9",
            "Cooldown": "#f4b83f",
            "Skipped": "#94a3b8",
            "Not Available": "#f76707",
            "Stuck": "#d63939",
            "Abandoned": "#6c757d"
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

    destroyAll: function () {
        Object.keys(this._charts).forEach(key => this._destroy(key));
    }
};
