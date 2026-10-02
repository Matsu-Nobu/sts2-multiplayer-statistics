using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace StsStats;

/// <summary>
/// ダメージ補正・HP 減少補正を「補正 1 つずつ」記録する (spec combat-stats.md §3.5、貢献スコア用)。
///
/// ゲームの Hook.ModifyDamage / Hook.ModifyHpLost は「値を変えたモデルの一覧」を計算順に返す
/// (Hook.ModifyDamageInternal: 足し算 → 掛け算 → 上限。デコンパイル確認済 v0.111.0)。
/// その一覧の各モデルに同じ引数で補正計算をもう一度させ、モデルごとの増減量・倍率・上限を得る。
/// 補正計算 (Modify*) は値を返すだけの関数で、状態の変更は別の Hook (AfterModifying*) で行われる。
///
/// 記録は直後の Hook.AfterDamageGiven で、同じ対象・攻撃者のものを取り出して payload に入れる。
///
/// **記録するのは CreatureCmd.Damage の実行中だけ** (実行中のダメージ処理ごとの枠 = Scope を AsyncLocal で持つ)。
/// 敵の攻撃予告 (AttackIntent) や手札のダメージ表示 (DamageVar) も Hook.ModifyDamage を呼ぶが、
/// それらは画面更新から来るので枠の外になり、記録に混ざらない
/// (2026-10-03 の実機確認で、被ダメ側の記録が攻撃予告の計算と取り違えられていたため導入)。
/// </summary>
internal static class ModifierLog
{
    internal sealed record Step(string Phase, AbstractModel? Model, decimal Value);

    internal sealed record Record(bool IsHpLost, Creature? Target, Creature? Dealer, decimal Base, decimal Final, List<Step> Steps);

    internal sealed class Scope { public readonly List<Record> Records = new(); }

    private static readonly System.Threading.AsyncLocal<Scope?> _scope = new();

    /// <summary>CreatureCmd.Damage (全経路が通る本体) の Prefix: この処理専用の記録の枠を作る。</summary>
    public static void DamageScopePrefix(out Scope? __state)
    {
        __state = _scope.Value;
        _scope.Value = new Scope();
    }

    public static void DamageScopePostfix(Scope? __state)
    {
        _scope.Value = __state;
    }

    private static void Add(Record r)
    {
        var scope = _scope.Value;
        if (scope == null) return;   // CreatureCmd.Damage の外 (攻撃予告・カード表示等) は記録しない
        lock (scope) scope.Records.Add(r);
    }

    public static void Clear() { }

    // === 記録 =====================================================================

    /// <summary>Hook.ModifyDamage の Postfix から呼ぶ。プレビュー (カードにカーソルを乗せた時等) は記録しない。</summary>
    public static void RecordDamage(
        Creature? target, Creature? dealer, decimal damage, ValueProp props, CardModel? cardSource, CardPlay? cardPlay,
        ModifyDamageHookType hookType, CardPreviewMode previewMode, IEnumerable<AbstractModel>? modifiers, decimal result)
    {
        if (previewMode != CardPreviewMode.None || target == null) return;
        var steps = new List<Step>();
        decimal num = damage;
        try
        {
            // カードのエンチャント (Hook.ModifyDamage の冒頭で先に効く)
            var ench = cardSource?.Enchantment;
            if (ench != null)
            {
                if (hookType.HasFlag(ModifyDamageHookType.Additive))
                {
                    decimal v = ench.EnchantDamageAdditive(num, props);
                    if (v != 0m) steps.Add(new Step("enchant", ench, v));
                    num += v;
                }
                if (hookType.HasFlag(ModifyDamageHookType.Multiplicative))
                {
                    decimal f = ench.EnchantDamageMultiplicative(num, props);
                    if (f != 1m) steps.Add(new Step("enchant", ench, num * f - num));
                    num *= f;
                }
            }

            var models = (modifiers ?? Enumerable.Empty<AbstractModel>()).Distinct().ToList();
            if (hookType.HasFlag(ModifyDamageHookType.Additive))
            {
                foreach (var m in models)
                {
                    decimal v = m.ModifyDamageAdditive(target, num, props, dealer, cardSource, cardPlay);
                    if (v != 0m) steps.Add(new Step("additive", m, v));
                    num += v;
                }
            }
            if (hookType.HasFlag(ModifyDamageHookType.Multiplicative))
            {
                foreach (var m in models)
                {
                    decimal f = m.ModifyDamageMultiplicative(target, num, props, dealer, cardSource, cardPlay);
                    if (f != 1m) steps.Add(new Step("multiplicative", m, f));
                    num *= f;
                }
            }
            if (hookType.HasFlag(ModifyDamageHookType.Cap))
            {
                decimal minCap = decimal.MaxValue;
                foreach (var m in models)
                {
                    decimal c = m.ModifyDamageCap(target, props, dealer, cardSource, cardPlay);
                    if (c < minCap)
                    {
                        minCap = c;
                        if (num > c) { steps.Add(new Step("cap", m, c - num)); num = c; }
                    }
                }
            }
        }
        catch (Exception ex) { Log.Error($"[StsStats] ModifierLog.RecordDamage error: {ex.Message}"); }

        // 再計算がゲームの結果とずれたら、その差は誰のものとも言えないので攻撃した本人に付ける (web)
        if (Math.Abs(num - result) > 0.01m) steps.Add(new Step("other", null, result - num));
        Add(new Record(false, target, dealer, damage, result, steps));
    }

    /// <summary>Hook.ModifyHpLost の Postfix から呼ぶ。プレイヤーが受ける分だけ記録 (rMit 用)。</summary>
    public static void RecordHpLost(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource,
        HpLossHookPhase phases, IEnumerable<AbstractModel>? modifiers, decimal result)
    {
        if (target?.Player == null) return;
        var steps = new List<Step>();
        decimal num = amount;
        try
        {
            var models = (modifiers ?? Enumerable.Empty<AbstractModel>()).Distinct().ToList();
            void Phase(Func<AbstractModel, decimal, decimal> f)
            {
                foreach (var m in models)
                {
                    decimal next = f(m, num);
                    if (next != num) steps.Add(new Step("hp_lost", m, next - num));
                    num = next;
                }
            }
            if (phases.HasFlag(HpLossHookPhase.BeforeOsty))
            {
                Phase((m, n) => m.ModifyHpLostBeforeOsty(target, n, props, dealer, cardSource));
                Phase((m, n) => m.ModifyHpLostBeforeOstyLate(target, n, props, dealer, cardSource));
            }
            if (phases.HasFlag(HpLossHookPhase.AfterOsty))
            {
                Phase((m, n) => m.ModifyHpLostAfterOsty(target, n, props, dealer, cardSource));
                Phase((m, n) => m.ModifyHpLostAfterOstyLate(target, n, props, dealer, cardSource));
            }
        }
        catch (Exception ex) { Log.Error($"[StsStats] ModifierLog.RecordHpLost error: {ex.Message}"); }
        if (Math.Abs(num - result) > 0.01m) steps.Add(new Step("hp_lost", null, result - num));
        if (steps.Count > 0) Add(new Record(true, target, dealer, amount, result, steps));
    }

    // === 取り出し =====================================================================

    /// <summary>
    /// AfterDamageGiven で呼ぶ。今のダメージ処理の枠の中で、同じ対象・攻撃者について **最初に** 記録された
    /// ダメージ補正 (CreatureCmd.Damage は対象ごとに最初に本物の補正計算をする) と、その後の
    /// <paramref name="hpTarget"/> の HP 減少補正を返す。枠の中身は次の対象のために空にする。
    /// </summary>
    public static (Record? Damage, List<Record> HpLost) Take(Creature? target, Creature? dealer, Creature? hpTarget)
    {
        var scope = _scope.Value;
        if (scope == null) return (null, new List<Record>());
        lock (scope)
        {
            var recs = scope.Records;
            int idx = recs.FindIndex(r => !r.IsHpLost && ReferenceEquals(r.Target, target) && ReferenceEquals(r.Dealer, dealer));
            var dmg = idx >= 0 ? recs[idx] : null;
            var hp = new List<Record>();
            if (hpTarget != null)
                for (int i = Math.Max(idx, 0); i < recs.Count; i++)
                    if (recs[i].IsHpLost && ReferenceEquals(recs[i].Target, hpTarget)) hp.Add(recs[i]);
            recs.Clear();
            return (dmg, hp);
        }
    }

    // === payload ====================================================================

    /// <summary>api.md「modifications」の形。</summary>
    public static object? ToPayload(Record? damage, List<Record>? hpLost = null)
    {
        if (damage == null && (hpLost == null || hpLost.Count == 0)) return null;
        var steps = new List<object>();
        if (damage != null) steps.AddRange(damage.Steps.Select(StepPayload));
        if (hpLost != null) foreach (var r in hpLost) steps.AddRange(r.Steps.Select(StepPayload));
        return new
        {
            @base = damage?.Base,
            final = damage?.Final,
            steps,
        };
    }

    private static object StepPayload(Step s)
    {
        var m = s.Model;
        string? kind = m switch
        {
            null             => null,
            PowerModel       => "power",
            RelicModel       => "relic",
            EnchantmentModel => "enchantment",
            OrbModel         => "orb",
            PotionModel      => "potion",
            CardModel        => "card",
            _                => "other",
        };
        string? name = null;
        try
        {
            name = m switch
            {
                PowerModel p       => PowerNameResolver.Resolve(p),
                RelicModel r       => ModelInfo.Text(r.Title),
                EnchantmentModel e => ModelInfo.Text(e.Title),
                CardModel c        => ModelInfo.SafeTitle(c),
                _                  => null,
            };
        }
        catch { }
        return new
        {
            phase      = s.Phase,
            model_id   = m?.Id.Entry,
            model_name = name,
            kind,
            value      = s.Value,
            appliers   = AppliersOf(m),
            owner      = OwnerOf(m),
        };
    }

    /// <summary>パワーの付与者ごとのスタック数 (全パワー)。内訳が無ければパワーが覚えている付与者。</summary>
    private static List<object>? AppliersOf(AbstractModel? m)
    {
        if (m is not PowerModel p) return null;
        try
        {
            var list = PowerOriginRegistry.LookupAll(p.Owner, p.Id.Entry)
                .Where(a => a.Stacks > 0)
                .OrderByDescending(a => a.Stacks)
                .Select(a => (object)new { player_id = a.Applier, stacks = a.Stacks })
                .ToList();
            if (list.Count > 0) return list;
            string? applier = Identity.OfCreature(p.Applier, includePets: true);
            if (applier != null) return new List<object> { new { player_id = applier, stacks = Math.Max(1, Math.Abs(p.Amount)) } };
        }
        catch { }
        return null;
    }

    private static string? OwnerOf(AbstractModel? m)
    {
        try
        {
            return m switch
            {
                PowerModel p       => Identity.OfCreature(p.Owner, includePets: true),
                RelicModel r       => r.Owner != null ? Identity.Of(r.Owner) : null,
                EnchantmentModel e => e.Card?.Owner != null ? Identity.Of(e.Card.Owner) : null,
                CardModel c        => c.Owner != null ? Identity.Of(c.Owner) : null,
                _                  => null,
            };
        }
        catch { return null; }
    }
}
