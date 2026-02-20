using System;

/// <summary>
/// 地图条目数据结构，包含地图显示名称和对应的场景名。
/// </summary>
[Serializable]
public struct MapEntry
{
    public string mapName;       // 显示名称，如 "内陆湖"
    public string sceneName;     // 场景名，如 "GameScene"
}
