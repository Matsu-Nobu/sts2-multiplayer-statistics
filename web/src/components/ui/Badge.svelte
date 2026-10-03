<script lang="ts">
  // 状態・種類のバッジ (spec ui.md §4.2)。色だけでなく文字でも示す。
  interface Props { label: string; tone?: 'ok' | 'bad' | 'warn' | 'accent' | 'neutral'; color?: string; title?: string }
  let { label, tone = 'neutral', color, title }: Props = $props();
  const TONES = {
    ok: 'text-ok border-ok/40 bg-ok/10',
    bad: 'text-bad border-bad/40 bg-bad/10',
    warn: 'text-warn border-warn/40 bg-warn/10',
    accent: 'text-accent border-accent/40 bg-accent/10',
    neutral: 'text-slate-300 border-bg-3 bg-bg-2',
  };
</script>

{#if color}
  <span class="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium border whitespace-nowrap"
        style:color={color} style:border-color={color + '66'} style:background-color={color + '1a'} {title}>{label}</span>
{:else}
  <span class="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium border whitespace-nowrap {TONES[tone]}" {title}>{label}</span>
{/if}
