using System.Collections.Generic;

namespace VoiceRoulette.Lines;

// Preset phrases the user can pick from in the settings 语音库 tab.
// Each entry has a default emotion that gets applied when the phrase is
// dropped into a wheel slot — user can still tweak emotion afterwards.
public static class LineLibrary
{
    public sealed record Entry(string Text, string Category, string? DefaultEmotion);

    // Categories: 全部 / 战斗 / 撤退 / 赞美 / 提示 / 危险 / 自嘲
    public static readonly Entry[] All =
    {
        // 战斗
        new("进攻！",        "战斗", "angry"),
        new("防御！",        "战斗", "novel_dialog"),
        new("我来挡",        "战斗", "novel_dialog"),
        new("集合！",        "战斗", "novel_dialog"),
        new("打精英怪！",    "战斗", "novel_dialog"),
        new("快攻！",        "战斗", "angry"),
        new("继续推进",      "战斗", "novel_dialog"),
        new("保护我！",      "战斗", "sorry"),

        // 撤退
        new("撤退！",        "撤退", "angry"),
        new("快走！",        "撤退", "angry"),
        new("需要休息",      "撤退", "sad"),
        new("去休息点",      "撤退", null),
        new("等一下",        "撤退", null),

        // 赞美
        new("好牌！",        "赞美", "happy"),
        new("干得漂亮！",    "赞美", "happy"),
        new("太强了！",      "赞美", "happy"),
        new("厉害！",        "赞美", "happy"),
        new("GG",            "赞美", "happy"),
        new("谢谢",          "赞美", "happy"),

        // 提示
        new("小心！",        "提示", "novel_dialog"),
        new("注意走位",      "提示", "novel_dialog"),
        new("敌人在哪？",    "提示", null),
        new("别慌，有我在！","提示", "novel_dialog"),
        new("重开一下",      "提示", null),

        // 危险
        new("危险！",        "危险", "angry"),
        new("注意陷阱！",    "危险", "angry"),
        new("我要挂了，救救我", "危险", "sorry"),
        new("残血了",        "危险", "sad"),

        // 自嘲
        new("我没有输出了",  "自嘲", "sad"),
        new("我有啥招呢",    "自嘲", "sorry"),
        new("那没办法",      "自嘲", "sad"),
        new("你打的什么东西", "自嘲", "angry"),
        new("？",            "自嘲", null),
        new("...",           "自嘲", null),
        new("笑死",          "自嘲", "happy"),
        new("哎",            "自嘲", "sad"),
    };

    public static IEnumerable<string> Categories
    {
        get
        {
            yield return "全部";
            yield return "战斗";
            yield return "撤退";
            yield return "赞美";
            yield return "提示";
            yield return "危险";
            yield return "自嘲";
        }
    }
}
