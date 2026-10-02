/** チップのホバー説明 (カタログの説明文を HTML に)。spec run-overview.md §2.6。 */
import { renderCardDescription, type CatalogLookup } from './catalog';

export const cardTip = (cat: CatalogLookup | null, id: string, upgraded: boolean): string => {
  const c = cat?.card(id, upgraded);
  return c ? renderCardDescription(c.description) : '';
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
