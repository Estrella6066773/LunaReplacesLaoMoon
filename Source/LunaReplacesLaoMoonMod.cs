using System.Reflection;
using HarmonyLib;
using Verse;

namespace LunaReplacesLaoMoon
{
    /// <summary>
    /// 模组入口。游戏在 LoadedModManager.CreateModClasses 里构造本类。
    ///
    /// 联动关系：构造函数先 PatchAll，挂上世界网格查找和分层月球显隐。
    /// 分层大气与轨道的 ResetLayerData、TryAddPlanetLayerts 在它自己的 StaticConstructorOnStartup 里才跑，
    /// 晚于全部模组构造函数，所以这里用手动补丁即可赶上那一次调用。
    /// 月之恋人的 PlanetLayerTools 在读档时往剧本里补 LNAM_MoonLayer，本模组不重复做这件事。
    ///
    /// 注意：HarmonyId 与 About.xml 的 packageId 相同。不要把 Harmony.dll 拷进 Assemblies。
    /// </summary>
    public class LunaReplacesLaoMoonMod : Mod
    {
        public const string HarmonyId = "Estrella6066.lunaReplacesLaoMoon";

        /// <summary>
        /// 入口构造时创建的唯一 Harmony 实例。后补的运输舱绘制补丁必须复用它，不要再 new 同一个 HarmonyId。
        /// </summary>
        public static Harmony Harmony { get; private set; }

        public LunaReplacesLaoMoonMod(ModContentPack content) : base(content)
        {
            Harmony = new Harmony(HarmonyId);
            Harmony.PatchAll(Assembly.GetExecutingAssembly());
            LaoMoonReplacement.PatchLayerMod(Harmony);
            // 月之恋人的 PlanetLayerTools 用长事件挂补丁，晚于本构造函数；这里先挂上 Finalizer，等它跑时再修。
            PlanetLayerToolsCompat.Patch(Harmony);
        }
    }
}
