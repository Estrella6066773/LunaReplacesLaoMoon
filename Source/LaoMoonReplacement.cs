using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LunaReplacesLaoMoon
{
    /// <summary>
    /// 停用分层大气与轨道自带的月球 LAO_Surface_Luna，让月之恋人的 LNAM_MoonLayer 占据月球位置。
    ///
    /// 联动关系：
    /// 分层大气在静态构造里调用 LayeredAtmosphereOrbitUtility.ResetLayerData，按设置里的名称生成 planetLayersLAO，
    /// 接着 TryAddPlanetLayerts 把这些图层写进剧本并重连。本类在 ResetLayerData 之后从这份列表和行星字典里删掉自带月球，
    /// 并在 TryAddPlanetLayerts 前后清掉剧本里的对应部件。
    /// 读档时 Scenario.ExposeData 先于 WorldGrid.ExposeData。PlanetLayerTools 在 LoadingVars 补上月之恋人图层，
    /// 本类用更晚的后置补丁删掉自带月球，WorldGrid 的 PostLoadInit 再调用 CreateRequiredLayers 时就看不到它。
    /// 月球派系的白名单由月之恋人自己的 MoonFaction 补丁写入 LNAM_MoonLayer，这里不重写。
    ///
    /// 注意：WorldGrid 有几处用图层数量当字典下标。图层编号不连续时，planetLayers[i] 会抛出 KeyNotFoundException。
    /// 前置补丁改为遍历 PlanetLayers 的值。不要改回按下标循环。
    /// PlanetLayer.get_Visible 上，分层大气的前置补丁会返回 false 并跳过后续后置补丁。隐藏自带月球必须用更早的前置补丁，直接跳过原方法。
    /// </summary>
    public static class LaoMoonReplacement
    {
        public const string LaoLunaDef = "LAO_Surface_Luna";

        const string LaoLunaPlanet = "LAO_Luna";

        static bool loggedRemoval;

        /// <summary>
        /// 挂到分层大气的静态方法上。类型来自对方程序集，不能在编译期引用，所以在入口构造函数里手动挂。
        /// 对方未加载时直接返回。
        /// </summary>
        public static void PatchLayerMod(Harmony harmony)
        {
            System.Type utility = AccessTools.TypeByName("LayeredAtmosphereOrbit.LayeredAtmosphereOrbitUtility");
            if (utility == null)
            {
                Log.Warning("[月之恋人替换分层月球] 没有找到分层大气与轨道，跳过移除自带月球。");
                return;
            }

            MethodInfo reset = AccessTools.Method(utility, "ResetLayerData");
            MethodInfo tryAdd = AccessTools.Method(utility, "TryAddPlanetLayerts");
            if (reset != null)
            {
                harmony.Patch(reset, postfix: new HarmonyMethod(typeof(LaoMoonReplacement), nameof(AfterReset)));
            }

            if (tryAdd != null)
            {
                harmony.Patch(
                    tryAdd,
                    prefix: new HarmonyMethod(typeof(LaoMoonReplacement), nameof(BeforeTryAdd)),
                    postfix: new HarmonyMethod(typeof(LaoMoonReplacement), nameof(AfterTryAdd)));
            }
        }

        /// <summary>
        /// ResetLayerData 已经按设置重建 planetLayersLAO。这里删掉自带月球，并把它从设置名称里去掉，避免下次重建又加回来。
        /// 同时从行星字典移除 LAO_Luna，否则后续重连仍会把它当成另一颗星球的出口。
        /// </summary>
        public static void AfterReset()
        {
            RemoveAutoAddName();
            System.Type utility = AccessTools.TypeByName("LayeredAtmosphereOrbit.LayeredAtmosphereOrbitUtility");
            if (utility == null)
            {
                return;
            }

            FieldInfo layersField = AccessTools.Field(utility, "planetLayersLAO");
            if (layersField?.GetValue(null) is IList layers)
            {
                for (int i = layers.Count - 1; i >= 0; i--)
                {
                    if (IsLaoLunaPart(layers[i] as ScenPart))
                    {
                        layers.RemoveAt(i);
                    }
                }
            }

            // PlanetLayerGroupDef、PlanetDef 定义在分层大气自己的程序集里，这里只按 defName 删键，避免引用那份程序集。
            RemoveNamedKeys(AccessTools.Field(utility, "planetLayerGroups")?.GetValue(null), LaoLunaDef);
            RemoveNamedKeys(AccessTools.Field(utility, "planets")?.GetValue(null), LaoLunaPlanet);
            RemoveNamedKeys(AccessTools.Field(utility, "planets")?.GetValue(null), LaoLunaDef);

            if (!loggedRemoval)
            {
                loggedRemoval = true;
                Log.Message("[月之恋人替换分层月球] 已停用分层大气自带的月球，月之恋人的月球继续使用。");
            }
        }

        /// <summary>
        /// TryAddPlanetLayerts 会把 planetLayersLAO 里剩下的图层写进剧本，并按海拔重连。
        /// 必须在它动剧本之前先删掉自带月球，否则重连会给其他图层留下指向它的连接。
        /// </summary>
        public static void BeforeTryAdd(Scenario scenario)
        {
            StripScenario(scenario);
        }

        public static void AfterTryAdd(Scenario scenario)
        {
            StripScenario(scenario);
        }

        /// <summary>
        /// 读档、初始化、存档都会走到 Scenario.ExposeData。
        /// LoadingVars 发生在 WorldGrid.CreateRequiredLayers 之前，这时删掉部件，自带月球就不会被当成必须生成的图层。
        /// 月之恋人的补丁也挂在这里，优先级更早，它先补上 LNAM_MoonLayer，本方法再删 LAO_Surface_Luna。
        /// </summary>
        public static void AfterScenarioExpose(Scenario __instance)
        {
            if (Scribe.mode == LoadSaveMode.Inactive)
            {
                return;
            }

            StripScenario(__instance);
        }

        static void StripScenario(Scenario scenario)
        {
            if (scenario == null)
            {
                return;
            }

            FieldInfo partsField = AccessTools.Field(typeof(Scenario), "parts");
            FieldInfo surfaceField = AccessTools.Field(typeof(Scenario), "surfaceLayer");
            if (partsField?.GetValue(scenario) is List<ScenPart> parts)
            {
                parts.RemoveAll(IsLaoLunaPart);
                for (int i = 0; i < parts.Count; i++)
                {
                    StripConnections(parts[i] as ScenPart_PlanetLayer);
                }
            }

            StripConnections(surfaceField?.GetValue(scenario) as ScenPart_PlanetLayer);
        }

        static bool IsLaoLunaPart(ScenPart part)
        {
            ScenPart_PlanetLayer layer = part as ScenPart_PlanetLayer;
            return layer != null && (layer.tag == LaoLunaDef || layer.layer?.defName == LaoLunaDef);
        }

        static void StripConnections(ScenPart_PlanetLayer layer)
        {
            if (layer?.connections == null)
            {
                return;
            }

            layer.connections.RemoveAll(connection => connection != null && connection.tag == LaoLunaDef);
        }

        /// <summary>
        /// 从分层大气的字典里删掉指定 defName 的键，并清掉各个值列表里的同名项。
        /// 键的类型不属于游戏本体，只能走 IDictionary。
        /// </summary>
        static void RemoveNamedKeys(object dictionary, string defName)
        {
            if (dictionary is not IDictionary dict)
            {
                return;
            }

            List<object> drop = new List<object>();
            foreach (DictionaryEntry entry in dict)
            {
                if (DefNameOf(entry.Key) == defName)
                {
                    drop.Add(entry.Key);
                }

                if (entry.Value is IList list)
                {
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (DefNameOf(list[i]) == defName)
                        {
                            list.RemoveAt(i);
                        }
                    }
                }
            }

            for (int i = 0; i < drop.Count; i++)
            {
                dict.Remove(drop[i]);
            }
        }

        static string DefNameOf(object def)
        {
            if (def == null)
            {
                return null;
            }

            return AccessTools.Field(def.GetType(), "defName")?.GetValue(def) as string;
        }

        static void RemoveAutoAddName()
        {
            System.Type modType = AccessTools.TypeByName("LayeredAtmosphereOrbit.LAOMod");
            object settings = AccessTools.Property(modType, "Settings")?.GetValue(null);
            if (settings == null)
            {
                return;
            }

            object value = AccessTools.Field(settings.GetType(), "AutoAddLayersDefNames")?.GetValue(settings);
            if (value == null)
            {
                value = AccessTools.Property(settings.GetType(), "AutoAddLayersDefNames")?.GetValue(settings);
            }

            if (value is List<string> names)
            {
                names.RemoveAll(name => name == LaoLunaDef);
            }
            else if (value is IList list)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] as string == LaoLunaDef)
                    {
                        list.RemoveAt(i);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 自带月球已经写进旧存档。Visible 返回 false，并且禁止选中，世界图层按钮不会再切过去。
    /// 前置补丁返回 false，分层大气自己的显隐判断不会把结果改回来。
    /// </summary>
    [HarmonyPatch(typeof(PlanetLayer), "get_Visible")]
    [HarmonyPriority(Priority.First)]
    public static class Patch_HideLaoLunaVisible
    {
        public static bool Prefix(PlanetLayer __instance, ref bool __result)
        {
            if (__instance?.Def?.defName != LaoMoonReplacement.LaoLunaDef)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlanetLayer), "get_Raycastable")]
    [HarmonyPriority(Priority.First)]
    public static class Patch_HideLaoLunaRaycastable
    {
        public static bool Prefix(PlanetLayer __instance, ref bool __result)
        {
            if (__instance?.Def?.defName != LaoMoonReplacement.LaoLunaDef)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlanetLayer), nameof(PlanetLayer.CanSelectLayer))]
    public static class Patch_RejectLaoLunaSelection
    {
        public static bool Prefix(PlanetLayer __instance, ref AcceptanceReport __result)
        {
            if (__instance?.Def?.defName != LaoMoonReplacement.LaoLunaDef)
            {
                return true;
            }

            __result = new AcceptanceReport("分层大气自带的月球已停用。");
            return false;
        }
    }

    /// <summary>
    /// 按情景标签找图层。原方法用 0 到 Count-1 当下标，编号缺号时读档会在 CreateRequiredLayers 里崩溃。
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.TryGetLayerByTag))]
    public static class Patch_TryGetLayerByTag
    {
        public static bool Prefix(Dictionary<int, PlanetLayer> ___planetLayers, string tag, ref PlanetLayer layer)
        {
            layer = null;
            if (___planetLayers == null)
            {
                return false;
            }

            foreach (PlanetLayer candidate in ___planetLayers.Values)
            {
                if (candidate != null && candidate.ScenarioTag == tag)
                {
                    layer = candidate;
                    return false;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 按定义找第一个图层。原方法同样按下标访问。定义未指定时仍走原方法，它会返回地表，不会访问字典下标。
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.FirstLayerOfDef))]
    public static class Patch_FirstLayerOfDef
    {
        public static bool Prefix(Dictionary<int, PlanetLayer> ___planetLayers, PlanetLayerDef def, ref PlanetLayer __result)
        {
            if (def == null)
            {
                return true;
            }

            __result = null;
            if (___planetLayers == null)
            {
                return false;
            }

            foreach (PlanetLayer candidate in ___planetLayers.Values)
            {
                if (candidate != null && candidate.Def == def)
                {
                    __result = candidate;
                    return false;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.TryGetFirstAdjacentLayerOfDef))]
    public static class Patch_TryGetFirstAdjacentLayerOfDef
    {
        public static bool Prefix(Dictionary<int, PlanetLayer> ___planetLayers, PlanetTile origin, PlanetLayerDef def, ref PlanetLayer layer, ref bool __result)
        {
            if (origin.LayerDef == def)
            {
                layer = origin.Layer;
                __result = true;
                return false;
            }

            if (___planetLayers != null)
            {
                foreach (PlanetLayer candidate in ___planetLayers.Values)
                {
                    if (candidate != null && candidate.Def == def && candidate.DirectConnectionTo(origin.Layer))
                    {
                        layer = candidate;
                        __result = true;
                        return false;
                    }
                }
            }

            layer = null;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Scenario), nameof(Scenario.ExposeData))]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_ScenarioExpose
    {
        public static void Postfix(Scenario __instance)
        {
            LaoMoonReplacement.AfterScenarioExpose(__instance);
        }
    }

    /// <summary>
    /// 世界图层按钮在 WorldGrid.GetGizmos 里生成。分层大气还会在后置补丁里重排这份列表，
    /// 所以本补丁用最后执行的优先级，在它排完之后丢掉自带月球那一颗按钮。
    /// 分层大气自己的图层菜单仍会列出它，但 CanSelectLayer 会拒绝选中。
    /// </summary>
    [HarmonyPatch(typeof(WorldGrid), nameof(WorldGrid.GetGizmos))]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_HideLaoLunaGizmo
    {
        public static void Postfix(ref IEnumerable<Gizmo> __result)
        {
            if (__result == null)
            {
                return;
            }

            PlanetLayerDef def = DefDatabase<PlanetLayerDef>.GetNamedSilentFail(LaoMoonReplacement.LaoLunaDef);
            if (def == null)
            {
                return;
            }

            UnityEngine.Texture icon = def.ViewGizmoTexture;
            List<Gizmo> kept = new List<Gizmo>();
            foreach (Gizmo gizmo in __result)
            {
                Command command = gizmo as Command;
                if (command != null && command.icon == icon)
                {
                    continue;
                }

                kept.Add(gizmo);
            }

            __result = kept;
        }
    }
}
