using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
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
/// そこで **呼ばれる側に目印を付ける**: 起動時に、上記モデルのクラスが宣言している全メソッド
/// (Hook の上書きに限らない。static も含む) のうち、本体でダメージ・ブロック・撃破・パワー付与・
/// エナジー獲得のコマンドを呼ぶものを IL から自動で選び、「実行中モデルを AsyncLocal に積む / 戻す」だけの
/// Prefix / Postfix を当てる。
///
/// これで、例えば毒の本体 PoisonPower.Trigger は、ターン開始時に発動しても、カード効果 (Outbreak) から
/// 発動しても、今後追加される何かから発動しても、同じ「毒の処理中」になる。呼ぶ側 (カード等) は個別に扱わない。
///
/// static メソッド (DoomPower.DoomKill 等) はインスタンスが無いので型だけを積み、どのパワーかは
/// ダメージを受けた creature に付いている同じ型のパワーで決める。
///
/// AsyncLocal は Prefix で積んだ値が非同期メソッドの継続 (await の後) にも引き継がれ、
/// Postfix で戻した値は呼び出し元にだけ効く。目印が重なったら一番内側 (最後に積んだもの) が有効。
/// </summary>
internal static class SourceContext
{
    internal sealed record Frame(AbstractModel? Instance, Type ModelType);

    private static readonly AsyncLocal<Frame?> _current = new();

    public static Frame? Current => _current.Value;

    public static void Prefix(AbstractModel __instance, out Frame? __state)
    {
        __state = _current.Value;
        _current.Value = new Frame(__instance, __instance.GetType());
    }

    public static void PrefixStatic(MethodBase __originalMethod, out Frame? __state)
    {
        __state = _current.Value;
        _current.Value = new Frame(null, __originalMethod.DeclaringType!);
    }

    public static void Postfix(Frame? __state)
    {
        _current.Value = __state;
    }

    // === 実行中モデルの解決 ===================================================

    /// <summary>
    /// 実行中のモデル。static メソッドで型しか無いときは、<paramref name="target"/> (ダメージ等を受けた creature) に
    /// 付いている同じ型のパワーを返す。
    /// </summary>
    public static AbstractModel? ResolveModel(Creature? target)
    {
        var f = _current.Value;
        if (f == null) return null;
        if (f.Instance != null) return f.Instance;
        if (target != null && typeof(PowerModel).IsAssignableFrom(f.ModelType))
        {
            try { return target.Powers.FirstOrDefault(p => p.GetType() == f.ModelType); }
            catch { return null; }
        }
        return null;
    }

    /// <summary>出どころの種類 (payload の source_kind)。カード以外は小文字のモデル種別。</summary>
    public static string CurrentKind()
    {
        var t = _current.Value?.ModelType;
        if (t == null) return "unknown";
        if (typeof(PowerModel).IsAssignableFrom(t))       return "power";
        if (typeof(RelicModel).IsAssignableFrom(t))       return "relic";
        if (typeof(OrbModel).IsAssignableFrom(t))         return "orb";
        if (typeof(EnchantmentModel).IsAssignableFrom(t)) return "enchantment";
        if (typeof(PotionModel).IsAssignableFrom(t))      return "potion";
        return "other";
    }

    /// <summary>カードが無いダメージ・ブロックの出どころとして使う情報。</summary>
    public static CardInfo? CurrentInfo(Creature? target)
    {
        var f = _current.Value;
        if (f == null) return null;
        try
        {
            var m = ResolveModel(target);
            string kind = CurrentKind();
            kind = char.ToUpperInvariant(kind[0]) + kind.Substring(1);   // "Power" / "Relic" … (source_card_type)
            if (m == null)
            {
                // 型しか分からない (static メソッドで、対象にそのパワーが無い)。クラス名から ID を作る
                string id = Regex.Replace(f.ModelType.Name, "(?<=[a-z0-9])(?=[A-Z])", "_").ToUpperInvariant();
                return new CardInfo(id, id, kind);
            }
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
    /// 出どころが「<paramref name="target"/> 自身に付いているパワー」(毒・Doom・絞殺など相手に付けたデバフ) なら、
    /// その付与者ごとのスタック数 (多い順)。そうでなければ null。
    /// web は与ダメージ・カード別の表・rDPS をこの比で按分する (spec combat-stats.md §4)。
    /// </summary>
    public static List<(string PlayerId, int Stacks)>? DebuffAppliers(Creature? target)
    {
        if (target == null) return null;
        try
        {
            if (ResolveModel(target) is not PowerModel p || !ReferenceEquals(p.Owner, target)) return null;
            var appliers = PowerOriginRegistry.LookupAll(target, p.Id.Entry)
                .Where(a => a.Stacks > 0)
                .OrderByDescending(a => a.Stacks)
                .Select(a => (a.Applier, a.Stacks))
                .ToList();
            if (appliers.Count > 0) return appliers;
            // 内訳が無い (付与を観測できなかった) ときは、パワーが覚えている付与者に全量
            string? applier = Identity.OfCreature(p.Applier, includePets: true);
            return applier != null ? new List<(string, int)> { (applier, Math.Max(1, p.Amount)) } : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 実行中モデルが「誰の行為か」。相手に付けたデバフは付与者 (最大スタックの人)、
    /// 自分のパワーは付与者か持ち主、レリック・オーブ・ポーションは持ち主、エンチャントはカードの持ち主。
    /// </summary>
    public static string? CurrentActorPlayerId(Creature? target)
    {
        var debuff = DebuffAppliers(target);
        if (debuff != null && debuff.Count > 0) return debuff[0].PlayerId;
        try
        {
            switch (ResolveModel(target))
            {
                case PowerModel p:       return Identity.OfCreature(p.Applier, includePets: true) ?? Identity.OfCreature(p.Owner, includePets: true);
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

    internal static readonly Type[] RootTypes =
    {
        typeof(PowerModel), typeof(RelicModel), typeof(OrbModel), typeof(EnchantmentModel), typeof(PotionModel),
    };

    /// <summary>本体でこれらを呼ぶメソッドだけを追跡対象にする (Commands / Commands.Builders 名前空間)。</summary>
    private static readonly HashSet<string> TargetCommandNames = new()
    {
        "Damage", "Attack", "GainBlock", "Kill", "Apply", "GainEnergy",
    };

    /// <summary>
    /// 目印を付けるメソッドを選ぶ。ゲームの起動前に検証ツール (tools/verify-game-api) からも同じ処理を呼ぶ。
    /// 対象: RootTypes の派生クラス (具象・抽象とも。RootTypes 自身は除く) が宣言している、インスタンス / static の
    /// 全メソッドのうち、本体で対象コマンドを呼ぶもの。
    /// </summary>
    internal static List<MethodInfo> SelectTargets(Assembly gameAssembly)
    {
        var result = new List<MethodInfo>();
        Type[] types;
        try { types = gameAssembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

        foreach (var type in types)
        {
            if (type.ContainsGenericParameters || RootTypes.Contains(type)) continue;
            if (!RootTypes.Any(r => r.IsAssignableFrom(type))) continue;
            if (type.Namespace != null && type.Namespace.Contains(".Mock")) continue;

            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.IsAbstract || m.ContainsGenericParameters || m.IsSpecialName) continue;
                bool calls;
                try { calls = CallsTargetCommand(m, type, 0, new HashSet<MethodBase>()); }
                catch { calls = false; }
                if (calls) result.Add(m);
            }
        }
        return result;
    }

    public static void AutoPatch(Harmony harmony)
    {
        var sw = Stopwatch.StartNew();
        var prefix       = new HarmonyMethod(AccessTools.Method(typeof(SourceContext), nameof(Prefix)));
        var prefixStatic = new HarmonyMethod(AccessTools.Method(typeof(SourceContext), nameof(PrefixStatic)));
        var postfix      = new HarmonyMethod(AccessTools.Method(typeof(SourceContext), nameof(Postfix)));
        var targets = SelectTargets(typeof(AbstractModel).Assembly);
        int patched = 0, failed = 0;
        var perRoot = RootTypes.ToDictionary(t => t.Name, _ => 0);
        var direct = new List<string>();   // Hook の上書きではない (外から呼ばれる) もの。ログで確認できるように

        foreach (var m in targets)
        {
            var owner = m.DeclaringType!;
            try
            {
                harmony.Patch(m, prefix: m.IsStatic ? prefixStatic : prefix, postfix: postfix);
                patched++;
                perRoot[RootTypes.First(r => r.IsAssignableFrom(owner)).Name]++;
                bool isOverride = !m.IsStatic && m.GetBaseDefinition().DeclaringType != owner;
                if (!isOverride) direct.Add($"{owner.Name}.{m.Name}{(m.IsStatic ? "(static)" : "")}");
            }
            catch (Exception ex)
            {
                failed++;
                Log.Error($"[StsStats] SourceContext patch failed: {owner.Name}.{m.Name}: {ex.Message}");
            }
        }
        sw.Stop();
        Log.Info($"[StsStats] SourceContext: patched {patched} ({string.Join(", ", perRoot.Select(kv => $"{kv.Key}={kv.Value}"))}), failed {failed}, {sw.ElapsedMilliseconds} ms");
        Log.Info($"[StsStats] SourceContext: 外から呼ばれる処理 {direct.Count} 件: {string.Join(", ", direct)}");
    }

    /// <summary>
    /// メソッド本体 (非同期なら状態機械の MoveNext) の IL に、対象コマンドの呼び出しがあるか。
    /// 同じクラス (と、その入れ子の状態機械・ラムダ) のヘルパーは 3 段まで辿る。
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
