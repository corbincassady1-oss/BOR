using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(StretchFlexMotion.Mod), "Stretch Flex Motion", "1.5.0", "BOR")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace StretchFlexMotion
{
    public class Mod : MelonMod
    {
        private MelonPreferences_Entry<bool> enabled;
        private MelonPreferences_Entry<bool> flexibleWaist;
        private MelonPreferences_Entry<bool> safetyMigration;
        private MelonPreferences_Entry<float> stretchiness;
        private MelonPreferences_Entry<float> flexibility;
                private MelonPreferences_Entry<float> armStretch;
        private MelonPreferences_Entry<float> legStretch;
        private MelonPreferences_Entry<float> spineStretch;
        private MelonPreferences_Entry<float> hipFlex;
        private MelonPreferences_Entry<float> waistFlex;
        private MelonPreferences_Entry<float> maxBend;

        private sealed class BoneState
        {
            public object Transform;
            public string Name;
            public int Group;
            public object OriginalScale;
            public object OriginalRotation;
        }

        // These are avatar-renderer transforms only. PhysicsRig is deliberately never accessed.
        private readonly List<BoneState> bones = new List<BoneState>();
        private object rigManager;
        private object avatarRoot;
        private float nextScan;
        private bool menuMade;
        private bool activeLastFrame;
        private bool waitingLogged;
        private bool appliedForCurrentEnable;

        private const int Arms = 1;
        private const int Legs = 2;
        private const int Spine = 3;
        private const int Hips = 4;
        private const int Waist = 5;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("StretchFlexMotion", "Stretch Flex Motion");
            enabled = cat.CreateEntry("Enabled", false);
            flexibleWaist = cat.CreateEntry("Flexible Waist", true);
            safetyMigration = cat.CreateEntry("SafetyMigration150", false);

            stretchiness = cat.CreateEntry("Stretchiness", 1.5f);
            flexibility = cat.CreateEntry("Flexibility", 1.5f);
            armStretch = cat.CreateEntry("Arm Stretch", 1.5f);
            legStretch = cat.CreateEntry("Leg Stretch", 1.5f);
            spineStretch = cat.CreateEntry("Spine Stretch", 1.5f);
            hipFlex = cat.CreateEntry("Hip Flexibility", 1.5f);
            waistFlex = cat.CreateEntry("Waist Flexibility", 1.5f);
            maxBend = cat.CreateEntry("Maximum Bend", 20f);

            // A prior build could leave Enabled=true in the saved config.
            // Force one safe-off migration when this version is first installed.
            if (!safetyMigration.Value)
            {
                enabled.Value = false;
                safetyMigration.Value = true;
            }

            MelonLogger.Msg("[Stretch Flex Motion] 1.5.0 loaded. One-shot visual-avatar mode; settings apply once when Enabled is pressed.");
        }

        public override void OnLateInitializeMelon()
        {
            TryMenu();
        }

        public override void OnUpdate()
        {
            if (!enabled.Value)
            {
                if (activeLastFrame || bones.Count > 0)
                    RestoreAndClear();

                activeLastFrame = false;
                appliedForCurrentEnable = false;
                TryMenu();
                return;
            }

            if (!activeLastFrame)
            {
                activeLastFrame = true;
                appliedForCurrentEnable = false;
                nextScan = 0f;
                waitingLogged = false;
            }

            // Search only while waiting for the visual avatar. Once the pose is
            // applied, there is no per-frame or periodic transform work.
            if (!appliedForCurrentEnable && TimeValue("time") >= nextScan)
            {
                nextScan = TimeValue("time") + 0.5f;
                if (FindPlayerAvatar())
                {
                    ApplyVisualFlexOnce();
                    appliedForCurrentEnable = true;
                }
            }

            TryMenu();
        }

        public override void OnDeinitializeMelon()
        {
            RestoreAndClear();
        }

        private bool FindPlayerAvatar()
        {
            try
            {
                Type playerType = FindType("BoneLib.Player");
                object manager = GetStatic(playerType, "RigManager") ?? GetStatic(playerType, "rigManager");
                if (manager == null)
                {
                    LogWaiting("Waiting for BoneLib.Player.RigManager.");
                    return false;
                }

                // RigManager.avatar is the rendered avatar component. Do not fall back
                // to manager.transform or physicsRig.transform: those contain live physics.
                object avatar = MemberOrGetter(manager, "avatar", "Avatar");
                if (avatar == null)
                {
                    LogWaiting("Player RigManager found; waiting for its avatar visual.");
                    return false;
                }

                object root = MemberOrGetter(avatar, "transform", "Transform");
                if (root == null)
                {
                    object gameObject = MemberOrGetter(avatar, "gameObject", "GameObject");
                    root = gameObject == null ? null : MemberOrGetter(gameObject, "transform", "Transform");
                }

                if (root == null)
                {
                    LogWaiting("Avatar component found, but its visual transform is unavailable.");
                    return;
                }

                if (!SameObject(manager, rigManager) || !SameObject(root, avatarRoot))
                {
                    RestoreAndClear();
                    rigManager = manager;
                    avatarRoot = root;
                    waitingLogged = false;
                }

                if (bones.Count == 0)
                {
                    int visited = CollectVisualBones(root, 0);
                    MelonLogger.Msg("[Stretch Flex Motion] Visual avatar scanned. Body transforms: " + bones.Count + ".");
                }

                return bones.Count > 0;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stretch Flex Motion] Avatar discovery: " + e.Message);
                return false;
            }
        }

        private void LogWaiting(string message)
        {
            if (!waitingLogged)
            {
                MelonLogger.Msg("[Stretch Flex Motion] " + message);
                waitingLogged = true;
            }
        }

        private int CollectVisualBones(object parent, int depth)
        {
            if (parent == null || depth > 80 || bones.Count >= 512)
                return 0;

            int visited = 0;
            try
            {
                int childCount = Convert.ToInt32(MemberOrGetter(parent, "childCount", "ChildCount"));
                for (int i = 0; i < childCount && bones.Count < 512; i++)
                {
                    object child = Call(parent, "GetChild", i);
                    if (child == null)
                        continue;

                    visited++;
                    string name = Name(child).ToLowerInvariant();

                    // Do not descend into hand/controller/collider/physics subtrees.
                    if (IsUnsafeVisualBranch(name))
                        continue;

                    int group = ClassifyBone(name);
                    if (group != 0)
                    {
                        bones.Add(new BoneState
                        {
                            Transform = child,
                            Name = name,
                            Group = group,
                            OriginalScale = MemberOrGetter(child, "localScale", "LocalScale"),
                            OriginalRotation = MemberOrGetter(child, "localRotation", "LocalRotation")
                        });
                    }

                    visited += CollectVisualBones(child, depth + 1);
                }
            }
            catch { }
            return visited;
        }

        private static bool IsUnsafeVisualBranch(string n)
        {
            return n.Contains("physics") || n.Contains("physrig") ||
                   n.Contains("rigidbody") || n.Contains("collider") ||
                   n.Contains("joint") || n.Contains("controller") ||
                   n.Contains("tracker") || n.Contains("grip") ||
                   n.Contains("weapon") || n.Contains("sensor") ||
                   n.Contains("hand") || n.Contains("finger") ||
                   n.Contains("thumb") || n.Contains("pinky") ||
                   n.Contains("index") || n.Contains("middlefinger") ||
                   n.Contains("ringfinger");
        }

        private static int ClassifyBone(string n)
        {
            if (n.Contains("waist") || n.Contains("abdomen") ||
                n.Contains("lowerback") || n.Contains("lower_back") ||
                n.Contains("lower-spine") || n.Contains("lower_spine") ||
                n.Contains("torso_lower") || n == "spine" ||
                n.Contains("spine1") || n.Contains("spine_01") ||
                n.Contains("spine 1"))
                return Waist;

            if (n.Contains("pelvis") || n == "hips" || n.Contains("hip_l") ||
                n.Contains("hip_r") || n.Contains("lefthip") || n.Contains("righthip"))
                return Hips;

            if (n.Contains("upperarm") || n.Contains("lowerarm") ||
                n.Contains("forearm") || n.Contains("leftarm") ||
                n.Contains("rightarm") || n.Contains("arm_l") ||
                n.Contains("arm_r") || n.Contains("bicep"))
                return Arms;

            if (n.Contains("thigh") || n.Contains("upperleg") ||
                n.Contains("lowerleg") || n.Contains("calf") ||
                n.Contains("shin") || n.Contains("leftleg") ||
                n.Contains("rightleg") || n.Contains("leg_l") ||
                n.Contains("leg_r") || n.Contains("foot"))
                return Legs;

            if (n.Contains("upperchest") || n.Contains("chest") ||
                n.Contains("neck") || n.Contains("spine2") ||
                n.Contains("spine_02") || n.Contains("spine 2") ||
                n.Contains("spine3") || n.Contains("spine_03") ||
                n.Contains("upper_spine"))
                return Spine;

            return 0;
        }

        private void ApplyVisualFlexOnce()
        {
            try
            {
                float stretch = Clamp(stretchiness.Value, 0f, 3f);
                float masterFlex = Clamp(flexibility.Value, 0f, 3f) / 3f;
                float bendLimit = Clamp(maxBend.Value, 0f, 45f);

                foreach (BoneState bone in bones)
                {
                    if (bone == null || bone.Transform == null)
                        continue;

                    float groupControl = GroupControl(bone.Group);
                    if (groupControl <= 0f)
                        continue;

                    // Apply scale once from the original snapshot. No lerp or
                    // continuous correction is performed after this pass.
                    float factor = Clamp(1f + stretch * groupControl * 0.05f, 1f, 1.45f);
                    if (bone.OriginalScale != null)
                    {
                        object targetScale = StretchScale(bone.OriginalScale, factor, bone.Name);
                        if (targetScale != null)
                            SetMember(bone.Transform, "localScale", targetScale);
                    }

                    float localFlex = GroupFlex(bone.Group) / 3f;
                    float amplitude = bendLimit * masterFlex * localFlex * 0.35f;
                    if (bone.Group == Waist && !flexibleWaist.Value)
                        amplitude = 0f;

                    if (bone.OriginalRotation != null && amplitude > 0.001f)
                    {
                        // Fixed, mild pose offset: computed once per activation.
                        // Alternate the slight side bias across bones to avoid
                        // applying a perfectly rigid block-like bend.
                        float sign = (GetId(bone.Transform) & 1) == 0 ? 1f : -1f;
                        float rx = amplitude * 0.16f;
                        float ry = amplitude * 0.07f * sign;
                        float rz = amplitude * 0.10f * sign;

                        if (bone.Group == Arms) { rx *= 0.55f; ry *= 0.45f; rz *= 0.55f; }
                        else if (bone.Group == Legs) { rx *= 0.45f; ry *= 0.35f; rz *= 0.40f; }
                        else if (bone.Group == Hips) { rx *= 0.75f; ry *= 0.75f; rz *= 0.55f; }
                        else if (bone.Group == Waist) { rx *= 1.15f; ry *= 0.85f; rz *= 1.35f; }
                        else if (bone.Group == Spine) { rx *= 0.65f; ry *= 0.50f; rz *= 0.65f; }

                        object delta = Euler(rx, ry, rz);
                        object targetRotation = delta == null ? null : Multiply(bone.OriginalRotation, delta);
                        if (targetRotation != null)
                            SetMember(bone.Transform, "localRotation", targetRotation);
                    }
                }

                MelonLogger.Msg("[Stretch Flex Motion] One-shot stretch/flex settings applied. Toggle Enabled off/on to reapply changed settings.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stretch Flex Motion] One-shot apply: " + e.Message);
            }
        }

        private float GroupControl(int group)
        {
            switch (group)
            {
                case Arms: return Clamp(armStretch.Value, 0f, 3f);
                case Legs: return Clamp(legStretch.Value, 0f, 3f);
                case Spine: return Clamp(spineStretch.Value, 0f, 3f);
                case Hips: return Clamp(hipFlex.Value, 0f, 3f);
                case Waist: return flexibleWaist.Value ? Clamp(waistFlex.Value, 0f, 3f) : 0f;
                default: return 0f;
            }
        }

        private float GroupFlex(int group)
        {
            switch (group)
            {
                case Hips: return Clamp(hipFlex.Value, 0f, 3f);
                case Waist: return flexibleWaist.Value ? Clamp(waistFlex.Value, 0f, 3f) : 0f;
                case Spine: return Clamp(spineStretch.Value, 0f, 3f);
                case Arms:
                case Legs: return 3f;
                default: return 0f;
            }
        }

        private static object StretchScale(object original, float factor, string name)
        {
            float x = Component(original, "x");
            float y = Component(original, "y");
            float z = Component(original, "z");

            bool lengthAxis = name.Contains("arm") || name.Contains("leg") ||
                              name.Contains("thigh") || name.Contains("calf") ||
                              name.Contains("shin") || name.Contains("spine") ||
                              name.Contains("chest") || name.Contains("waist") ||
                              name.Contains("abdomen");
            if (lengthAxis)
                return Vector(x, y * factor, z);

            float widthFactor = 1f + (factor - 1f) * 0.20f;
            return Vector(x * widthFactor, y * factor, z * widthFactor);
        }

        private void RestoreAndClear()
        {
            try
            {
                foreach (BoneState bone in bones)
                {
                    if (bone == null || bone.Transform == null)
                        continue;

                    if (bone.OriginalScale != null)
                        SetMember(bone.Transform, "localScale", bone.OriginalScale);

                    if (bone.OriginalRotation != null)
                        SetMember(bone.Transform, "localRotation", bone.OriginalRotation);
                }
            }
            catch { }

            bones.Clear();
            avatarRoot = null;
            rigManager = null;
        }

        private void TryMenu()
        {
            if (menuMade)
                return;

            try
            {
                Type pageType = FindType("BoneLib.BoneMenu.Page");
                if (pageType == null)
                    return;

                object root = pageType.GetProperty("Root", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (root == null)
                    return;

                Type colorType = FindType("UnityEngine.Color");
                object cyan = Activator.CreateInstance(colorType, new object[] { 0.1f, 0.9f, 1f, 1f });
                MethodInfo createPage = pageType.GetMethod("CreatePage", new[] { typeof(string), colorType, typeof(int), typeof(bool) });
                if (createPage == null)
                    return;

                object page = createPage.Invoke(root, new object[] { "Stretch Flex Motion", cyan, 0, true });
                if (page == null)
                    return;

                CreateBool(page, "Enabled", enabled.Value, v => enabled.Value = v);
                CreateFloat(page, "Stretchiness", stretchiness.Value, 0.1f, 0f, 3f, v => stretchiness.Value = v);
                CreateFloat(page, "Flexibility", flexibility.Value, 0.1f, 0f, 3f, v => flexibility.Value = v);
                CreateBool(page, "Flexible Waist", flexibleWaist.Value, v => flexibleWaist.Value = v);
                CreateFloat(page, "Waist Flexibility", waistFlex.Value, 0.1f, 0f, 3f, v => waistFlex.Value = v);
                CreateFloat(page, "Arm Stretch", armStretch.Value, 0.1f, 0f, 3f, v => armStretch.Value = v);
                CreateFloat(page, "Leg Stretch", legStretch.Value, 0.1f, 0f, 3f, v => legStretch.Value = v);
                CreateFloat(page, "Spine Stretch", spineStretch.Value, 0.1f, 0f, 3f, v => spineStretch.Value = v);
                CreateFloat(page, "Hip Flexibility", hipFlex.Value, 0.1f, 0f, 3f, v => hipFlex.Value = v);
                CreateFloat(page, "Maximum Bend", maxBend.Value, 1f, 0f, 45f, v => maxBend.Value = v);
                menuMade = true;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stretch Flex Motion] BoneMenu: " + e.Message);
            }
        }

        private void CreateBool(object page, string name, bool value, Action<bool> callback)
        {
            Type colorType = FindType("UnityEngine.Color");
            object white = Activator.CreateInstance(colorType, new object[] { 1f, 1f, 1f, 1f });
            foreach (MethodInfo method in page.GetType().GetMethods())
            {
                if (method.Name == "CreateBool" && method.GetParameters().Length == 4)
                {
                    method.Invoke(page, new object[] { name, white, value, callback });
                    return;
                }
            }
        }

        private void CreateFloat(object page, string name, float value, float step, float min, float max, Action<float> callback)
        {
            Type colorType = FindType("UnityEngine.Color");
            object white = Activator.CreateInstance(colorType, new object[] { 1f, 1f, 1f, 1f });
            foreach (MethodInfo method in page.GetType().GetMethods())
            {
                if (method.Name == "CreateFloat" && method.GetParameters().Length == 7)
                {
                    method.Invoke(page, new object[] { name, white, value, step, min, max, callback });
                    return;
                }
            }
        }

        private static object MemberOrGetter(object instance, params string[] names)
        {
            if (instance == null) return null;
            foreach (string name in names)
            {
                object value = GetMember(instance, name);
                if (value != null) return value;
                value = CallNoArg(instance, "get_" + name);
                if (value != null) return value;
            }
            return null;
        }

        private static object CallNoArg(object instance, string method)
        {
            try
            {
                return instance?.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, null);
            }
            catch { return null; }
        }

        private static bool SameObject(object a, object b)
        {
            if (a == null || b == null) return false;
            int idA = GetId(a);
            int idB = GetId(b);
            return (idA != 0 && idA == idB) || ReferenceEquals(a, b);
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType(name, false);
                if (found != null) return found;
            }
            return Type.GetType(name + ", UnityEngine.CoreModule") ??
                   Type.GetType(name + ", BoneLib") ??
                   Type.GetType(name);
        }

        private static object GetStatic(Type type, string name)
        {
            try
            {
                if (type == null) return null;
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property != null) return property.GetValue(null);
                return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            }
            catch { return null; }
        }

        private static object GetMember(object instance, string name)
        {
            try
            {
                if (instance == null) return null;
                PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (property != null) return property.GetValue(instance);
                return instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)?.GetValue(instance);
            }
            catch { return null; }
        }

        private static void SetMember(object instance, string name, object value)
        {
            try
            {
                if (instance == null) return;
                PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(instance, value);
                    return;
                }
                instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(instance, value);
            }
            catch { }
        }

        private static object Call(object instance, string name, params object[] args)
        {
            try
            {
                if (instance == null) return null;
                foreach (MethodInfo method in instance.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (method.Name == name && method.GetParameters().Length == args.Length)
                        return method.Invoke(instance, args);
                }
            }
            catch { }
            return null;
        }

        private static string Name(object instance)
        {
            return Convert.ToString(MemberOrGetter(instance, "name", "Name")) ?? "";
        }

        private static int GetId(object instance)
        {
            try
            {
                return Convert.ToInt32(instance.GetType().GetMethod("GetInstanceID")?.Invoke(instance, null));
            }
            catch { return 0; }
        }

        private static float Component(object vector, string component)
        {
            try { return Convert.ToSingle(GetMember(vector, component)); }
            catch { return 0f; }
        }

        private static float TimeValue(string property)
        {
            try
            {
                Type timeType = FindType("UnityEngine.Time");
                return Convert.ToSingle(timeType.GetProperty(property, BindingFlags.Public | BindingFlags.Static)?.GetValue(null));
            }
            catch { return property == "deltaTime" ? 0.016f : 0f; }
        }

        private static object Vector(float x, float y, float z)
        {
            try { return Activator.CreateInstance(FindType("UnityEngine.Vector3"), new object[] { x, y, z }); }
            catch { return null; }
        }

        private static object Euler(float x, float y, float z)
        {
            try
            {
                return FindType("UnityEngine.Quaternion").GetMethod("Euler", new[] { typeof(float), typeof(float), typeof(float) }).Invoke(null, new object[] { x, y, z });
            }
            catch { return null; }
        }

        private static object Multiply(object a, object b)
        {
            try
            {
                return a.GetType().GetMethod("op_Multiply", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { a, b });
            }
            catch { return null; }
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        private static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }
    }
}