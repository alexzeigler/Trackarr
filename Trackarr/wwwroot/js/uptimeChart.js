window.trackarrUptimeChart = {
    _chart: null,

    render: function (id, data) {
        const el = document.getElementById(id);
        if (!el || typeof ApexCharts === "undefined") return;

        if (this._chart) {
            this._chart.destroy();
            this._chart = null;
        }

        const primary = (getComputedStyle(document.documentElement).getPropertyValue("--tblr-primary") || "#206bc4").trim();
        const periodMs = data.periodEnd - data.periodStart;
        const day = 24 * 60 * 60 * 1000;

        // Granularity adapts to how wide the selected window is — hour-level detail makes sense for a
        // 24h view but is noise once the axis spans 7 or 30 days.
        const formatTick = function (ms) {
            const d = new Date(ms);
            if (periodMs <= day) return d.toLocaleTimeString(undefined, {hour: "numeric", minute: "2-digit"});
            if (periodMs <= 7 * day) return d.toLocaleString(undefined, {month: "short", day: "numeric", hour: "numeric"});
            return d.toLocaleDateString(undefined, {month: "short", day: "numeric"});
        };

        this._chart = new ApexCharts(el, {
            chart: {type: "area", height: 300, toolbar: {show: false}, background: "transparent"},
            theme: {mode: document.documentElement.getAttribute("data-bs-theme") || "dark"},
            stroke: {width: 2, curve: "smooth"},
            dataLabels: {enabled: false},
            markers: {size: 0, hover: {size: 5}},
            fill: {
                type: "gradient",
                gradient: {shadeIntensity: 1, opacityFrom: 0.35, opacityTo: 0, stops: [0, 90, 100]}
            },
            // [timestampMs, pingMs] pairs on a real datetime axis, so the axis itself represents the
            // calendar window — not just "however many points happen to exist".
            series: [{name: "Ping", data: data.points}],
            colors: [primary],
            xaxis: {
                type: "datetime",
                min: data.periodStart,
                max: data.periodEnd,
                labels: {
                    rotate: -45,
                    rotateAlways: true,
                    hideOverlappingLabels: true,
                    style: {fontSize: "11px"},
                    formatter: formatTick
                },
                axisTicks: {show: false},
                tooltip: {enabled: false}
            },
            yaxis: {
                labels: {
                    style: {fontSize: "11px"},
                    formatter: function (val) {
                        return val === null || val === undefined ? "" : `${Math.round(val)}ms`;
                    }
                }
            },
            legend: {show: false},
            grid: {borderColor: "rgba(255,255,255,.08)"},
            tooltip: {
                shared: true,
                intersect: false,
                x: {
                    formatter: function (val) {
                        return new Date(val).toLocaleString(undefined, {
                            month: "short", day: "numeric", hour: "numeric", minute: "2-digit"
                        });
                    }
                },
                y: {
                    formatter: function (val) {
                        return val === null || val === undefined ? "No response" : `${Math.round(val)} ms`;
                    }
                }
            }
        });
        this._chart.render();
    },

    destroy: function () {
        if (this._chart) {
            this._chart.destroy();
            this._chart = null;
        }
    }
};
