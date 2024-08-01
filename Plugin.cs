using BepInEx;
using BepInEx.Logging;
using DG.Tweening;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MiniCenterControl
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {   
        private void Awake()
        {
            Log = Logger;

            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            
            SceneManager.sceneLoaded += OnSceneLoaded;
            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded: {scene.name}");
        }

        internal static ManualLogSource Log;
    }

    public class Manager: MonoBehaviour
    {
        public static ColorCode.Option GetRandomColor()
        {
            // 50% departing outside region.
            if (UnityEngine.Random.value > 0.5)
            {
                return ColorCode.Option.Gray;
            }
            return GetRandomRunwayColor();
        }

        public static ColorCode.Option GetRandomRunwayColor()
        {
            return colors[UnityEngine.Random.Range(0, colors.Count)];
        }

        private void Start()
        {
            colors = new List<ColorCode.Option>();

            int i = -1;
            foreach(Runway runway in Runway.Runways)
            {
                RunwayTag tag = runway.gameObject.AddComponent<RunwayTag>();
                ColorCode.Option color = ColorCode.AllTakeOffOptions[++i % ColorCode.AllTakeOffOptions.Count];
                tag.color_ = color;
                colors.Add(color);
                runway.Square.GetComponent<Renderer>().material.color = ColorCode.GetColor(color);

                if (MapManager.gameMode != GameMode.SandBox && i > 0)
                {
                    // At least has as many exit as the amount of runsways.
                    TakeoffTaskManager.Instance.AddApron();
                    TakeoffTaskManager.Instance.AddEntrance();
                }
            }
        }

        public static Manager Instance;
        public static List<ColorCode.Option> colors;
    }

    public class ActiveAircraftTag : MonoBehaviour
    {
        public ColorCode.Option color_ = ColorCode.Option.Gray;
        public bool active_ = false;
    }

    public class AircraftTag : MonoBehaviour 
    {
        private void Update()
        {
            if (aircraft_ == null)
            {
                return;
            }

            if (color_ != ColorCode.Option.Gray && aircraft_.AP.GetComponent<Renderer>() != null)
            {
                aircraft_.AP.GetComponent<Renderer>().material.color = ColorCode.GetColor(color_);
            }

            if (aircraft_.direction == Aircraft.Direction.Outbound && 
                (aircraft_.state == Aircraft.State.Flying || aircraft_.state == Aircraft.State.HeadingAfterReachingWaypoint) &&
                color_ != ColorCode.Option.Gray && afterTakeoffCoroutine == null)
            {
                afterTakeoffCoroutine = AfterTakeoffCoroutine(aircraft_);
                Manager.Instance.StartCoroutine(afterTakeoffCoroutine);
            }
        }

        private static IEnumerator AfterTakeoffCoroutine(Aircraft aircraft)
        {
            yield return new WaitForSeconds(10f);

            Vector3 position = aircraft.AP.transform.position;
            float heading = aircraft.heading;
            ColorCode.Option color = Manager.GetRandomRunwayColor();
            AircraftTag tag = aircraft.GetComponent<AircraftTag>();
            if (tag != null)
            {
                color = tag.color_;
            }

            aircraft.ConditionalDestroy();

            aircraft = AircraftManager.Instance.CreateInboundAircraft(position, heading);
            aircraft.colorCode = color;
        }

        public Aircraft aircraft_;
        public TakeoffTask task_;
        public ColorCode.Option color_ = ColorCode.Option.Gray;
        private IEnumerator afterTakeoffCoroutine = null;
    }

    public class TakeoffTaskTag : MonoBehaviour 
    {
        private void Update()
        {
            if (aircraft_ == null)
            {
                return;
            }

            aircraft_.color = ColorCode.GetColor(color_);
        }

        public Image aircraft_;
        public TakeoffTask task_;
        public ColorCode.Option color_ = ColorCode.Option.Gray;
    }

    public class RunwayTag : MonoBehaviour 
    {
        public static bool CanUseRunway(ColorCode.Option color, Runway runway)
        {
            if (runway == null)
            {
                return false;
            }

            RunwayTag tag = runway.GetComponent<RunwayTag>();
            if (tag == null)
            {
                return false;
            }
            return tag.color_ == color;
        }

        public Runway runway_;
        public ColorCode.Option color_;
    }

    // Assign color code to all arrival.
    [HarmonyPatch(typeof(Aircraft), "Start", new Type[] {})]
    class PatchAircraftStart
    {
        static void Postfix(ref Aircraft __instance)
        {
            if (__instance.direction == Aircraft.Direction.Inbound && 
                __instance.colorCode == ColorCode.Option.Gray)
            {
                List<ColorCode.Option> colors = new List<ColorCode.Option>();
                foreach (Waypoint waypoint in WaypointManager.Instance.Waypoints)
                {
                    if (!colors.Contains(waypoint.colorCode) && TakeoffTaskManager.Instance.colorOptions.Contains(waypoint.colorCode))
                    {
                        colors.Add(waypoint.colorCode);
                    }
                }

                // 25% transiting through the airspace.
                if (UnityEngine.Random.value < 0.25)
                {
                    Vector3 position = __instance.AP.transform.position;
                    float heading = __instance.heading;
                    __instance.ConditionalDestroy();

                    // From CreateOutboundAircraft().
                    Aircraft component = UnityEngine.Object.Instantiate<GameObject>(AircraftManager.Instance.AircraftCirclePrefab, position, Quaternion.identity, AircraftManager.Instance.OutboundRoot.transform).GetComponent<Aircraft>();
                    component.direction = Aircraft.Direction.Outbound;
                    component.heading = heading;
                    component.manualTurn = true;
                    component.targetManualHeading = heading;
                    component.targetSpeed = 24f;
                    component.colorCode = colors[UnityEngine.Random.Range(0, colors.Count)];
                    component.shapeCode = ShapeCode.Option.Circle;

                    AircraftTag aircraftTag = component.gameObject.AddComponent<AircraftTag>();
                    aircraftTag.aircraft_ = component;
                    aircraftTag.color_ = ColorCode.Option.Gray;
                }
                else
                {
                    // Arrivals from outside of the screen.
                    __instance.colorCode = Manager.GetRandomRunwayColor();
                }
            }
        }
    }

    // Update color code's color.
    [HarmonyPatch(typeof(Aircraft), "Update", new Type[] {})]
    class PatchAircraftUpdate
    {
        static void Postfix(ref Aircraft __instance)
        {
            if (__instance.direction == Aircraft.Direction.Inbound)
            {
                __instance.Panel.GetComponent<Renderer>().material.color = ColorCode.GetColor(__instance.colorCode);
            }
        }
    }

    // Cannot land on runway with different color.
    [HarmonyPatch(typeof(Aircraft), "TrySetupLanding", new Type[] {typeof(Runway), typeof(bool)})]
    class PatchTrySetupLanding
    {
        static bool Prefix(Runway runway, bool doLand, ref Aircraft __instance)
        {
            Runway runway2 = (runway ? runway : Aircraft.CurrentCommandingRunway);
            if (!RunwayTag.CanUseRunway(__instance.colorCode, runway2))
            {
                // Reflex for  __instance.LandingRunway;
                Runway LandingRunway = __instance.GetFieldValue<Runway>("LandingRunway");
                bool flag = __instance.state == Aircraft.State.Landing && runway2 == LandingRunway;

                // Reflex for bool flag2 = __instance.GenerateLandingPathL1(runway2, out path, flag);
                MethodInfo GenerateLandingPathL1 = typeof(Aircraft).GetMethod("GenerateLandingPathL1", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                object[] args = new object[] { runway2, null, flag, true };
                GenerateLandingPathL1.Invoke(__instance, args);
                List<Vector3> path = (List<Vector3>)args[1];

                // Reflex for  __instance.ShowPath(path, false /* success */);
                MethodInfo ShowPath = __instance.GetType().GetMethod("ShowPath", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                ShowPath.Invoke(__instance, new object[] { path, false /* success */ });

                return false;
            }
            return true;
        }
    }

    // Do not allow colored outbound to leave.
    [HarmonyPatch(typeof(Aircraft), "OnTriggerEnter2D", new Type[] {typeof(Collider2D)})]
    class PatchOnTriggerEnter2D
    {
        static bool Prefix(Collider2D other, ref bool ___mainMenuMode, ref ColorCode.Option ___colorCode, 
                           ref ShapeCode.Option ___shapeCode, ref Aircraft __instance, ref bool ___reachExit)
        {
            if (___mainMenuMode || !((Component)(object)other).CompareTag("CollideCheck"))
            {
                return false;
            }

            if (((Component)(object)other).gameObject.layer == LayerMask.NameToLayer("Waypoint"))
            {
                Waypoint waypoint = ((Component)(object)other).GetComponent<WaypointRef>().waypoint;
                if (waypoint != null && ___colorCode == waypoint.colorCode && ___shapeCode == waypoint.shapeCode)
                {
                    AircraftTag tag = __instance.GetComponent<AircraftTag>();
                    if (tag != null && tag.color_ == ColorCode.Option.Gray)
                    {
                        WaypointManager.Instance.Handoff(waypoint);
                        __instance.aircraftVoiceAndSubtitles.PlayHandOff();
                        AircraftManager.Instance.AircraftHandOffEvent.Invoke(__instance.gameObject.transform.position);
                        ___reachExit = true;
                        __instance.Invoke("ConditionalDestroy", 2f);
                    }

                    return false;
                }
            }

            return true;
        }
    }

    // Cannot takeoff from runway with different color.
    [HarmonyPatch(typeof(TakeoffTask), "OnPointUp", new Type[] {})]
    class PatchTakeoffTaskOnPointUp
    {
        static void RejectTakeoff(ref TakeoffTask __instance)
        {
            float duration2 = 0.5f;
            __instance.Panel.transform.DOScale(1f, duration2).SetUpdate(isIndependentUpdate: true);
            __instance.transform.DOMove(__instance.apron.gameObject.transform.position, duration2).SetUpdate(isIndependentUpdate: true);
            AudioManager.instance.PlayRejectTakeoff();

            __instance.inCommand = false;
            TakeoffTask.CurrentCommandingTakeoffTask = null;
            TakeoffTask.CurrentCommandingTakeoffPoint = null;
            TakeoffTask.CurrentCommandingRunway = null;
            foreach (Runway runway_ in Runway.Runways)
            {
                runway_.HideTakeoffPoints();
            }
        }

        static bool Prefix(ref TakeoffTask __instance)
        {
            if (!__instance.inCommand)
            {
                return false;
            }

            if (TakeoffTask.CurrentCommandingTakeoffPoint == null && __instance.apron != null && __instance.apron.gameObject != null)
            {
                return true;
            }

            Runway runway = TakeoffTask.CurrentCommandingTakeoffPoint.GetComponent<RunwayRef>().runway;
            if (!RunwayTag.CanUseRunway(__instance.colorCode, runway))
            {
                RejectTakeoff(ref __instance);
                return false;
            }

            return true;
        }
    }

    // Initialize takeoff task with destination color.
    [HarmonyPatch(typeof(TakeoffTask), "Start", new Type[] {})]
    class PatchTakeoffTaskStart
    {
        static void Postfix(ref TakeoffTask __instance, ref Image ___AP)
        {
            __instance.colorCode = Manager.GetRandomRunwayColor();
            __instance.SetColor();

            TakeoffTaskTag tag = __instance.gameObject.AddComponent<TakeoffTaskTag>();
            tag.task_ = __instance;
            tag.aircraft_ = ___AP;
            tag.color_ = Manager.GetRandomColor();
            while (__instance.colorCode == tag.color_)
            {
                tag.color_ = Manager.GetRandomColor();
            }
        }
    }

    // Transfer takeoff task destination color to aircraft.
    [HarmonyPatch(typeof(TakeoffTask), "SetupTakeoff", new Type[] {})]
    class PatchSetupTakeoff
    {
        static bool Prefix(ref TakeoffTask __instance)
        {
            ActiveAircraftTag tag = AircraftManager.Instance.GetComponent<ActiveAircraftTag>();
            TakeoffTaskTag taskTag = __instance.GetComponent<TakeoffTaskTag>();
            if (tag == null || taskTag == null)
            {
                return true;
            }

            tag.color_ = taskTag.color_;
            tag.active_ = true;

            return true;
        }

        static void Postfix(ref TakeoffTask __instance)
        {
           ActiveAircraftTag tag = AircraftManager.Instance.GetComponent<ActiveAircraftTag>();
            if (tag == null)
            {
                return;
            }

            tag.active_ = false;

        }
    }

    // Temp holder of the aicraft color.
    [HarmonyPatch(typeof(AircraftManager), "Start", new Type[] {})]
    class PatchAircraftManagerStart
    {
        static bool Prefix(ref AircraftManager __instance)
        {
            __instance.gameObject.AddComponent<ActiveAircraftTag>();
            return true;
        }
    }

    // Transfer tag color.
    [HarmonyPatch(typeof(AircraftManager), "CreateOutboundAircraft", new Type[] {
        typeof(Runway), typeof(Vector3), typeof(float), typeof(float), typeof(string), typeof(ColorCode.Option), typeof(ShapeCode.Option)})]
    class PatchCreateOutboundAircraft
    {
        static void Postfix(Runway runway, Vector3 position, float heading, float nominalHeading, string lr,
                            ColorCode.Option colorCode, ShapeCode.Option shapeCode,
                            ref AircraftManager __instance, ref Aircraft __result)
        {

            ActiveAircraftTag tag = AircraftManager.Instance.GetComponent<ActiveAircraftTag>();
            if (tag != null &&  __result.direction == Aircraft.Direction.Outbound)
            {
                AircraftTag aircraftTag = __result.gameObject.AddComponent<AircraftTag>();
                aircraftTag.aircraft_ = __result;
                aircraftTag.color_ = tag.color_;
            }
        }
    }

    // Fully upgrade before starting.
    [HarmonyPatch(typeof(UpgradeManager), "Start", new Type[] {})]
    class PatchUpgradeManagerStart
    {
        static void Postfix(ref UpgradeManager __instance)
        {
            Plugin.Log.LogInfo("UpgradeManager Starts");

            // Max-out all airspace.
            if (MapManager.gameMode != GameMode.SandBox)
            {
                Camera.main.DOOrthoSize(LevelManager.Instance.maximumCameraOrthographicSize, 0.5f).SetUpdate(isIndependentUpdate: true);
            }

            // Attach Manager to ESC button.
            GameObject esc_button = GameObject.Find("ESC_Button");
            Manager manager = esc_button.gameObject.AddComponent<Manager>();
            Manager.Instance = manager;
        }
    }

    // Remove not useful upgrades.
    [HarmonyPatch(typeof(UpgradeManager), "ProcessOptionProbs", new Type[] {})]
    class PatchProcessOptionProbs
    {
        static void Postfix(ref List<float> __result)
        {
            if (MapManager.gameMode != GameMode.SandBox)
            {
                __result[4] = 0; // AIRSPACE
            }
        }
    }

    // Do not allow camera size change.
    [HarmonyPatch(typeof(LevelManager), "Start", new Type[] {})]
    class PatchLevelManagerStart
    {
        static void Postfix()
        {
            LevelManager.CameraSizeIncByFailGenWaypoint = 0f;
        }
    }

    public static class ReflectionExtensions
    {
        public static T GetFieldValue<T>(this object obj, string name)
        {
            // Set the flags so that private and public fields from instances will be found
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var field = obj.GetType().GetField(name, bindingFlags);
            return (T)field?.GetValue(obj);
        }

        public static void SetFieldValue<T>(this object obj, string name, T value)
        {
            // Set the flags so that private and public fields from instances will be found
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var field = obj.GetType().GetField(name, bindingFlags);
            field.SetValue(obj, value);
        }
    }
}
