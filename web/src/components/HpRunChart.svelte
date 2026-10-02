<script lang="ts">
  // HP 推移 (全員。spec ui.md §3.1): プレイヤーごとの線をプレイヤー色で重ねる。点を押すとその階の詳細へ。
  import { onMount, onDestroy } from 'svelte';
  import { Chart, registerables, type ChartConfiguration } from 'chart.js';
  import { roomVisual, type FloorSummary } from '../lib/runOverview';
  Chart.register(...registerables);

  interface Series { pid: string; name: string; color: string; floors: FloorSummary[] }
  interface Props { series: Series[]; onSelect: (floor: number) => void; highlight?: string }
  let { series, onSelect, highlight }: Props = $props();

  let canvas: HTMLCanvasElement;
  let chart: Chart | null = null;

  let labels = $derived([...new Set(series.flatMap(s => s.floors.map(f => f.floor)))].sort((a, b) => a - b));

  function floorAt(i: number): FloorSummary | undefined {
    for (const s of series) { const f = s.floors.find(x => x.floor === labels[i]); if (f) return f; }
    return undefined;
  }

  function buildData(): ChartConfiguration<'line'>['data'] {
    return {
      labels,
      datasets: series.map(s => {
        const byFloor = new Map(s.floors.map(f => [f.floor, f]));
        const dim = highlight && series.length > 1 && s.pid !== highlight;
        return {
          label: s.name,
          data: labels.map(fl => byFloor.get(fl)?.hp_out ?? null),
          borderColor: dim ? s.color + '77' : s.color,
          backgroundColor: s.color,
          borderWidth: dim ? 1.5 : 2.5,
          pointRadius: labels.map(fl => { const f = byFloor.get(fl); return f && ['Elite', 'Boss'].includes(f.room_type) ? 4 : 2.5; }),
          pointBackgroundColor: dim ? s.color + '77' : s.color,
          pointHoverRadius: 6,
          tension: 0,
          spanGaps: true,
        };
      }),
    };
  }

  function config(): ChartConfiguration<'line'> {
    return {
      type: 'line',
      data: buildData(),
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        interaction: { mode: 'index', intersect: false },
        onClick: (_e, els) => { if (els.length) onSelect(labels[els[0].index]); },
        onHover: (e, els) => { (e.native?.target as HTMLElement | undefined)?.style.setProperty('cursor', els.length ? 'pointer' : 'default'); },
        plugins: {
          legend: { display: series.length > 1, position: 'bottom', labels: { color: '#cbd5e1', boxWidth: 10, boxHeight: 10, usePointStyle: true } },
          tooltip: {
            callbacks: {
              title: (items) => {
                const f = floorAt(items[0].dataIndex);
                if (!f) return '';
                const v = roomVisual(f.room_type);
                return `${f.floor} 階 · ${v.label}` + (f.encounter_name ? ` · ${f.encounter_name}` : '');
              },
              label: (item) => {
                const s = series[item.datasetIndex];
                const f = s.floors.find(x => x.floor === labels[item.dataIndex]);
                if (!f) return '';
                return `${s.name}: ${f.hp_out}/${f.max_hp_out}`;
              },
              footer: () => 'クリックで階の詳細へ',
            },
          },
        },
        scales: {
          x: { ticks: { color: '#94a3b8', autoSkip: true, maxRotation: 0 }, grid: { color: '#252a33' }, title: { display: true, text: '階', color: '#94a3b8' } },
          y: { ticks: { color: '#94a3b8' }, grid: { color: '#252a33' }, beginAtZero: true, title: { display: true, text: 'HP', color: '#94a3b8' } },
        },
      },
    };
  }

  onMount(() => { chart = new Chart(canvas, config()); });
  onDestroy(() => { chart?.destroy(); chart = null; });
  $effect(() => {
    void series; void highlight; void labels;
    if (!chart) return;
    chart.data = buildData();
    chart.options.plugins!.legend!.display = series.length > 1;
    chart.update('none');
  });
</script>

<div class="h-72 sm:h-80"><canvas bind:this={canvas} aria-label="HP 推移のグラフ"></canvas></div>
