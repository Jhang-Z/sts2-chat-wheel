using System.Collections.Generic;

namespace VoiceRoulette.Lines;

/// <summary>
/// Pool of preset phrases the user can pick from in the settings UI.
/// Grouped into categories for browsing.
/// </summary>
public static class LineLibrary
{
    public sealed record Category(string Name, List<string> Phrases);

    public static readonly List<Category> All = new()
    {
        new("战斗", new() { "好牌！", "打精英怪！", "快攻！", "保护我！", "帮个忙" }),
        new("走位", new() { "去休息点", "继续推进", "等一下", "撤退！", "集合" }),
        new("防御", new() { "我来挡", "小心！", "注意走位" }),
        new("称赞", new() { "干得漂亮！", "GG", "太强了", "厉害！" }),
        new("状态", new() { "残血了", "缺蓝", "我满血", "我有牌" }),
        new("表情", new() { "?", "...", "笑死", "哎" }),
    };

    public static IEnumerable<string> AllPhrases()
    {
        foreach (var c in All)
            foreach (var p in c.Phrases)
                yield return p;
    }
}
