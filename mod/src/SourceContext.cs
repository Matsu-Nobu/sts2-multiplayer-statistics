using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace StsStats;

/// <summary>
/// 「いま何のモデル (パワー・レリック・オーブ・エンチャント・ポーション) の処理が走っているか」を追跡する
/// (docs/redesign-v2.md §2.4-2)。
///
/// ゲームには共通の「実行中モデル」の記録が無い (PlayerChoiceContext.PushModel は一部の Hook だけ)。
/// そこで起動時に、上記モデルの全具象クラスが上書きしている非同期メソッドのうち、本体で
/// ダメージ・ブロック・撃破・パワー付与・エナジー獲得のコマンドを呼ぶものを IL から自動で選び、
/// 「実行中モデルを AsyncLocal に積む / 戻す」だけの Prefix / Postfix を当てる。
///
/// AsyncLocal は Prefix で積んだ値が非同期メソッドの継続 (await の後) にも引き継がれ、
/// Postfix で戻した値は呼び出し元 (Hook のループ) にだけ効く。
/// </summary>
internal static class SourceContext
{
    private static readonly AsyncLocal<AbstractModel?> _current = new();

    public static AbstractModel? Current => _current.Value;

    public static void Prefix(AbstractModel __instance, out AbstractModel? __state)
    {
        __state = _current.Value;
        _current.Value = __instance;
    }

    public static void Postfix(AbstractModel? __state)
    {
        _current.Value = __state;
    }

    /// <summary>出どころの種類 (payload の source_kind)。カード以外は小文字のモデル種別。</summary>
    public static string CurrentKind() => _current.Value switch
    {
        PowerModel       => "power",
        RelicModel       => "relic",
        OrbModel         => "orb",
        EnchantmentModel => "enchantment",
        PotionModel      => "potion",
        null             => "unknown",
        _                => "other",
    };

    /// <summary>カードが無いダメージ・ブロックの出どころとして使う情報。</summary>
    public static CardInfo? CurrentInfo()
    {
        var m = _current.Value;
        if (m == null) return null;
        try
        {
            string kind = m switch
            {
                PowerModel       => "Power",
                RelicModel       => "Relic",
                OrbModel         => "Orb",
                EnchantmentModel => "Enchantment",
                PotionModel      => "Potion",
                _                => m.GetType().Name,
            };
            string name = m switch
            {
                PowerModel p       => PowerNameResolver.Resolve(p) ?? p.Id.Entry,
                RelicModel r       => ModelInfo.Text(r.Title),
                PotionModel po     => ModelInfo.Text(po.Title),
                EnchantmentModel e => ModelInfo.Text(e.Title),
                _                  => m.Id.Entry,
            };
            return new CardInfo(m.Id.Entry, string.IsNullOrEmpty(name) ? m.Id.Entry : name, kind);
        }
        catch { return null; }
    }

    /// <summary>
    /// 実行中モデルが「誰の行為か」。パワーは付与者 (毒は付与したプレイヤー)、
    /// レリック・オーブ・ポーションは持ち主、エンチャントはカードの持ち主。
    /// </summary>
    public static string? CurrentActorPlayerId()
    {
        var m = _current.Value;
        if (m == null) return null;
        try
        {
            switch (m)
            {
                case PowerModel p:
                {
                    // 複数人が重ねたパワーは stacks が最大の人 (詳細な按分は web が active_on_target で行う)
                    var appliers = PowerOriginRegistry.LookupAll(p.Owner, p.Id.Entry);
                    if (appliers.Count > 0) return appliers.OrderByDescending(a => a.Stacks).First().Applier;
                    return Identity.OfCreature(p.Applier, includePets: true) ?? Identity.OfCreature(p.Owner, includePets: true);
                }
                case RelicModel r:       return OwnerId(r.Owner);
                case OrbModel o:         return OwnerId(o.Owner);
                case PotionModel po:     return OwnerId(po.Owner);
                case EnchantmentModel e: return OwnerId(e.Card?.Owner);
            }
        }
        catch { }
        return null;
    }

    private static string? OwnerId(Player? p) => p != null ? Identity.Of(p) : null;

    // === 自動 patch ==============================================================

    private static readonly Type[] RootTypes =
    {
        typeof(PowerModel), typeof(RelicModel), typeof(OrbModel), typeof(EnchantmentModel), typeof(PotionModel),
    };

    /// <summary>本体でこれらを呼ぶメソッドだけを追跡対象にする (Commands / Commands.Builders 名前空間)。</summary>
    private static readonly HashSet<string> TargetCommandNames = new()
    {
        "Damage", "Attack", "GainBlock", "Kill", "Apply", "GainEnergy",
    };

    public static void AutoPatch(Harmony harmony)
    {
        var sw = Stopwatch.StartNew();
        var prefix  = new HarmonyMethod(AccessTools.Method(typeof(SourceContext), nameof(Prefix)));
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(SourceContext), nameof(Postfix)));
        int scanned = 0, patched = 0, failed = 0;
        var perRoot = RootTypes.ToDictionary(t => t.Name, _ => 0);

        foreach (var type in typeof(AbstractModel).Assembly.GetTypes())
        {
            if (type.IsAbstract || type.ContainsGenericParameters) continue;
            var root = RootTypes.FirstOrDefault(r => r.IsAssignableFrom(type));
            if (root == null) continue;
            if (type.Namespace != null && type.Namespace.Contains(".Mock")) continue;

            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.IsAbstract || m.ContainsGenericParameters) continue;
                if (!typeof(Task).IsAssignableFrom(m.ReturnType)) continue;
                if (m.GetBaseDefinition().DeclaringType == type) continue;   // 上書き (override) のみ
                scanned++;
                bool calls;
                try { calls = CallsTargetCommand(m, type, 0, new HashSet<MethodBase>()); }
                catch { calls = false; }
                if (!calls) continue;
                try
                {
                    harmony.Patch(m, prefix: prefix, postfix: postfix);
                    patched++;
                    perRoot[root.Name]++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error($"[StsStats] SourceContext patch failed: {type.Name}.{m.Name}: {ex.Message}");
                }
            }
        }
        sw.Stop();
        Log.Info($"[StsStats] SourceContext: scanned {scanned} overrides, patched {patched} ({string.Join(", ", perRoot.Select(kv => $"{kv.Key}={kv.Value}"))}), failed {failed}, {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>
    /// メソッド本体 (非同期なら状態機械の MoveNext) の IL に、対象コマンドの呼び出しがあるか。
    /// 同じクラス内のヘルパー (DoomPower.DoomKill 等) は 3 段まで辿る。
    /// </summary>
    private static bool CallsTargetCommand(MethodBase method, Type owner, int depth, HashSet<MethodBase> visited)
    {
        if (depth > 3 || !visited.Add(method)) return false;
        var body = method;
        var sm = method.GetCustomAttribute<AsyncStateMachineAttribute>();
        if (sm != null)
        {
            var moveNext = sm.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (moveNext != null) body = moveNext;
        }
        foreach (var ins in PatchProcessor.GetOriginalInstructions(body))
        {
            if (ins.operand is not MethodBase called) continue;
            var dt = called.DeclaringType;
            if (dt == null) continue;
            if (dt.Namespace != null && dt.Namespace.StartsWith("MegaCrit.Sts2.Core.Commands") && TargetCommandNames.Contains(called.Name))
                return true;
            // 同じクラス (と、その入れ子の状態機械・ラムダ) のヘルパーを辿る
            if (dt == owner || dt.DeclaringType == owner || (dt.IsAssignableFrom(owner) && dt != typeof(AbstractModel) && !RootTypes.Contains(dt)))
            {
                if (CallsTargetCommand(called, owner, depth + 1, visited)) return true;
            }
        }
        return false;
    }
}
