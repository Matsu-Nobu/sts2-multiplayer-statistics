<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { Chart, registerables, type ChartConfiguration } from 'chart.js';
  Chart.register(...registerables);

  interface Series { label: string; values: number[]; color: string; }

  interface Props {
    labels: (string | number)[];
    series: Series[];
    type?: 'line' | 'bar';
    title?: string;
  }
  let { labels, series, type = 'line', title }: Props = $props();

  let canvas: HTMLCanvasElement;
  let chart: Chart | null = null;

  function buildConfig(): ChartConfiguration {
    return {
      type,
      data: {
        labels,
        datasets: series.map(s => ({
          label: s.label,
          data: s.values,
          borderColor: s.color,
          backgroundColor: type === 'bar' ? s.color + 'cc' : s.color + '33',
          borderWidth: 2,
          tension: 0.3,
          pointRadius: 3,
        })),
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { labels: { color: '#cbd5e1' } },
          title: title ? { display: true, text: title, color: '#cbd5e1' } : undefined,
        },
        scales: {
          x: { ticks: { color: '#94a3b8' }, grid: { color: '#252a33' } },
          y: { ticks: { color: '#94a3b8' }, grid: { color: '#252a33' }, beginAtZero: true },
        },
      },
    };
  }

  onMount(() => { chart = new Chart(canvas, buildConfig()); });
  onDestroy(() => { chart?.destroy(); });

  $effect(() => {
    // labels / series 変更時に作り直す
    if (!chart) return;
    chart.destroy();
    chart = new Chart(canvas, buildConfig());
  });
</script>

<div class="bg-bg-1 border border-bg-3 rounded-lg p-3" style="height: 280px;">
  <canvas bind:this={canvas}></canvas>
</div>
