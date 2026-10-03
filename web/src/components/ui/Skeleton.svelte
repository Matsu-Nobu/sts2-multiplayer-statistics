<script lang="ts">
  // 読み込み中 (spec ui.md §4.2): 画面の骨格の形。
</script>

<div class="animate-pulse" aria-busy="true" aria-label="読み込み中">
  <div class="border-b border-bg-3 bg-bg-1">
    <div class="max-w-7xl mx-auto px-4 py-4 space-y-2">
      <div class="h-6 w-56 bg-bg-3 rounded"></div>
      <div class="h-4 w-80 max-w-full bg-bg-2 rounded"></div>
      <div class="h-8 w-64 bg-bg-2 rounded mt-3"></div>
    </div>
  </div>
  <div class="max-w-7xl mx-auto px-4 py-6 space-y-4">
    <div class="h-64 bg-bg-1 border border-bg-3 rounded-lg"></div>
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-4">
      <div class="h-80 bg-bg-1 border border-bg-3 rounded-lg"></div>
      <div class="h-80 bg-bg-1 border border-bg-3 rounded-lg"></div>
      <div class="h-80 bg-bg-1 border border-bg-3 rounded-lg"></div>
    </div>
  </div>
</div>
