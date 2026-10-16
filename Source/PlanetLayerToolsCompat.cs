using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LunaReplacesLaoMoon
{
    /// <summary>
    /// 月之恋人附带的 PlanetLayerTools 在 1.7 上会中途失败。
    ///
    /// 联动关系：Luna Amante 的 Mod 构造函数把 HarmonyInitializer.HarmonyPatches 排进长事件。
    /// 它在挂 TravellingTransporters.get_Start 时，后置补丁参数写成了 Gravship，1.7 的 Harmony 直接抛 ArgumentException。
    /// 异常发生后，同方法里后面的运输舱 End、剧本补图层、读档补势力都不会再执行。
    /// 本类用 Finalizer 吃掉这次异常，再按正确类型补挂运输舱绘制，并补上 Scenario.ExposeData 与 FactionManagerPostLoadInit。
    ///
    /// 注意：Start / End 在游戏里是私有属性，要用 AccessTools.PropertyGetter。不要再把 __instance 写成 Gravship。
    /// </summary>
    public static class PlanetLayerToolsCompat
    {
        static bool repaired;

        public static void Patch(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method("PlanetLayerTools.HarmonyInitializer:HarmonyPatches");
            if (target == null)
            {
                Log.Warning("[月之恋人替换分层月球] 没有找到 PlanetLayerTools.HarmonyPatches，跳过运输舱补丁修复。");
                return;
            }

            harmony.Patch(target, finalizer: new HarmonyMethod(typeof(PlanetLayerToolsCompat), nameof(AfterPlanetLayerToolsPatches)));
        }

        /// <summary>
        /// HarmonyPatches 中途抛错时吞掉异常，并补上它没跑完的补丁。正常跑完时不做任何事。
        /// </summary>
        public static Exception AfterPlanetLayerToolsPatches(Exception __exception)
        {
            if (__exception == null || repaired)
            {
                return __exception;
            }

            repaired = true;
            Log.Warning(
                "[月之恋人替换分层月球] PlanetLayerTools 挂运输舱绘制补丁失败（1.7 类型不匹配）。正在补挂剩余补丁。原因: "
                + __exception.Message);

            try
            {
                RepairRemainingPatches();
            }
            catch (Exception e)
            {
                Log.Error("[月之恋人替换分层月球] 补挂 PlanetLayerTools 剩余补丁失败: " + e);
            }

            return null;
        }

        static void RepairRemainingPatches()
        {
            // 复用入口里已经创建的实例。这里再 new 同一个 HarmonyId，Harmony 会报配置冲突。
            Harmony harmony = LunaReplacesLaoMoonMod.Harmony;
            if (harmony == null)
            {
                Log.Error("[月之恋人替换分层月球] Harmony 实例尚未创建，无法补挂。");
                return;
            }
            System.Type initializer = AccessTools.TypeByName("PlanetLayerTools.HarmonyInitializer");
            if (initializer == null)
            {
                return;
            }

            bool fixCaravan = ReadBoolField(initializer, "_fixCaravanLocationPatch");
            bool addLayer = ReadBoolField(initializer, "_addLayerIfNotPresentPatch");

            if (fixCaravan)
            {
                MethodInfo start = AccessTools.PropertyGetter(typeof(TravellingTransporters), "Start");
                MethodInfo end = AccessTools.PropertyGetter(typeof(TravellingTransporters), "End");
                // 对方在 Start 上已经失败，一般没挂上；这里只挂正确签名的后置补丁。
                if (start != null)
                {
                    harmony.Patch(start, postfix: new HarmonyMethod(typeof(PlanetLayerToolsCompat), nameof(TravellingTransportersStartDrawPos)));
                }

                if (end != null)
                {
                    harmony.Patch(end, postfix: new HarmonyMethod(typeof(PlanetLayerToolsCompat), nameof(TravellingTransportersEndDrawPos)));
                }
            }

            if (addLayer)
            {
                MethodInfo scenario = AccessTools.Method(typeof(Scenario), nameof(Scenario.ExposeData));
                MethodInfo factions = AccessTools.Method(typeof(BackCompatibility), nameof(BackCompatibility.FactionManagerPostLoadInit));
                MethodInfo scenarioPost = AccessTools.Method(initializer, "ScenarioExposeDataPostFix");
                MethodInfo factionsPost = AccessTools.Method(initializer, "FactionManagerPostLoadInitPostFix");

                if (scenario != null && scenarioPost != null && !AlreadyPatched(scenario, scenarioPost))
                {
                    harmony.Patch(scenario, postfix: new HarmonyMethod(scenarioPost));
                }

                if (factions != null && factionsPost != null && !AlreadyPatched(factions, factionsPost))
                {
                    harmony.Patch(factions, postfix: new HarmonyMethod(factionsPost));
                }
            }

            Log.Message("[月之恋人替换分层月球] 已补挂 PlanetLayerTools 的运输舱绘制与读档补图层补丁。");
        }

        /// <summary>
        /// 与原版 TransportPodStartDrawPosPostFix 相同，只是 __instance 改成 TravellingTransporters。
        /// </summary>
        public static void TravellingTransportersStartDrawPos(ref Vector3 __result, PlanetTile ___initialTile)
        {
            __result += ___initialTile.Layer.Origin;
        }

        public static void TravellingTransportersEndDrawPos(ref Vector3 __result, PlanetTile ___initialTile)
        {
            __result += ___initialTile.Layer.Origin;
        }

        static bool ReadBoolField(System.Type type, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            return field != null && field.GetValue(null) is bool value && value;
        }

        static bool AlreadyPatched(MethodBase original, MethodInfo patch)
        {
            Patches info = Harmony.GetPatchInfo(original);
            if (info == null)
            {
                return false;
            }

            foreach (Patch postfix in info.Postfixes)
            {
                if (postfix.PatchMethod == patch)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
