using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace VoiceRoulette.Input;

// Cmd+Click (Mac) / Ctrl+Click (Win/Linux) on a potion or power broadcasts
//   - "我有 能量药水"
//   - "我处于 力量2 的状态"
// so teammates know your current state.
public sealed partial class StatusPinger : Node
{
    private SceneTree? _tree;
    private Action<string>? _onPing;
    private bool _previousLeftPressed;
    private bool _previousRightPressed;
    private bool _firedThisRightPress;

    public void Start(Action<string> onPing)
    {
        _tree = (SceneTree)Engine.GetMainLoop();
        _onPing = onPing;
        _tree.ProcessFrame += OnTick;
        GD.Print("[VR][Pinger] Start — Cmd/Ctrl+Click potion/power, Right-click card");
    }

    private void OnTick()
    {
        if (_tree == null || _onPing == null) return;

        // Branch 1: Right-click — announce the card under cursor.
        // Also watch the mouse-button MASK in addition to IsMouseButtonPressed
        // since the game may consume the click before polling sees it.
        var rightPressed = Godot.Input.IsMouseButtonPressed(MouseButton.Right);
        var maskRight = ((int)Godot.Input.GetMouseButtonMask() & (int)MouseButtonMask.Right) != 0;
        var anyRight = rightPressed || maskRight;
        var rightEdge = anyRight && !_previousRightPressed;
        _previousRightPressed = anyRight;

        if (rightEdge)
        {
            var clickPos = _tree.Root.GetMousePosition();
            GD.Print($"[VR][Pinger] right-click EDGE at {clickPos}");
            var (card, debugInfo) = FindCardUnderCursorVerbose(_tree.Root, clickPos);
            GD.Print($"[VR][Pinger] scanned {debugInfo}");
            if (card != null)
            {
                var cardName = ExtractCardTitle(card);
                if (!string.IsNullOrWhiteSpace(cardName))
                {
                    var cardMsg = $"我准备打出【{cardName}】";
                    GD.Print($"[VR][Pinger] card ping: {cardMsg}");
                    _onPing(cardMsg);
                }
                else
                {
                    GD.Print($"[VR][Pinger] card matched ({card.GetType().Name}) but no title label");
                }
            }
        }

        // Branch 2: Cmd/Ctrl+left-click — announce potion/power.
        var modifierHeld =
            Godot.Input.IsKeyPressed(Key.Meta) ||
            Godot.Input.IsKeyPressed(Key.Ctrl);

        var leftPressed = Godot.Input.IsMouseButtonPressed(MouseButton.Left);
        var clicked = leftPressed && !_previousLeftPressed && modifierHeld;
        _previousLeftPressed = leftPressed;

        if (!clicked) return;

        var mousePos = _tree.Root.GetMousePosition();
        var hit = FindStatusUnderCursor(_tree.Root, mousePos);
        if (hit == null)
        {
            GD.Print("[VR][Pinger] no potion/power under cursor");
            return;
        }

        var typeName = hit.GetType().Name;
        var isPotion = typeName.Contains("Potion");
        var isPower = typeName.Contains("Power");
        if (!isPotion && !isPower) return;

        // Stage 1 (PREFERRED): reflect into NPotion._potion / NPower._power and
        // call its GetTitle() / GetName() method to get the localized name
        // directly. This avoids any popup-visibility race conditions across
        // rapid clicks on different potions/powers.
        var name = TryReflectedTitle(hit);
        // Stage 2: try local CJK labels inside the clicked node.
        if (string.IsNullOrWhiteSpace(name))
            name = TryLocalCjkLabel(hit);
        // Stage 3: as a last resort scan visible tooltips (closest to cursor).
        if (string.IsNullOrWhiteSpace(name))
            name = FindVisibleTooltipTitle(_tree.Root, mousePos);
        if (string.IsNullOrWhiteSpace(name)) name = "未知";

        // For powers, extract the stack count from a digit-only child label.
        var stackCount = isPower ? FindStackCount(hit) : null;

        string message = isPotion
            ? $"我有【{name}】"
            : !string.IsNullOrEmpty(stackCount)
                ? $"我处于【{name}{stackCount}】的状态"
                : $"我处于【{name}】状态";

        GD.Print($"[VR][Pinger] ping: {message}  (type={typeName}, stack={stackCount ?? "-"})");
        _onPing(message);
    }

    // -------------------------------------------------------------------------
    // Hit detection
    // -------------------------------------------------------------------------

    // Wrapper/holder words to exclude when matching Potion/Power. We want the
    // actual icon node (NPotion / NPower), not its container.
    private static readonly string[] StatusWrapperWords =
    {
        "Holder", "Container", "Popup", "Button", "Inventory", "Holster",
        "Lab", "Category", "Shortcut",
    };

    private static bool IsStatusIconTypeName(string name)
    {
        var matchesPotion = name.Contains("Potion") || name.Contains("Power");
        if (!matchesPotion) return false;
        foreach (var w in StatusWrapperWords)
            if (name.Contains(w)) return false;
        // Skip common Power-named utility classes (PowerContainer is filtered above; this catches PowerVfx, PowerFlash etc.)
        if (name.EndsWith("Vfx") || name.EndsWith("Flash") || name.EndsWith("Applied") || name.EndsWith("Removed")) return false;
        return true;
    }

    private static Node? FindStatusUnderCursor(Node root, Vector2 mousePos)
    {
        // Pick the CLOSEST visible Potion/Power icon to the cursor, not just any
        // within a fixed tolerance. Top-bar potions are tightly packed (~50px apart)
        // so a fixed-tolerance "first match wins" picks wrong node when icons overlap.
        Node? best = null;
        float bestDist = float.MaxValue;
        const float MaxDist = 70f;

        Walk(root);
        return best;

        void Walk(Node n)
        {
            var typeName = n.GetType().Name;
            if (IsStatusIconTypeName(typeName))
            {
                float? dist = null;
                if (n is Control ctrl && ctrl.IsVisibleInTree())
                {
                    var rect = ctrl.GetGlobalRect();
                    if (rect.HasPoint(mousePos)) dist = 0f;
                    else
                    {
                        // Distance from rect to mouse
                        var dx = MathF.Max(0, MathF.Max(rect.Position.X - mousePos.X, mousePos.X - rect.End.X));
                        var dy = MathF.Max(0, MathF.Max(rect.Position.Y - mousePos.Y, mousePos.Y - rect.End.Y));
                        dist = MathF.Sqrt(dx * dx + dy * dy);
                    }
                }
                else if (n is Node2D n2d && n2d.IsVisibleInTree())
                {
                    dist = (n2d.GlobalPosition - mousePos).Length();
                }

                if (dist.HasValue && dist.Value <= MaxDist && dist.Value < bestDist)
                {
                    bestDist = dist.Value;
                    best = n;
                }
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
    }

    // -------------------------------------------------------------------------
    // Stage 1: local label scan inside the clicked node
    // -------------------------------------------------------------------------

    private static string? TryLocalCjkLabel(Node node)
    {
        var bag = new List<string>();
        CollectLabelsRecursive(node, bag, 0);
        return PickBestNameLabel(bag);
    }

    /// <summary>
    /// Read the localized title directly from the Potion/Power data object via
    /// reflection. STS2's Potion / Power model classes have GetTitle() and
    /// GetName() methods that return the user-facing name. This is the most
    /// reliable extraction path because it doesn't depend on popup visibility.
    /// </summary>
    private static string? TryReflectedTitle(Node node)
    {
        var t = node.GetType();

        // Look for the data-holding field/property: _potion, _power, Potion, Power, etc.
        var dataNames = new[] { "_potion", "_power", "Potion", "Power", "_data", "Data", "_def", "Def" };
        object? data = null;
        foreach (var name in dataNames)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            data = f?.GetValue(node);
            if (data == null)
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                data = p?.GetValue(node);
            }
            if (data != null) break;
        }
        if (data == null) return null;

        var dt = data.GetType();

        // Preferred: parameterless methods like GetTitle / GetName.
        foreach (var methodName in new[] { "GetTitle", "GetName", "GetDisplayName", "GetLocalizedName" })
        {
            try
            {
                var m = dt.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null, types: Type.EmptyTypes, modifiers: null);
                if (m != null && m.ReturnType == typeof(string))
                {
                    var result = m.Invoke(data, null) as string;
                    if (!string.IsNullOrWhiteSpace(result)) return CleanLabelText(result);
                }
            }
            catch { /* skip — try next */ }
        }

        // Fallback: properties.
        foreach (var prop in new[] { "Title", "Name", "DisplayName", "LocalizedName" })
        {
            var p = dt.GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            if (p?.PropertyType == typeof(string))
            {
                try
                {
                    var s = p.GetValue(data) as string;
                    if (!string.IsNullOrWhiteSpace(s)) return CleanLabelText(s);
                }
                catch { /* skip */ }
            }
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Stage 2: search visible HoverTip / Tooltip / Popup containers globally
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Card hit detection (used by single right-click)
    // -------------------------------------------------------------------------

    // Card-like type names but excluding obvious wrappers/holders.
    private static readonly string[] CardWrapperWords =
    {
        "Holder", "Highlight", "Container", "Bundle", "Library", "Pile",
        "Selection", "Choose", "Reward", "View", "Inspect", "Filter",
        "Tickbox", "Trail", "Fly", "Transform", "Glow", "Smith", "Enchant",
        "Upgrade", "Layout", "Cost", "Vfx", "Preview", "Removal", "Hand",
    };

    private static bool IsCardLikeTypeName(string name)
    {
        if (!name.StartsWith("N") || !name.Contains("Card")) return false;
        foreach (var w in CardWrapperWords)
            if (name.Contains(w)) return false;
        return true;
    }

    /// <summary>
    /// Walk the scene tree finding any card-like node (NCard, NMerchantCard, etc.)
    /// whose bounds contain the cursor. We pick the closest match to handle
    /// overlapping hand cards. Returns a debug summary for logging.
    /// </summary>
    private static (Node? card, string debug) FindCardUnderCursorVerbose(Node root, Vector2 mousePos)
    {
        // Permissive search: hovered cards in STS2 lift up ~80px AND scale up.
        // We collect all visible cards with their rough bounds, then pick the
        // closest to cursor (allowing up to 200px slop). This works whether the
        // card is in normal hand position or in lifted-hover state.
        const float MaxDistance = 200f;
        Node? best = null;
        float bestDistance = float.MaxValue;
        int totalCards = 0;
        int visibleCards = 0;
        int candidates = 0;
        Vector2 closestSeen = Vector2.Zero;

        Walk(root);
        return (best, $"{totalCards} card-like nodes total, {visibleCards} visible, {candidates} within {MaxDistance}px (closest at {closestSeen})");

        void Walk(Node n)
        {
            var typeName = n.GetType().Name;
            if (IsCardLikeTypeName(typeName))
            {
                totalCards++;
                Vector2 origin = Vector2.Zero;
                Vector2 halfSize = Vector2.Zero;
                bool visible = false;
                if (n is Control ctrl)
                {
                    visible = ctrl.IsVisibleInTree();
                    var rect = ctrl.GetGlobalRect();
                    origin = rect.Position + rect.Size / 2f;
                    halfSize = rect.Size / 2f;
                }
                else if (n is Node2D n2d)
                {
                    visible = n2d.IsVisibleInTree();
                    origin = n2d.GlobalPosition;
                    halfSize = new Vector2(70f, 95f) * n2d.GlobalScale;
                }
                if (visible)
                {
                    visibleCards++;
                    var dx = MathF.Max(0, MathF.Abs(mousePos.X - origin.X) - halfSize.X);
                    var dy = MathF.Max(0, MathF.Abs(mousePos.Y - origin.Y) - halfSize.Y);
                    var dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist < bestDistance)
                    {
                        // track closest seen even if outside threshold
                        bestDistance = dist;
                        closestSeen = origin;
                        if (dist <= MaxDistance)
                        {
                            candidates++;
                            best = n;
                        }
                    }
                }
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
    }

    /// <summary>
    /// Extract the card's name from its child labels: pick the topmost CJK
    /// label, skipping numeric-only ones (cost) and very long ones (description).
    /// </summary>
    private static string? ExtractCardTitle(Node card)
    {
        var labelsWithY = new List<(string text, float y)>();
        CollectLabelsWithY(card, labelsWithY, 0);

        string? best = null;
        float bestY = float.MaxValue;
        foreach (var (text, y) in labelsWithY)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^-?\d+$")) continue;
            if (IsBlacklistedActionLabel(text)) continue;
            if (!HasMultipleCjk(text)) continue;
            if (text.Length > 12) continue;
            if (y < bestY) { bestY = y; best = text; }
        }
        return best;
    }

    /// <summary>
    /// Find the topmost CJK label inside any visible CardPreview / InspectCard
    /// container — that's the card title shown when right-clicking a card.
    /// </summary>
    private static string? FindVisibleCardTitle(Node root)
    {
        string? bestTitle = null;
        float bestY = float.MaxValue;
        Walk(root);
        return bestTitle;

        void Walk(Node n)
        {
            var typeName = n.GetType().Name;
            // Strict match: only card-preview-style containers, not generic Popup
            // (which would falsely match potion popups).
            var isCardContainer = typeName.Contains("CardPreview")
                                  || typeName.Contains("InspectCard");

            if (isCardContainer && IsVisible(n))
            {
                var labelsWithY = new List<(string text, float y)>();
                CollectLabelsWithY(n, labelsWithY, 0);
                foreach (var (text, y) in labelsWithY)
                {
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (!HasMultipleCjk(text)) continue;
                    if (text.Length > 12) continue;  // skip descriptions
                    if (y < bestY) { bestY = y; bestTitle = text; }
                }
            }

            foreach (var c in n.GetChildren()) Walk(c);
        }
    }

    // Common UI action / button labels that show up inside popups but are
    // never the actual title we want.
    private static readonly HashSet<string> UiActionBlacklist = new()
    {
        // Potion / item actions
        "饮用", "丢弃", "扔出", "投掷", "使用", "吃下", "戴上", "穿戴",
        "装备", "拾取", "放下",
        // Generic UI actions
        "取消", "确认", "关闭", "升级", "选择", "跳过",
        "保留", "移除", "确定", "返回", "继续", "下一步", "上一步",
        "进入", "离开", "结束", "开始", "重试", "重置", "保存",
        // Card type words (often appear at the top of card popups but aren't titles)
        "攻击", "技能", "能力", "状态", "诅咒",
        // Shop / event
        "查看", "购买", "出售", "学习", "锻造", "净化",
        "更换", "应用",
    };

    private static bool IsBlacklistedActionLabel(string text)
    {
        return UiActionBlacklist.Contains(text);
    }

    /// <summary>
    /// Find the title from the visible HoverTip / Tooltip / Popup container
    /// CLOSEST to the cursor. This filters out lingering popups from a previous
    /// click whose visibility hasn't been cleared yet.
    /// Within the chosen container, returns the topmost (smallest Y) CJK label
    /// that isn't a blacklisted UI action word.
    /// </summary>
    private static string? FindVisibleTooltipTitle(Node root, Vector2 cursorPos)
    {
        // Phase 1: collect all visible tooltip containers with their center positions
        var containers = new List<(Node container, Vector2 center)>();
        Collect(root);

        if (containers.Count == 0) return null;

        // Phase 2: sort by distance to cursor; try each in order until we find a valid title
        containers.Sort((a, b) =>
            a.center.DistanceSquaredTo(cursorPos)
                .CompareTo(b.center.DistanceSquaredTo(cursorPos)));

        foreach (var (container, _) in containers)
        {
            var labelsWithY = new List<(string text, float y)>();
            CollectLabelsWithY(container, labelsWithY, 0);

            string? best = null;
            float bestY = float.MaxValue;
            foreach (var (text, y) in labelsWithY)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!HasMultipleCjk(text)) continue;
                if (text.Length > 12) continue;
                if (IsBlacklistedActionLabel(text)) continue;
                if (y < bestY) { bestY = y; best = text; }
            }
            if (!string.IsNullOrWhiteSpace(best)) return best;
        }
        return null;

        void Collect(Node n)
        {
            var typeName = n.GetType().Name;
            var isContainer = typeName.Contains("HoverTip")
                              || typeName.Contains("Tooltip")
                              || typeName.Contains("Popup");

            if (isContainer && IsVisible(n))
            {
                Vector2 center = Vector2.Zero;
                if (n is Control ctrl)
                {
                    var rect = ctrl.GetGlobalRect();
                    center = rect.Position + rect.Size / 2f;
                }
                else if (n is Node2D n2d)
                {
                    center = n2d.GlobalPosition;
                }
                containers.Add((n, center));
            }

            foreach (var c in n.GetChildren()) Collect(c);
        }
    }

    private static void CollectLabelsWithY(Node node, List<(string, float)> bag, int depth)
    {
        if (depth > 6) return;
        foreach (var child in node.GetChildren())
        {
            string? raw = null;
            Vector2? pos = null;
            if (child is Label lbl && lbl.IsVisibleInTree())
            {
                raw = lbl.Text;
                pos = lbl.GlobalPosition;
            }
            else if (child is RichTextLabel rtl && rtl.IsVisibleInTree())
            {
                raw = rtl.Text;
                pos = rtl.GlobalPosition;
            }
            if (!string.IsNullOrWhiteSpace(raw) && pos.HasValue)
            {
                var clean = CleanLabelText(raw!);
                if (!string.IsNullOrWhiteSpace(clean))
                    bag.Add((clean, pos.Value.Y));
            }
            CollectLabelsWithY(child, bag, depth + 1);
        }
    }

    private static bool IsVisible(Node n)
    {
        // CanvasItem-derived nodes have IsVisibleInTree(); fall back to true otherwise.
        if (n is CanvasItem ci) return ci.IsVisibleInTree();
        return true;
    }

    // -------------------------------------------------------------------------
    // Stack count extraction (for Powers like 力量2)
    // -------------------------------------------------------------------------

    private static string? FindStackCount(Node node)
    {
        var bag = new List<string>();
        CollectLabelsRecursive(node, bag, 0);
        foreach (var s in bag)
        {
            // Match pure-digit (or with sign) labels: "2", "10", "-1"
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d+$"))
                return s;
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Label collection helpers
    // -------------------------------------------------------------------------

    private static void CollectLabelsRecursive(Node node, List<string> bag, int depth)
    {
        if (depth > 6) return;
        foreach (var child in node.GetChildren())
        {
            string? raw = null;
            if (child is Label lbl) raw = lbl.Text;
            else if (child is RichTextLabel rtl) raw = rtl.Text;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var clean = CleanLabelText(raw!);
                if (!string.IsNullOrWhiteSpace(clean)) bag.Add(clean);
            }
            CollectLabelsRecursive(child, bag, depth + 1);
        }
    }

    private static string? PickBestNameLabel(List<string> candidates)
    {
        string? best = null;
        int bestScore = -1;
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            if (c.Length < 2) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(c, @"^-?\d+$")) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(c, @"^[A-Za-z0-9 \-+]+$")) continue;
            if (IsBlacklistedActionLabel(c)) continue;

            int chineseCount = 0;
            foreach (var ch in c)
                if (ch >= 0x4E00 && ch <= 0x9FFF) chineseCount++;
            if (chineseCount == 0) continue;

            var score = chineseCount * 10 + Math.Min(c.Length, 12);
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    private static bool HasMultipleCjk(string s)
    {
        int count = 0;
        foreach (var ch in s)
            if (ch >= 0x4E00 && ch <= 0x9FFF && ++count >= 2) return true;
        return false;
    }

    private static string CleanLabelText(string s)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(s, @"\[/?[^\]]+\]", "");
        clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
        if (clean.Length > 16) clean = clean[..16];
        return clean;
    }
}
