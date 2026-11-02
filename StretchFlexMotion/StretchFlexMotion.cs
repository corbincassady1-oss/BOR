using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(StretchFlexMotion.Mod), "Stretch Flex Motion", "1.2.0", "BOR")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace StretchFlexMotion
{
    public class Mod : MelonMod
    {
        private MelonPreferences_Entry<bool> enabled;
        private MelonPreferences_Entry<float> stretchiness;
        private MelonPreferences_Entry<float> flexibility;
        private MelonPreferences_Entry<float> stretchSpeed;
        private MelonPreferences_Entry<float> flexSpeed;
        private MelonPreferences_Entry<float> armStretch;
        private MelonPreferences_Entry<float> legStretch;
        private MelonPreferences_Entry<float> spineStretch;
        private MelonPreferences_Entry<float> hipFlex;
        private MelonPreferences_Entry<float> maxBend;

        private readonly Dictionary<object, object> baseScale = new Dictionary<object, object>();
        private readonly Dictionary<object, object> baseRotation = new Dictionary<object, object>();
        private readonly List<object> bones = new List<object>();
        private object rigManager;
        private object rigRoot;
        private float nextScan;
        private float nextLog;
        private float lastY;
        private bool menuMade;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("StretchFlexMotion", "Stretch Flex Motion");
            enabled = cat.CreateEntry("Enabled", true);
            stretchiness = cat.CreateEntry("Stretchiness", 1.5f);
            flexibility = cat.CreateEntry("Flexibility", 1.5f);
            stretchSpeed = cat.CreateEntry("Stretch Speed", 4.0f);
            flexSpeed = cat.CreateEntry("Flex Speed", 4.0f);
            armStretch = cat.CreateEntry("Arm Stretch", 1.5f);
            legStretch = cat.CreateEntry("Leg Stretch", 1.5f);
            spineStretch = cat.CreateEntry("Spine Stretch", 1.5f);
            hipFlex = cat.CreateEntry("Hip Flexibility", 1.5f);
            maxBend = cat.CreateEntry("Maximum Bend", 35.0f);
            MelonLogger.Msg("[Stretch Flex Motion] 1.2.0 loaded.");
        }

        public override void OnLateInitializeMelon() => TryMenu();

        public override void OnUpdate()
        {
            if (!enabled.Value)
            {
                Restore();
                return;
            }

            float now = GetTime();
            if (now >= nextScan)
            {
                nextScan = now + 0.5f;
                FindPlayerRig();
                TryMenu();
            }
        }

        public override void OnLateUpdate()
        {
            if (!enabled.Value || bones.Count == 0) return;
            Apply();
        }

        public override void OnDeinitializeMelon() => Restore();

        private void FindPlayerRig()
        {
            try
            {
                object playerType = FindType("BoneLib.Player");
                object manager = GetStatic((Type)playerType, "RigManager");
                if (manager == null) manager = GetStatic((Type)playerType, "rigManager");

                if (manager == null)
                {
                    Type rmType = FindRigManagerType();
                    if (rmType != null) manager = FindFirstObjectOfType(rmType);
                }

                if (manager == null) return;

                object physicsRig = GetMember(manager, "physicsRig");
                object root = physicsRig != null ? GetMember(physicsRig, "transform") : GetMember(manager, "transform");
                if (root == null) return;

                if (!SameObject(manager, rigManager) || !SameObject(root, rigRoot))
                {
                    Restore();
                    baseScale.Clear();
                    baseRotation.Clear();
                    rigManager = manager;
                    rigRoot = root;
                    bones.Clear();
                }

                if (bones.Count == 0)
                {
                    CollectTransforms(root);
                    lastY = GetFloat(GetMember(root, "position"), "y");
                    MelonLogger.Msg("[Stretch Flex Motion] Found player rig. Transform count: " + bones.Count);
                }

                if (GetTime() >= nextLog)
                {
                    nextLog = GetTime() + 5f;
                    MelonLogger.Msg("[Stretch Flex Motion] Applying to " + bones.Count + " transforms.");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stretch Flex Motion] Rig discovery: " + e.Message);
            }
        }

        private void CollectTransforms(object root)
        {
            try
            {
                object countValue = GetMember(root, "childCount");
                int count = Convert.ToInt32(countValue);
                for (int i = 0; i < count; i++)
                {
                    object child = Call(root, "GetChild", i);
                    if (child == null) continue;
                    bones.Add(child);
                    CollectTransforms(child);
                }
            }
            catch { }
        }

        private void Apply()
        {
            try
            {
                float time = GetTime();
                float dt = Math.Max(GetDelta(), 0.001f);
                object pos = GetMember(rigRoot, "position");
                float y = GetFloat(pos, "y");
                float velocity = Math.Abs((y - lastY) / dt);
                lastY = y;

                float movement = Clamp01(velocity * 0.5f + 0.25f);
                float stretch = Clamp(stretchiness.Value, 0f, 3f) * (0.55f + movement * 0.45f);
                float flex = Clamp(flexibility.Value, 0f, 3f);
                float bendLimit = Clamp(maxBend.Value, 0f, 90f);
                float scaleLerp = Clamp01(1f - (float)Math.Exp(-Math.Max(stretchSpeed.Value, 0.1f) * dt));
                float rotLerp = Clamp01(1f - (float)Math.Exp(-Math.Max(flexSpeed.Value, 0.1f) * dt));

                foreach (object tr in bones)
                {
                    if (tr == null) continue;
                    string name = GetName(tr).ToLowerInvariant();
                    float group = GroupAmount(name);
                    if (group <= 0f) continue;

                    if (!baseScale.ContainsKey(tr)) baseScale[tr] = GetMember(tr, "localScale");
                    if (!baseRotation.ContainsKey(tr)) baseRotation[tr] = GetMember(tr, "localRotation");

                    object originalScale = baseScale[tr];
                    object currentScale = GetMember(tr, "localScale");
                    if (originalScale != null && currentScale != null)
                    {
                        float amount = 1f + stretch * group * 0.40f;
                        object target = ScaleForBone(originalScale, amount, name);
                        if (target != null) SetMember(tr, "localScale", LerpVector(currentScale, target, scaleLerp));
                    }

                    object originalRotation = baseRotation[tr];
                    object currentRotation = GetMember(tr, "localRotation");
                    if (originalRotation != null && currentRotation != null)
                    {
                        float phase = (GetId(tr) % 1000) * 0.013f;
                        float wave = (float)Math.Sin(time * Math.Max(flexSpeed.Value, 0.1f) + phase);
                        float side = (float)Math.Cos(time * Math.Max(flexSpeed.Value, 0.1f) * 0.73f + phase);
                        float bend = wave * bendLimit * 0.50f * flex * group;
                        float twist = side * bendLimit * 0.30f * flex * group;
                        object targetRot = MultiplyRotation(originalRotation, Euler(bend, twist, bend * 0.20f));
                        if (targetRot != null) SetMember(tr, "localRotation", Slerp(currentRotation, targetRot, rotLerp));
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stretch Flex Motion] Apply: " + e.Message);
            }
        }

        private float GroupAmount(string n)
        {
            if (n.Contains("upperarm") || n.Contains("upper_arm") || n.Contains("lowerarm") || n.Contains("lower_arm") || n.Contains("forearm") || n.Contains("arm") || n.Contains("hand")) return Clamp(armStretch.Value, 0f, 3f);
            if (n.Contains("thigh") || n.Contains("upperleg") || n.Contains("upper_leg") || n.Contains("lowerleg") || n.Contains("lower_leg") || n.Contains("calf") || n.Contains("shin") || n.Contains("leg") || n.Contains("foot") || n.Contains("toe")) return Clamp(legStretch.Value, 0f, 3f);
            if (n.Contains("spine") || n.Contains("chest") || n.Contains("abdomen") || n.Contains("torso") || n.Contains("neck")) return Clamp(spineStretch.Value, 0f, 3f);
            if (n.Contains("pelvis") || n.Contains("hip") || n.Contains("hips")) return Clamp(hipFlex.Value, 0f, 3f);
            return 0f;
        }

        private object ScaleForBone(object v, float factor, string name)
        {
            float x = Component(v, "x"), y = Component(v, "y"), z = Component(v, "z");
            if (name.Contains("upperarm") || name.Contains("forearm") || name.Contains("thigh") || name.Contains("calf") || name.Contains("shin") || name.Contains("spine") || name.Contains("chest") || name.Contains("abdomen") || name.Contains("leg") || name.Contains("arm"))
                return MakeVector(x, y * factor, z);
            return MakeVector(x * (1f + (factor - 1f) * 0.30f), y * factor, z * (1f + (factor - 1f) * 0.30f));
        }

        private void Restore()
        {
            try
            {
                foreach (var p in baseScale) if (p.Value != null) SetMember(p.Key, "localScale", p.Value);
                foreach (var p in baseRotation) if (p.Value != null) SetMember(p.Key, "localRotation", p.Value);
            }
            catch { }
        }

        private void TryMenu()
        {
            if (menuMade) return;
            try
            {
                Type pageType = FindType("BoneLib.BoneMenu.Page");
                if (pageType == null) return;
                object root = pageType.GetProperty("Root", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (root == null) return;
                Type colorType = FindType("UnityEngine.Color");
                object cyan = Activator.CreateInstance(colorType, new object[] { 0.1f, 0.9f, 1f, 1f });
                MethodInfo createPage = pageType.GetMethod("CreatePage", new[] { typeof(string), colorType, typeof(int), typeof(bool) });
                if (createPage == null) return;
                object page = createPage.Invoke(root, new object[] { "Stretch Flex Motion", cyan, 0, true });
                if (page == null) return;
                CreateBool(page, "Enabled", enabled.Value, v => enabled.Value = v);
                CreateFloat(page, "Stretchiness", stretchiness.Value, 0.1f, 0f, 3f, v => stretchiness.Value = v);
                CreateFloat(page, "Flexibility", flexibility.Value, 0.1f, 0f, 3f, v => flexibility.Value = v);
                CreateFloat(page, "Stretch Speed", stretchSpeed.Value, 0.1f, 0.5f, 10f, v => stretchSpeed.Value = v);
                CreateFloat(page, "Flex Speed", flexSpeed.Value, 0.1f, 0.5f, 10f, v => flexSpeed.Value = v);
                CreateFloat(page, "Arm Stretch", armStretch.Value, 0.1f, 0f, 3f, v => armStretch.Value = v);
                CreateFloat(page, "Leg Stretch", legStretch.Value, 0.1f, 0f, 3f, v => legStretch.Value = v);
                CreateFloat(page, "Spine Stretch", spineStretch.Value, 0.1f, 0f, 3f, v => spineStretch.Value = v);
                CreateFloat(page, "Hip Flexibility", hipFlex.Value, 0.1f, 0f, 3f, v => hipFlex.Value = v);
                CreateFloat(page, "Maximum Bend", maxBend.Value, 1f, 0f, 90f, v => maxBend.Value = v);
                menuMade = true;
            }
            catch (Exception e) { MelonLogger.Warning("[Stretch Flex Motion] BoneMenu: " + e.Message); }
        }

        private void CreateBool(object page, string name, bool value, Action<bool> cb)
        {
            Type colorType = FindType("UnityEngine.Color");
            object white = Activator.CreateInstance(colorType, new object[] { 1f, 1f, 1f, 1f });
            foreach (var m in page.GetType().GetMethods()) if (m.Name == "CreateBool" && m.GetParameters().Length == 4) { m.Invoke(page, new object[] { name, white, value, cb }); return; }
        }

        private void CreateFloat(object page, string name, float value, float step, float min, float max, Action<float> cb)
        {
            Type colorType = FindType("UnityEngine.Color");
            object white = Activator.CreateInstance(colorType, new object[] { 1f, 1f, 1f, 1f });
            foreach (var m in page.GetType().GetMethods()) if (m.Name == "CreateFloat" && m.GetParameters().Length == 7) { m.Invoke(page, new object[] { name, white, value, step, min, max, cb }); return; }
        }

        private static bool SameObject(object a, object b) { return a != null && b != null && ReferenceEquals(a, b); }
        private static Type FindRigManagerType()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = a.GetType("Il2CppSLZ.Marrow.RigManager", false) ?? a.GetType("SLZ.Marrow.RigManager", false);
                    if (t != null) return t;
                    foreach (var x in a.GetTypes()) if (x.Name == "RigManager") return x;
                }
                catch { }
            }
            return null;
        }
        private static object FindFirstObjectOfType(Type t)
        {
            try
            {
                Type objectType = FindType("UnityEngine.Object");
                MethodInfo m = objectType.GetMethod("FindFirstObjectByType", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null) ?? objectType.GetMethod("FindObjectOfType", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null);
                return m?.Invoke(null, new object[] { t });
            }
            catch { return null; }
        }
        private static Type FindType(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { Type t = a.GetType(name, false); if (t != null) return t; }
            return Type.GetType(name + ", UnityEngine.CoreModule") ?? Type.GetType(name + ", BoneLib") ?? Type.GetType(name);
        }
        private static object GetStatic(Type t, string name) { if (t == null) return null; var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); if (p != null) return p.GetValue(null); return t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null); }
        private static object GetMember(object o, string name) { if (o == null) return null; var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); if (p != null) return p.GetValue(o); var f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); return f?.GetValue(o); }
        private static void SetMember(object o, string name, object v) { if (o == null) return; var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); if (p != null && p.CanWrite) { p.SetValue(o, v); return; } o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(o, v); }
        private static object Call(object o, string name, params object[] args) { try { var methods = o.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); foreach (var m in methods) if (m.Name == name && m.GetParameters().Length == args.Length) return m.Invoke(o, args); } catch { } return null; }
        private static string GetName(object o) => Convert.ToString(GetMember(o, "name")) ?? "";
        private static int GetId(object o) { try { return Convert.ToInt32(o.GetType().GetMethod("GetInstanceID")?.Invoke(o, null)); } catch { return 1; } }
        private static float GetFloat(object v, string p) { try { return Convert.ToSingle(GetMember(v, p)); } catch { return 0f; } }
        private static float GetTime() { try { return Convert.ToSingle(FindType("UnityEngine.Time").GetProperty("time").GetValue(null)); } catch { return 0f; } }
        private static float GetDelta() { try { return Convert.ToSingle(FindType("UnityEngine.Time").GetProperty("deltaTime").GetValue(null)); } catch { return 0.016f; } }
        private static object MakeVector(float x, float y, float z) => Activator.CreateInstance(FindType("UnityEngine.Vector3"), new object[] { x, y, z });
        private static float Component(object v, string p) => GetFloat(v, p);
        private static object LerpVector(object a, object b, float t) => FindType("UnityEngine.Vector3").GetMethod("Lerp", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { a, b, t });
        private static object Euler(float x, float y, float z) => FindType("UnityEngine.Quaternion").GetMethod("Euler", new[] { typeof(float), typeof(float), typeof(float) }).Invoke(null, new object[] { x, y, z });
        private static object MultiplyRotation(object a, object b) { try { return a.GetType().GetMethod("op_Multiply", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { a, b }); } catch { return null; } }
        private static object Slerp(object a, object b, float t) => FindType("UnityEngine.Quaternion").GetMethod("Slerp", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { a, b, t });
        private static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        private static float Clamp01(float v) => Clamp(v, 0f, 1f);
    }
}
