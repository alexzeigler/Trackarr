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
            legend: {
                position: "top"
            },
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
            legend: {
                show: false
            },
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