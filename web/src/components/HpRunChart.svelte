<script lang="ts">
  // HP・ゴールドの推移 (全員。spec ui.md §3.1): プレイヤーごとの線をプレイヤー色で重ねる。
  // 各階にマウスを乗せると、その階の情報 (選んでいるプレイヤーの変化) を出す。点を押すとその階の詳細へ。
  import { onMount, onDestroy } from 'svelte';
  import { Chart, registerables, type ChartConfiguration } from 'chart.js';
  import { roomVisual, type FloorSummary } from '../lib/runOverview';
  Chart.register(...registerables);

  interface Series { pid: string; name: string; color: string; floors: FloorSummary[] }
  interface Props { series: Series[]; onSelect: (floor: number) => void; highlight?: string; metric?: 'hp' | 'gold'; cardName?: (id: string) => string }
  let { series, onSelect, highlight, metric = 'hp', cardName = (id) => id }: Props = $props();
  const value = (f: FloorSummary) => (metric === 'hp' ? f.hp_out : f.gold_out);

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
          data: labels.map(fl => { const f = byFloor.get(fl); return f ? value(f) : null; }),
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
                return metric === 'hp' ? `${s.name}: HP ${f.hp_out}/${f.max_hp_out}` : `${s.name}: ${f.gold_out} ゴールド`;
              },
              afterBody: (items) => details(labels[items[0].dataIndex]),
              footer: () => 'クリックで階の詳細へ',
            },
          },
        },
        scales: {
          x: { ticks: { color: '#94a3b8', autoSkip: true, maxRotation: 0 }, grid: { color: '#252a33' }, title: { display: true, text: '階', color: '#94a3b8' } },
          y: { ticks: { color: '#94a3b8' }, grid: { color: '#252a33' }, beginAtZero: true, title: { display: true, text: metric === 'hp' ? 'HP' : 'ゴールド', color: '#94a3b8' } },
        },
      },
    };
  }

  // 選んでいるプレイヤーのその階の変化 (spec ui.md §3.1)
  function details(floor: number): string[] {
    const s = series.find(x => x.pid === highlight) ?? series[0];
    const f = s?.floors.find(x => x.floor === floor);
    if (!f) return [];
    const lines: string[] = [''];
    if (series.length > 1) lines.push(`${s.name} の変化`);
    const dh = f.hp_out - f.hp_in, dg = f.gold_out - f.gold_in;
    const sg = (n: number) => (n >= 0 ? `+${n}` : `${n}`);
    lines.push(`HP ${f.hp_in} → ${f.hp_out} (${sg(dh)})、ゴールド ${f.gold_in} → ${f.gold_out} (${sg(dg)})`);
    const nm = (id: string, n?: string) => n ?? cardName(id);
    if (f.cards_obtained.length) lines.push(`カード入手: ${f.cards_obtained.map(c => nm(c.card_id, c.card_name)).join('、')}`);
    if (f.relics_obtained.length) lines.push(`レリック入手: ${f.relics_obtained.map(r => r.relic_name ?? r.relic_id).join('、')}`);
    if (f.potions_obtained.length) lines.push(`ポーション入手: ${f.potions_obtained.map(p => p.potion_name ?? p.potion_id).join('、')}`);
    if (f.shop_purchases.length) lines.push(`購入: ${f.shop_purchases.map(p => p.name || p.id).join('、')}`);
    if (f.cards_upgraded.length) lines.push(`${f.rest_options.includes('SMITH') ? '鍛冶' : 'アップグレード'}: ${f.cards_upgraded.map(c => nm(c.card_id, c.card_name)).join('、')}`);
    if (f.cards_removed.length) lines.push(`除去: ${f.cards_removed.map(c => nm(c.card_id, c.card_name)).join('、')}`);
    if (f.cards_enchanted.length) lines.push(`エンチャント: ${f.cards_enchanted.map(e => `${nm(e.card_id, e.card_name)} ← ${e.enchantment_name ?? e.enchantment_id}`).join('、')}`);
    if (f.cards_transformed.length) lines.push(`変化: ${f.cards_transformed.map(t => `${nm(t.from.card_id, t.from.card_name)} → ${nm(t.to.card_id, t.to.card_name)}`).join('、')}`);
    if (f.event_choices.length) lines.push(`イベントの選択: ${f.event_choices.map(c => c.title).join('、')}`);
    return lines;
  }

  onMount(() => { chart = new Chart(canvas, config()); });
  onDestroy(() => { chart?.destroy(); chart = null; });
  $effect(() => {
    void series; void highlight; void labels; void metric;
    if (!chart) return;
    chart.data = buildData();
    (chart.options.scales!.y as { title: { text: string } }).title.text = metric === 'hp' ? 'HP' : 'ゴールド';
    chart.options.plugins!.legend!.display = series.length > 1;
    chart.update('none');
  });
</script>

<div class="h-72 sm:h-80"><canvas bind:this={canvas} aria-label={metric === 'hp' ? 'HP の推移のグラフ' : 'ゴールドの推移のグラフ'}></canvas></div>
