#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TMPro;
using TMPro.EditorUtilities;
using System.IO;

public static class ChineseFontSetup
{
    // Common Chinese characters (GB2312 Level 1 subset ~3500 chars) + ASCII + punctuation
    // This covers 99%+ of daily Chinese usage
    private const string CommonChineseChars =
        // ASCII printable
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
        // Chinese punctuation
        "\uff0c\u3002\uff01\uff1f\u3001\uff1b\uff1a\u201c\u201d\u2018\u2019\uff08\uff09\u3010\u3011\u300a\u300b\u2014\u2026\u00b7" +
        // UI-specific characters used in our project
        "输入角色名字请确认连接主机退出背包为空关闭内陆湖玩家地图选择开始游戏" +
        // Common Chinese characters (high frequency subset)
        "的一是不了人我在有他这中大来上个国到说们为子和你地出会也时要就可以对生能而行方后多日都三小军二无同么经法当起与好看学进种将还分此心前面又定见只主没公从";

    [MenuItem("Tools/Setup Chinese Font (从系统字体生成)")]
    public static void SetupChineseFont()
    {
        // Try to find Microsoft YaHei on Windows
        string fontPath = null;
        string[] candidates = new[]
        {
            "C:/Windows/Fonts/msyh.ttc",    // Microsoft YaHei
            "C:/Windows/Fonts/msyhbd.ttc",   // Microsoft YaHei Bold
            "C:/Windows/Fonts/simsun.ttc",   // SimSun
            "C:/Windows/Fonts/simhei.ttf",   // SimHei
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                fontPath = path;
                break;
            }
        }

        if (fontPath == null)
        {
            EditorUtility.DisplayDialog("Chinese Font Setup",
                "未找到系统中文字体。请手动将 .ttf/.otf 中文字体文件放入 Assets/Fonts/ 目录，\n" +
                "然后使用 Window → TextMeshPro → Font Asset Creator 生成 SDF 字体。",
                "OK");
            return;
        }

        // Copy font to project
        string destDir = "Assets/Fonts";
        if (!AssetDatabase.IsValidFolder(destDir))
            AssetDatabase.CreateFolder("Assets", "Fonts");

        string fontFileName = Path.GetFileName(fontPath).Replace(".ttc", ".ttf");
        string destPath = $"{destDir}/{fontFileName}";

        if (!File.Exists(destPath))
        {
            File.Copy(fontPath, destPath, true);
            AssetDatabase.Refresh();
        }

        // Load the font
        var font = AssetDatabase.LoadAssetAtPath<Font>(destPath);
        if (font == null)
        {
            Debug.LogError($"[ChineseFontSetup] 无法加载字体: {destPath}");
            return;
        }

        EditorUtility.DisplayDialog("Chinese Font Setup",
            $"已将系统字体复制到 {destPath}。\n\n" +
            "接下来请手动操作：\n" +
            "1. Window → TextMeshPro → Font Asset Creator\n" +
            "2. Source Font File: 选择 Assets/Fonts/" + fontFileName + "\n" +
            "3. Atlas Resolution: 4096 x 4096\n" +
            "4. Character Set: Custom Characters\n" +
            "5. 粘贴下面的字符（已复制到剪贴板）\n" +
            "6. 点击 Generate Font Atlas → Save As: Assets/Fonts/ChineseFont SDF.asset\n" +
            "7. 然后在 Edit → Project Settings → TextMeshPro → Settings\n" +
            "   把生成的字体设为 Default Font Asset 或加入 Fallback Font Assets",
            "OK");

        // Copy characters to clipboard
        GUIUtility.systemCopyBuffer = CommonChineseChars;
        Debug.Log($"[ChineseFontSetup] 字体已复制到 {destPath}，常用中文字符已复制到剪贴板（{CommonChineseChars.Length} 个字符）");
    }
}
#endif
