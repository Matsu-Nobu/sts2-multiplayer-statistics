using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace StsStats;

/// <summary>
/// BaseLib が読み込まれているときだけ、BaseLib 用の DLL (StsStatsBaseLib.dll) を読み込み、mod 設定画面を登録する。
/// StsStats.dll は BaseLib を参照しないので、BaseLib が無い環境でも動く。
/// </summary>
internal static class BaseLibBridge
{
    private const string AdapterDll = "StsStatsBaseLib.dll";
    private const string AdapterType = "StsStatsBaseLib.Entry";
    private static bool _done;

    /// <summary>
    /// LocManager.Initialize の後。ゲームは全部の mod を読み込んでから翻訳を読み込むので、ここなら BaseLib の有無が分かる。
    /// 設定画面の文言を翻訳の表に足すので、翻訳を読み込んだ後に呼ぶ。
    /// </summary>
    public static void LocManagerInitializePostfix()
    {
        if (_done) return;
        _done = true;
        try
        {
            if (!ModManager.GetLoadedMods().Any(m => m.manifest?.id == "BaseLib"))
            {
                Log.Info("[StsStats] BaseLib not loaded; the save data button is not shown");
                return;
            }
            var self = typeof(BaseLibBridge).Assembly;
            var context = AssemblyLoadContext.GetLoadContext(self)
                ?? throw new InvalidOperationException("no AssemblyLoadContext for StsStats");
            string path = Path.Combine(Path.GetDirectoryName(self.Location)!, AdapterDll);
            var adapter = context.LoadFromAssemblyPath(path);
            adapter.GetType(AdapterType, throwOnError: true)!
                .GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null);
            Log.Info("[StsStats] BaseLib mod config registered");
        }
        catch (Exception ex) { Log.Error($"[StsStats] BaseLib mod config failed: {ex}"); }
    }
}
