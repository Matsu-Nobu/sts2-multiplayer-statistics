// テスト時に sts2.dll が無い環境で ApiClient/HttpSender をビルド・実行できるよう、
// 参照される最小限の型をここでスタブ提供する。

namespace MegaCrit.Sts2.Core.Logging;

internal static class Log
{
    public static void Info(string msg) { /* no-op in tests */ }
    public static void Error(string msg) { /* no-op in tests */ }
}
