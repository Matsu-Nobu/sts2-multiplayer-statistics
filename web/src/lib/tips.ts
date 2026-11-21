/** チップのホバー説明 (カタログの説明文を HTML に)。spec run-overview.md「chip ホバー tooltip (description)」。 */
import { renderCardDescription, type CatalogLookup } from './catalog';

/** カードの説明。エンチャントが付いていれば、その名前と説明を区切り線の下に足す。 */
export const cardTip = (cat: CatalogLookup | null, id: string, upgraded: boolean, enchantmentId?: string | null): string => {
  const c = cat?.card(id, upgraded);
  if (!c) return '';
  const e = enchantmentId ? cat?.enchantment(enchantmentId) : null;
  const ench = e
    ? `<span class="block mt-1.5 pt-1.5 border-t border-bg-3"><span class="text-purple-300">${e.name}</span><br>${renderCardDescription(e.description)}</span>`
    : '';
  return renderCardDescription(c.description) + ench;
};
export const relicTip = (cat: CatalogLookup | null, id: string): string => {
  const r = cat?.relic(id);
  return r ? renderCardDescription(r.description) : '';
};
export const potionTip = (cat: CatalogLookup | null, id: string): string => {
  const p = cat?.potion(id);
  return p ? renderCardDescription(p.description) : '';
};
export const enchantmentTip = (cat: CatalogLookup | null, id: string): string => {
  const e = cat?.enchantment(id);
  return e ? renderCardDescription(e.description) : '';
};
