// ゲーム更新時の検証ツール。使い方:
//   verify-game-api <STS2_DATA_DIR> <StsStats.dll> <mod/src/ModEntry.cs> <members.txt>
// 1) ModEntry の patch 一覧表 (P(...) の行) について、当て先が sts2.dll に存在し、
//    prefix / postfix の引数が Harmony で結び付くか (名前・型) を確認する。
// 2) members.txt に書いた「名前で参照しているメンバー」が存在するかを確認する。
// 失敗が 1 件でもあれば終了コード 1。
using System.Reflection;
using System.Text.RegularExpressions;

if (args.Length < 4) { Console.Error.WriteLine("usage: verify-game-api <STS2_DATA_DIR> <StsStats.dll> <ModEntry.cs> <members.txt>"); return 2; }
string dataDir = args[0], modDll = args[1], modEntry = args[2], membersFile = args[3];

var paths = Directory.GetFiles(dataDir, "*.dll").ToList();
var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
paths.AddRange(Directory.GetFiles(runtimeDir, "*.dll").Where(p => !paths.Any(x => Path.GetFileName(x) == Path.GetFileName(p))));
paths.Add(Path.GetFullPath(modDll));
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths));
var game = mlc.LoadFromAssemblyPath(Path.Combine(dataDir, "sts2.dll"));
var mod  = mlc.LoadFromAssemblyPath(Path.GetFullPath(modDll));
var gameTypes = game.GetTypes();

const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
int fail = 0, checkedCount = 0;
void Fail(string s) { fail++; Console.WriteLine("  NG  " + s); }
void Ok(string s)   { Console.WriteLine("  ok  " + s); }

Type? GameType(string simple)
{
    var hits = gameTypes.Where(t => t.Name == simple).ToList();
    return hits.Count == 1 ? hits[0] : null;
}

// Harmony (AccessTools.Method) と同じく、派生クラスから順に探して最初に見つかった型のメソッドだけを候補にする
List<MethodInfo> MethodsInHierarchy(Type t, string name)
{
    for (var x = t; x != null; x = x.BaseType)
    {
        var hits = x.GetMethods(ALL).Where(m => m.Name == name).ToList();
        if (hits.Count > 0) return hits;
    }
    return new();
}

string CsName(string n) => n switch
{
    "bool" => "Boolean", "int" => "Int32", "long" => "Int64", "ulong" => "UInt64", "string" => "String",
    "decimal" => "Decimal", "float" => "Single", "double" => "Double", "object" => "Object", _ => n,
};

Type Elem(Type t) => t.IsByRef ? t.GetElementType()! : t;

bool Compatible(Type patchParam, Type original)
{
    var p = Elem(patchParam); var o = Elem(original);
    if (p.FullName == "System.Object") return true;
    if (p.IsAssignableFrom(o)) return true;
    var under = Nullable.GetUnderlyingType(p);
    if (under != null && under.FullName == o.FullName) return true;
    return p.FullName == o.FullName;
}

// === 1) patch 一覧表 ===========================================================
Console.WriteLine("== patch 一覧表 (ModEntry.PatchTable)");
var rx = new Regex(@"^\s*P\(typeof\((\w+)\),\s*""(\w+)"",\s*typeof\((\w+)\),\s*(null|""(\w+)""),\s*(null|""(\w+)"")((?:,\s*typeof\([\w.<>]+\))*)\);", RegexOptions.Multiline);
foreach (Match m in rx.Matches(File.ReadAllText(modEntry)))
{
    checkedCount++;
    string targetName = m.Groups[1].Value, method = m.Groups[2].Value, patchCls = m.Groups[3].Value;
    string? prefix = m.Groups[5].Success ? m.Groups[5].Value : null;
    string? postfix = m.Groups[7].Success ? m.Groups[7].Value : null;
    var argTypes = Regex.Matches(m.Groups[8].Value, @"typeof\(([\w.<>]+)\)").Select(x => x.Groups[1].Value).ToList();
    string label = $"{targetName}.{method}";

    var target = GameType(targetName);
    if (target == null) { Fail($"{label}: 型 {targetName} が見つからない (または同名が複数)"); continue; }
    var candidates = MethodsInHierarchy(target, method);
    if (argTypes.Count > 0)
        candidates = candidates.Where(c => c.GetParameters().Select(p => p.ParameterType.Name.Split('`')[0]).SequenceEqual(argTypes.Select(a => CsName(a.Split('<')[0].Split('.').Last())))).ToList();
    if (candidates.Count == 0) { Fail($"{label}: メソッドが見つからない"); continue; }
    if (candidates.Count > 1) { Fail($"{label}: 同名のメソッドが {candidates.Count} 個 (引数の型を指定すること)"); continue; }
    var original = candidates[0];

    var cls = mod.GetType("StsStats." + patchCls);
    if (cls == null) { Fail($"{label}: patch のクラス {patchCls} が mod に無い"); continue; }
    bool okAll = true;
    foreach (var pname in new[] { prefix, postfix }.Where(x => x != null))
    {
        var pm = cls.GetMethods(ALL).Where(x => x.Name == pname).ToList();
        if (pm.Count != 1) { Fail($"{label}: {patchCls}.{pname} が見つからない"); okAll = false; continue; }
        foreach (var p in pm[0].GetParameters())
        {
            if (p.Name == "__instance")
            {
                if (original.IsStatic) { Fail($"{label}: static メソッドに __instance"); okAll = false; }
                else if (!Compatible(p.ParameterType, original.DeclaringType!)) { Fail($"{label}: __instance の型 {p.ParameterType.Name} が {original.DeclaringType!.Name} と合わない"); okAll = false; }
                continue;
            }
            if (p.Name == "__result")
            {
                if (!Compatible(p.ParameterType, original.ReturnType)) { Fail($"{label}: __result の型 {p.ParameterType.Name} が戻り値 {original.ReturnType.Name} と合わない"); okAll = false; }
                continue;
            }
            if (p.Name!.StartsWith("__")) continue;
            var op = original.GetParameters().FirstOrDefault(x => x.Name == p.Name);
            if (op == null) { Fail($"{label}: {pname} の引数 '{p.Name}' が元のメソッドに無い (元: {string.Join(", ", original.GetParameters().Select(x => x.Name))})"); okAll = false; continue; }
            if (!Compatible(p.ParameterType, op.ParameterType)) { Fail($"{label}: 引数 '{p.Name}' の型 {p.ParameterType.Name} が元の {op.ParameterType.Name} と合わない"); okAll = false; }
        }
    }
    if (okAll) Ok(label);
}
if (checkedCount == 0) Fail("patch 一覧表の行が 1 件も読めなかった (ModEntry.PatchTable の書式を確認)");

// === 2) 名前で参照しているメンバー =================================================
Console.WriteLine("== 名前で参照しているメンバー (members.txt)");
foreach (var raw in File.ReadAllLines(membersFile))
{
    var line = raw.Trim();
    if (line == "" || line.StartsWith("#")) continue;
    var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    string kind = p[0], typeName = p[1], member = p.Length > 2 ? p[2] : "";
    bool pubOnly = p.Length > 3 && p[3] == "pub";
    var t = game.GetType(typeName);
    if (t == null) { Fail($"{typeName}: 型が無い"); continue; }
    var flags = pubOnly ? (BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly) : ALL;
    bool Has(Func<Type, bool> f) { for (var x = t; x != null; x = x.BaseType) if (f(x)) return true; return false; }
    bool found = kind switch
    {
        "type"   => true,
        "prop"   => Has(x => x.GetProperty(member, flags) != null),
        "field"  => Has(x => x.GetField(member, flags) != null),
        "method" => Has(x => x.GetMethods(flags).Any(mm => mm.Name == member)),
        "enum"   => t.IsEnum && t.GetEnumNames().Contains(member),
        _        => false,
    };
    if (found) Ok($"{kind} {typeName} {member}"); else Fail($"{kind} {typeName} {member}: 見つからない");
}

// === 3) 出どころの自動追跡で選ばれるメソッド (SourceContext.SelectTargets) ====================
// ゲームと同じ選び方を本物の sts2.dll に対して実行する (読み込むだけで実行はしない)。
// 毒など「カード効果からも呼ばれる処理」に目印が付くことを確認する (docs/redesign-v2.md §2.4-2)。
Console.WriteLine("== 出どころの自動追跡 (SourceContext.SelectTargets)");
try
{
    System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
    {
        foreach (var dir in new[] { dataDir, Path.GetDirectoryName(Path.GetFullPath(modDll))! })
        {
            var p = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
        }
        return null;
    };
    var liveGame = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dataDir, "sts2.dll"));
    var liveMod  = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(modDll));
    var select = liveMod.GetType("StsStats.SourceContext")!.GetMethod("SelectTargets", BindingFlags.Static | BindingFlags.NonPublic)!;
    var selected = ((System.Collections.IList)select.Invoke(null, new object[] { liveGame })!).Cast<MethodInfo>().ToList();
    var direct = selected.Where(x => x.IsStatic || x.GetBaseDefinition().DeclaringType == x.DeclaringType)
                         .Select(x => $"{x.DeclaringType!.Name}.{x.Name}{(x.IsStatic ? "(static)" : "")}").OrderBy(x => x).ToList();
    Console.WriteLine($"  選ばれたメソッド {selected.Count} 個、うち Hook の上書きではないもの {direct.Count} 個: {string.Join(", ", direct)}");
    // 必ず入っていてほしいもの (これが外れたら、毒・Doom 等の帰属が壊れる)
    foreach (var must in new[] { "PoisonPower.Trigger", "PoisonPower.AfterSideTurnStart", "DoomPower.DoomKill", "LightningOrb.Evoke", "ThornsPower.BeforeDamageReceived",
        // カードを渡さずに毒を付ける処理 (付けた持ち物を実行中のモデルから求める。api.md「origin」)
        "PoisonPotion.OnUse", "NoxiousFumesPower.AfterSideTurnStart", "EnvenomPower.AfterDamageGiven", "CorrosiveWavePower.AfterCardDrawn", "ConcoctPower.AfterDamageGiven", "TwistedFunnel.BeforeSideTurnStart",
        "PlatingPower.BeforeSideTurnEndEarly" })
    {
        if (selected.Any(x => $"{x.DeclaringType!.Name}.{x.Name}" == must)) Ok($"選ばれている: {must}");
        else Fail($"選ばれていない: {must} (ゲーム側の実装が変わった可能性。デコンパイルで確認すること)");
    }
    // 「ゲームのイベントへの反応」か「カードの効果が直接発動させる処理」かの判定 (api.md「triggered_by」)
    var isHook = liveMod.GetType("StsStats.SourceContext")!.GetMethod("IsHook", BindingFlags.Static | BindingFlags.NonPublic)!;
    foreach (var (name, expectHook) in new[] {
        ("PoisonPower.AfterSideTurnStart", true), ("ThornsPower.BeforeDamageReceived", true), ("NoxiousFumesPower.AfterSideTurnStart", true),
        ("PoisonPower.Trigger", false), ("LightningOrb.Evoke", false), ("LightningOrb.Passive", false), ("DoomPower.DoomKill", false) })
    {
        var m = selected.FirstOrDefault(x => $"{x.DeclaringType!.Name}.{x.Name}" == name);
        if (m == null) { Fail($"見つからない: {name}"); continue; }
        bool got = (bool)isHook.Invoke(null, new object[] { m })!;
        if (got == expectHook) Ok($"{name} は{(expectHook ? "イベントへの反応" : "直接発動させる処理")}");
        else Fail($"{name} の判定が逆 ({(got ? "反応" : "直接")} と判定)");
    }
}
catch (Exception ex) { Fail($"SourceContext.SelectTargets を実行できなかった: {ex.GetBaseException().Message}"); }

Console.WriteLine(fail == 0 ? "\n結果: すべて OK" : $"\n結果: {fail} 件の問題");
return fail == 0 ? 0 : 1;
