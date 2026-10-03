<script lang="ts" generics="T extends string">
  // 同じ画面内の表示の切り替え (spec ui.md §4.2)。画面の移動には使わない。
  interface Props { options: { value: T; label: string }[]; value: T; onChange: (v: T) => void; label?: string }
  let { options, value, onChange, label }: Props = $props();
</script>

<div class="inline-flex bg-bg-2 border border-bg-3 rounded-md p-0.5 text-sm" role="group" aria-label={label}>
  {#each options as o (o.value)}
    <button type="button" aria-pressed={o.value === value}
            class="px-3 py-1 rounded focus-ring {o.value === value ? 'bg-bg-3 text-slate-100 font-medium' : 'text-slate-400 hover:text-slate-200'}"
            onclick={() => onChange(o.value)}>{o.label}</button>
  {/each}
</div>
