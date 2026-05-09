<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { Chart, registerables, type ChartConfiguration } from 'chart.js';
  import { roomVisual, type FloorSummary } from '../lib/runOverview';
  Chart.register(...registerables);

  interface Props {
    floors: FloorSummary[];
    selectedFloor: number | null;
    onSelect: (floor: number) => void;
    cardNames?: Record<string, string>;
  }
  let { floors, selectedFloor, onSelect, cardNames = {} }: Props = $props();

  function nameCard(id: string): string { return cardNames[id] ?? id; }

  let canvas: HTMLCanvasElement;
  let chart: Chart | null = null;

  function buildConfig(): ChartConfiguration {
    const labels = floors.map(f => f.floor);
    const hp = floors.map(f => f.hp_in);
    const colors = floors.map(f => roomVisual(f.room_type).color);
    const radii = floors.map(f => selectedFloor === f.floor ? 7 : 4);

    return {
      type: 'line',
      data: {
        labels,
        datasets: [
          {
            label: 'HP',
            data: hp,
            borderColor: '#7aa2f7',
            backgroundColor: '#7aa2f733',
            pointBackgroundColor: colors,
            pointBorderColor: colors,
            pointRadius: radii,
            pointHoverRadius: 9,
            tension: 0.2,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        animations: { colors: false, x: false, y: false },
        transitions: { active: { animation: { duration: 0 } } },
        interaction: { mode: 'index', intersect: false },
        onClick: (_evt, elements) => {
          if (elements.length === 0) return;
          const idx = elements[0].index;
          const f = floors[idx];
          if (f) onSelect(f.floor);
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              title: (items) => {
                const idx = items[0].dataIndex;
                const f = floors[idx];
                if (!f) return '';
                const v = roomVisual(f.room_type);
                return `階 ${f.floor} ${v.emoji} ${v.label}` + (f.encounter_name ? ` — ${f.encounter_name}` : '');
              },
              label: (item) => {
                const idx = item.dataIndex;
                const f = floors[idx];
                if (!f) return '';
                const delta = f.hp_out - f.hp_in;
                return `HP ${f.hp_in}/${f.max_hp_in} → ${f.hp_out}/${f.max_hp_out} (${delta >= 0 ? '+' : ''}${delta})`;
              },
              afterBody: (items) => {
                const idx = items[0].dataIndex;
                const f = floors[idx];
                if (!f) return [];
                const lines: string[] = [];
                const goldDelta = f.gold_out - f.gold_in;
                lines.push(`ゴールド ${f.gold_in} → ${f.gold_out} (${goldDelta >= 0 ? '+' : ''}${goldDelta})`);
                if (f.cards_obtained.length > 0)
                  lines.push(`カード入手: ${f.cards_obtained.map(c => c.card_name ?? nameCard(c.card_id)).join(', ')}`);
                if (f.cards_upgraded.length > 0)
                  lines.push(`アップグレード: ${f.cards_upgraded.map(c => c.card_name ?? nameCard(c.card_id)).join(', ')}`);
                if (f.relics_obtained.length > 0)
                  lines.push(`レリック入手: ${f.relics_obtained.map(r => r.relic_id).join(', ')}`);
                if (f.potions_obtained.length > 0)
                  lines.push(`ポーション入手: ${f.potions_obtained.map(p => p.potion_id).join(', ')}`);
                if (f.cards_removed.length > 0)
                  lines.push(`カード除去: ${f.cards_removed.map(c => c.card_name ?? nameCard(c.card_id)).join(', ')}`);
                if (f.cards_enchanted && f.cards_enchanted.length > 0)
                  lines.push(`エンチャ: ${f.cards_enchanted.map(e => `${e.card_name ?? nameCard(e.card_id)}←${e.enchantment_id}`).join(', ')}`);
                if (f.event_choices && f.event_choices.length > 0)
                  lines.push(`イベント選択: ${f.event_choices.map(c => c.title || c.history_name || c.text_key).join(', ')}`);
                return lines;
              },
            },
          },
        },
        scales: {
          x: { ticks: { color: '#94a3b8' }, grid: { color: '#252a33' } },
          y: { ticks: { color: '#94a3b8' }, grid: { color: '#252a33' }, beginAtZero: true },
        },
      },
    };
  }

  onMount(() => { chart = new Chart(canvas, buildConfig()); });
  onDestroy(() => { chart?.destroy(); chart = null; });

  // floors / selectedFloor の変化を確実に検知するため、長さ・最終 hp・選択を依存に明示する
  $effect(() => {
    void floors.length;
    void floors.map(f => f.hp_in).join(',');
    void selectedFloor;
    if (!chart) return;
    const cfg = buildConfig();
    chart.data = cfg.data;
    chart.update('none');   // アニメーションなしで再描画
  });
</script>

<div class="bg-bg-1 border border-bg-3 rounded-lg p-3" style="height: 320px;">
  <canvas bind:this={canvas}></canvas>
</div>
