using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>只向原版 books 追加一个根；直属八个子对象供原版导航使用。</summary>
internal static class ComputerManualUI
{
    private const string RootName = "PCREPAIR_ComputerManual";
    private static MultiPageUIManager? owner;
    private static GameObject? book;
    private static int revision = -1;
    private static bool renderedEnglish;
    private static readonly Dictionary<string, Sprite> MaterialSprites = new(StringComparer.Ordinal);

    static ComputerManualUI()
    {
        ComputerManualPages.PagesChanged += RefreshLanguage;
        LanguageText.LanguageChanged += RefreshLanguage;
    }

    internal static void Open()
    {
        try
        {
            var manager = MultiPageUIManager.Instance;
            if (manager == null) return;
            var index = Ensure(manager);
            if (index >= 0) manager.OpenUI(index);
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("打开电脑装机交易手册失败：" + ex.Message);
        }
    }

    private static bool Same(MultiPageUIManager manager) =>
        owner != null && manager != null && owner.Pointer == manager.Pointer;

    private static int FindIndex(MultiPageUIManager manager)
    {
        if (!Same(manager) || book == null || manager.books == null) return -1;
        for (var i = 0; i < manager.books.Count; i++)
            if (manager.books[i] != null && manager.books[i].Pointer == book.Pointer) return i;
        return -1;
    }

    private static int Ensure(MultiPageUIManager manager)
    {
        try { return EnsureCore(manager); }
        catch (Exception ex)
        {
            Core.Log?.Warning("电脑手册管理器尚未就绪：" + ex.Message);
            return -1;
        }
    }

    private static int EnsureCore(MultiPageUIManager manager)
    {
        if (manager == null || manager.panel == null || manager.books == null) return -1;
        if (Same(manager) && book is { } cachedBook)
        {
            var cachedPointer = cachedBook.Pointer;
            if (cachedBook == null)
            {
                // Unity 原生对象已销毁但托管包装仍存在，移除自己的死条目再重建。
                for (var dead = manager.books.Count - 1; dead >= 0; dead--)
                    if (!ReferenceEquals(manager.books[dead], null) && manager.books[dead].Pointer == cachedPointer)
                        manager.books.RemoveAt(dead);
                book = null;
            }
        }
        var index = FindIndex(manager);
        var languageRevision = ComputerManualPages.Revision * 2 + (ComputerManualPages.UseEnglish ? 1 : 0);
        if (index >= 0 && revision == languageRevision) return index;
        if (!Same(manager))
        {
            if (owner != null && book != null)
            {
                var oldIndex = FindIndex(owner);
                if (oldIndex >= 0) owner.books.RemoveAt(oldIndex);
                book.SetActive(false);
                UnityEngine.Object.Destroy(book);
            }
            // 场景中的 Unity 对象可能已经销毁；不将旧索引用于新 manager。
            owner = manager;
            book = null;
            revision = -1;
        }
        var active = index >= 0 && book != null && book.activeSelf && manager.panel.activeSelf;
        var page = Math.Clamp(manager.currentPage, 0, ComputerManualPages.PageCount - 1);
        var english = ComputerManualPages.UseEnglish;
        GameObject? replacement = null;
        try
        {
            try { replacement = BuildBook(manager, english); }
            catch (Exception ex) when (english)
            {
                Core.Log?.Warning("电脑手册英文排版无法创建，整本回退中文：" + ex.Message);
                english = false;
                replacement = BuildBook(manager, false);
            }
            var previous = book;
            if (index >= 0) manager.books[index] = replacement;
            else
            {
                manager.books.Add(replacement);
                index = manager.books.Count - 1;
            }
            book = replacement;
            renderedEnglish = english;
            revision = languageRevision;
            replacement = null;
            if (previous != null) { previous.SetActive(false); UnityEngine.Object.Destroy(previous); }
            if (active)
            {
                book.SetActive(true);
                manager.currentBookIndex = index;
                manager.currentPage = page;
                manager.ShowPage(page);
                manager.UpdateButtons();
            }
            return index;
        }
        catch (Exception ex)
        {
            if (replacement != null) UnityEngine.Object.Destroy(replacement);
            Core.Log?.Warning("注册电脑手册页面失败：" + ex.Message);
            return -1;
        }
    }

    private static GameObject BuildBook(MultiPageUIManager manager, bool english)
    {
        // Clone the vanilla book/page transforms, keeping the native header art.
        // Its first page is a 566x232 header, not the shared 628x804 paper.
        var original = FindBookTemplate(manager);
        var pageTemplate = original.transform.GetChild(0).gameObject;
        var chinese = english ? null : ChineseComputerManual.Content;
        var font = english ? FindFontTemplate(manager) : null;
        var chineseFont = english ? null : ComputerManualFont.Prepare(chinese!);
        var paper = manager.panel.GetComponent<RectTransform>();
        if (paper == null || paper.rect.width <= 0 || paper.rect.height <= 0)
            throw new InvalidOperationException("原版手册纸张尚未就绪。");
        var root = UnityEngine.Object.Instantiate(original, original.transform.parent);
        root.name = RootName;
        root.SetActive(false);
        root.transform.SetAsFirstSibling();
        try
        {
            ClearChildren(root.transform);
            for (var i = 0; i < ComputerManualPages.PageCount; i++)
            {
                var page = UnityEngine.Object.Instantiate(pageTemplate, root.transform);
                page.name = "Page" + (i + 1);
                page.SetActive(false);
                var rect = page.GetComponent<RectTransform>();
                if (rect == null || rect.rect.width <= 0 || rect.rect.height <= 0)
                    throw new InvalidOperationException("原版手册页面模板尺寸无效。");
                // Detaching before deferred Destroy keeps the native direct child count exact.
                // Destroy descendant LocBind components with their old content, never clone them into new labels.
                ClearChildren(rect);
                var decoration = page.GetComponent<Image>();
                if (decoration != null)
                {
                    if (decoration.sprite == null || !decoration.sprite.name.StartsWith("guide_logo", StringComparison.Ordinal))
                        throw new InvalidOperationException("原版燃料手册页顶装饰不匹配。");
                    decoration.raycastTarget = false;
                }
                var area = CreatePaperArea(rect, paper);
                if (english) BuildEnglishPage(area.gameObject, font!, ComputerManualPages.EnglishPage(i));
                else BuildChinesePage(area, chineseFont!, chinese!, i);
            }
            if (root.transform.childCount != ComputerManualPages.PageCount)
                throw new InvalidOperationException("电脑手册直属页面数量无效。");
            return root;
        }
        catch { root.SetActive(false); UnityEngine.Object.Destroy(root); throw; }
    }

    private static GameObject FindBookTemplate(MultiPageUIManager manager)
    {
        foreach (var original in manager.books)
            if (original != null && original.name == "BookFuel" && original.transform.childCount > 0) return original;
        throw new InvalidOperationException("原版燃料手册页面模板尚未就绪。");
    }

    private static void ClearChildren(Transform parent)
    {
        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            child.gameObject.SetActive(false);
            child.SetParent(null, false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }

    private static RectTransform CreatePaperArea(RectTransform page, RectTransform paper)
    {
        var topLeft = page.InverseTransformPoint(paper.TransformPoint(new Vector3(paper.rect.xMin, paper.rect.yMax, 0)));
        var bottomRight = page.InverseTransformPoint(paper.TransformPoint(new Vector3(paper.rect.xMax, paper.rect.yMin, 0)));
        var node = NewRect("PaperContent", page, new Vector2(bottomRight.x - topLeft.x, topLeft.y - bottomRight.y), Vector2.zero);
        var area = node.GetComponent<RectTransform>();
        area.anchorMin = area.anchorMax = area.pivot = new Vector2(0, 1);
        area.anchoredPosition = new Vector2(topLeft.x - page.rect.xMin, topLeft.y - page.rect.yMax);
        if (area.rect.width <= 0 || area.rect.height <= 0)
            throw new InvalidOperationException("原版手册纸张坐标映射失败。");
        return area;
    }

    private static void BuildChinesePage(RectTransform area, TMP_FontAsset font, ChineseComputerManual.Document content, int index)
    {
        var page = content.Pages[index];
        NativeText(area, font, "Title", content.Title, .127f, .09f, .81f, .033f, 16, "blue", true);
        NativeText(area, font, "Heading", page.Heading, .127f, .133f, .81f, .045f, 22, "body", true);
        if (index == 3) RecipeTableRules(area);
        for (var i = 0; i < page.Elements.Length; i++)
        {
            var element = page.Elements[i];
            var name = "Element" + (i + 1);
            if (element.IsIcon)
            {
                var sprite = ResolveChineseIcon(element.Id);
                if (sprite == null) throw new InvalidOperationException("中文手册配图无法加载：" + element.Id);
                var node = NewRect(name, area, Vector2.zero, Vector2.zero);
                var rect = node.GetComponent<RectTransform>();
                Place(rect, area, element.X, element.Y, element.Width, element.Height);
                // preserveAspect uses the pivot when centering narrow sprites inside the box.
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2((element.X + element.Width / 2) * area.rect.width,
                    -(element.Y + element.Height / 2) * area.rect.height);
                var image = Add<Image>(node);
                image.sprite = sprite;
                image.color = Color.white;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            else NativeText(area, font, name, element.Text, element.X, element.Y, element.Width, element.Height,
                element.FontSize, element.Style, element.Center);
        }
    }

    private static void RecipeTableRules(RectTransform area)
    {
        // Thin rules between the header and five recipe rows, matching the v2 table.
        var tops = new[] { .302f, .3635f, .4205f, .4775f, .5345f, .5915f };
        for (var i = 0; i < tops.Length; i++)
        {
            var node = NewRect("TableRule" + (i + 1), area, Vector2.zero, Vector2.zero);
            Place(node.GetComponent<RectTransform>(), area, .127f, tops[i], .823f, 1 / ChineseComputerManual.PaperHeight);
            var rule = Add<Image>(node);
            rule.color = new Color(.51f, .53f, .49f, .6f);
            rule.raycastTarget = false;
        }
    }

    private static Sprite? ResolveChineseIcon(string id)
    {
        var materialId = ChineseComputerManual.VanillaItemId(id);
        if (materialId == null)
            return SpriteAssets.Get(Components.Find(id)!.SpriteKey);
        if (MaterialSprites.TryGetValue(id, out var cached) && cached != null) return cached;
        // Native item factory supplies the correct atlas. This transient unowned data
        // template never enters a store/inventory or invokes an activation callback.
        var item = DirectoryMaster.Item(materialId, false);
        var sprite = item?.TryCast<GameItemElement>()?.ResolveItemSprite();
        if (sprite != null) MaterialSprites[id] = sprite;
        return sprite;
    }

    private static Color Ink(string style) => style switch
    {
        "blue" => new Color(.19f, .33f, .47f, 1),
        "green" => new Color(.18f, .43f, .35f, 1),
        "arrow" => new Color(.18f, .56f, .60f, 1),
        "caption" => new Color(.47f, .48f, .44f, 1),
        _ => new Color(.32f, .32f, .31f, 1)
    };

    private static void NativeText(RectTransform area, TMP_FontAsset font, string name, string value,
        float x, float y, float width, float height, float fontSize, string style, bool center)
    {
        var node = NewRect(name, area, Vector2.zero, Vector2.zero);
        var text = Add<TextMeshProUGUI>(node);
        Place(text.rectTransform, area, x, y, width, height);
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.fontSize = fontSize * area.rect.width / ChineseComputerManual.PaperWidth;
        text.fontStyle = FontStyles.Normal;
        text.color = Ink(style);
        text.enableWordWrapping = style != "arrow";
        text.enableAutoSizing = false;
        text.margin = Vector4.zero;
        text.richText = false;
        text.alignment = center ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = value;
        var size = text.rectTransform.rect.size;
        var preferred = text.GetPreferredValues(value, size.x, float.PositiveInfinity);
        if (preferred.y > size.y + 1 || preferred.x > size.x + 1)
            throw new InvalidOperationException("中文手册固定字号文字超出元素边界：" + name + "，" + value);
    }

    private static void Place(RectTransform rect, RectTransform area, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x * area.rect.width, -y * area.rect.height);
        rect.sizeDelta = new Vector2(width * area.rect.width, height * area.rect.height);
    }

    private static TextMeshProUGUI FindFontTemplate(MultiPageUIManager manager)
    {
        foreach (var originalBook in manager.books)
        {
            if (originalBook == null || originalBook.name == RootName) continue;
            foreach (var text in originalBook.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (text != null && text.font != null && text.text.Length > 50) return text;
        }
        if (manager.pageNumberText != null && manager.pageNumberText.font != null) return manager.pageNumberText;
        throw new InvalidOperationException("原版手册字体尚未就绪。");
    }

    private static void BuildEnglishPage(GameObject page, TextMeshProUGUI template, ComputerManualPages.Page content)
    {
        var area = NewRect("Content", page.transform, new Vector2(480, 580), new Vector2(9, 6));
        Text(area.transform, "Title", template, content.Title,
            new Vector2(480, 62), new Vector2(0, 255), 24, 32, true);
        var illustrated = content.Illustrations.Length > 0;
        Text(area.transform, "Body", template, content.Body,
            new Vector2(480, illustrated ? 430 : 502), new Vector2(0, illustrated ? 8 : -28), 12, 20, false);
        if (!illustrated) return;
        var width = Math.Min(100f, 460f / content.Illustrations.Length);
        for (var i = 0; i < content.Illustrations.Length; i++)
        {
            var id = content.Illustrations[i];
            var spriteKey = Components.Find(id)!.SpriteKey;
            var sprite = SpriteAssets.Get(spriteKey);
            if (sprite == null) throw new InvalidOperationException("手册配图缺失：" + id);
            var icon = NewRect("Illustration" + (i + 1), area.transform, new Vector2(width - 8, 62),
                new Vector2((i - (content.Illustrations.Length - 1) / 2f) * width, -256));
            var image = Add<Image>(icon);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
    }

    private static void Text(Transform parent, string name, TextMeshProUGUI template, string value,
        Vector2 size, Vector2 position, float minSize, float maxSize, bool title)
    {
        // 新建真实 TMP 组件，只复制字体资源，避免克隆原版本地化绑定覆盖正文。
        var node = NewRect(name, parent, size, position);
        var text = Add<TextMeshProUGUI>(node);
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.color = template.color;
        text.enableWordWrapping = true;
        text.enableAutoSizing = true;
        text.fontSize = maxSize;
        text.fontSizeMin = minSize;
        text.fontSizeMax = maxSize;
        text.richText = false;
        text.alignment = title ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.text = value;
        // 准备阶段以固定宽度测量最小字号，无法容纳时整本中文回退，避免静默裁字。
        text.enableAutoSizing = false;
        text.fontSize = minSize;
        if (text.GetPreferredValues(value, size.x, float.PositiveInfinity).y > size.y + 1)
            throw new InvalidOperationException("手册正文超出页面：" + name);
        text.enableAutoSizing = true;
        text.fontSize = maxSize;
    }

    private static T Add<T>(GameObject node) where T : Il2CppObjectBase
    {
        var type = Il2CppSystem.Type.internal_from_handle(IL2CPP.il2cpp_class_get_type(Il2CppClassPointerStore<T>.NativeClassPtr));
        return node.AddComponent(type).TryCast<T>() ?? throw new InvalidOperationException("页面组件创建失败：" + typeof(T).Name);
    }

    private static GameObject NewRect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var node = new GameObject(name);
        try
        {
            var rect = Add<RectTransform>(node);
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;
            return node;
        }
        catch { UnityEngine.Object.Destroy(node); throw; }
    }

    private static void RefreshLanguage()
    {
        if (owner == null) return;
        Ensure(owner);
    }

    [HarmonyPatch(typeof(MultiPageUIManager), nameof(MultiPageUIManager.Start))]
    internal static class StartPatch
    {
        private static void Postfix(MultiPageUIManager __instance) => Ensure(__instance);
    }

    [HarmonyPatch(typeof(MultiPageUIManager), nameof(MultiPageUIManager.ShowPage), new[] { typeof(int) })]
    internal static class PagePatch
    {
        private static void Postfix(MultiPageUIManager __instance)
        {
            if (__instance.pageNumberText == null || FindIndex(__instance) != __instance.currentBookIndex ||
                book == null || !book.activeSelf) return;
            if (!renderedEnglish)
            {
                ComputerManualFont.ObserveVisiblePage(book, __instance.currentPage);
                return;
            }
            __instance.pageNumberText.text = LanguageText.Get("manual.page.indicator", __instance.currentPage + 1, ComputerManualPages.PageCount);
        }
    }
}
