using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using StsStats;

namespace StsStatsBaseLib;

/// <summary>StsStats.dll の BaseLibBridge が、BaseLib が読み込まれているときだけ呼ぶ。</summary>
public static class Entry
{
    /// <summary>翻訳を読み込んだ後に呼ぶ。</summary>
    public static void Register()
    {
        var config = new StatsModConfig();
        ModConfigRegistry.Register("StsStats", config);
        config.AddTexts();
        LocManager.Instance.SubscribeToLocaleChange(config.AddTexts);
    }
}

/// <summary>
/// mod 設定画面。「セーブデータを送る」のボタンと、その説明と、送っている間と送った後の表示だけを置く。
/// 文言はユーザーの承認を得たもの (docs/spec/data-sources.md「セーブデータの履歴の取り込み」)。
/// </summary>
internal sealed class StatsModConfig : SimpleModConfig
{
    private const string RowLabel = "セーブデータ";
    private const string ButtonLabel = "セーブデータを送る";
    private const string Description =
        "セーブデータに残っているランの記録を STS2 Stats に送ります。\nmod を入れる前に遊んだランも、STS2 Stats の統計に入るようになります。";
    private const string ConfirmBody =
        "セーブデータに残っているランの記録を STS2 Stats に送ります。送るのは、勝利か死亡で終わったランのうち、STS2 Stats にまだ無いものだけです。";
    private const string ConfirmYes = "送る";
    private const string ConfirmNo = "やめる";

    // NGenericPopup は翻訳の表のキーで文言を受け取るので、表 settings_ui にこの mod の文言を足しておく (AddTexts)
    private const string LocTable = "settings_ui";
    private const string KeyPrefix = "STSSTATS-SEND_SAVE_DATA.";

    private const string ModTitle = "StS2 Multiplayer Statistics";

    private MegaRichTextLabel? _status;

    /// <summary>
    /// この mod の文言を翻訳の表 settings_ui に足す。言語を切り替えると表が読み込み直されるので、そのたびに足す。
    /// BaseLib は mod 一覧と見出しの名前を「{ModPrefix から末尾の - を除いたもの}.mod_title」で引き、無ければ名前空間の名前を出す。
    /// </summary>
    public void AddTexts()
    {
        LocManager.Instance.GetTable(LocTable).MergeWith(new Dictionary<string, string>
        {
            [ModPrefix.TrimEnd('-') + ".mod_title"] = ModTitle,
            [KeyPrefix + "header"] = ButtonLabel,
            [KeyPrefix + "body"] = ConfirmBody,
            [KeyPrefix + "yes"] = ConfirmYes,
            [KeyPrefix + "no"] = ConfirmNo,
        });
    }

    // 設定の項目を持たない設定クラスは、BaseLib の mod 一覧に出ない (ModConfig.VisibleInModList)
    public override bool VisibleInModList() => true;

    public override void SetupConfigUI(Control optionContainer)
    {
        optionContainer.AddChild(CreateButton(RowLabel, ButtonLabel, OnPressed));
        optionContainer.AddChild(Text(Description));
        _status = Text(HistoryImport.IsRunning ? "送っています…" : "");
        optionContainer.AddChild(_status);
    }

    private static MegaRichTextLabel Text(string text)
    {
        var label = CreateRawLabelControl(text, 20);
        label.FitContent = true;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    private void OnPressed()
    {
        if (HistoryImport.IsRunning) return;
        _ = ConfirmAndSend();
    }

    private async Task ConfirmAndSend()
    {
        try
        {
            var popup = NGenericPopup.Create();
            if (popup == null || NModalContainer.Instance == null) return;
            NModalContainer.Instance.Add(popup, true);
            bool yes = await popup.WaitForConfirmation(
                new LocString(LocTable, KeyPrefix + "body"), new LocString(LocTable, KeyPrefix + "header"),
                new LocString(LocTable, KeyPrefix + "no"), new LocString(LocTable, KeyPrefix + "yes"));
            if (!yes) return;

            SetStatus("送っています…");
            int sent = await HistoryImport.RunAsync((done, total) => SetStatus($"送っています… {done} / {total}"));
            SetStatus(sent == HistoryImport.Failed ? "送れませんでした。時間をおいて、もう一度押してください。"
                : sent == 0 ? "送るランはありませんでした。"
                : $"{sent} 件のランを送りました。");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] send save data error: {ex}");
            SetStatus("送れませんでした。時間をおいて、もう一度押してください。");
        }
    }

    /// <summary>送る処理は別のスレッドから呼ぶので、画面の文字はゲームのスレッドで書き換える。</summary>
    private void SetStatus(string text)
    {
        var label = _status;
        if (label == null) return;
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(label)) label.Text = text;
        }).CallDeferred();
    }
}
