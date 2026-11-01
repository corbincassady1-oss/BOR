using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(StretchFlexMotion.Mod), "Stretch Flex Motion", "1.1.0", "BOR")]
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
        private object rigRoot;
        private System.Collections.IEnumerable bones;
        private float nextScan;
        private float lastY;
        private bool menuMade;
        private bool loggedRig;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("StretchFlexMotion", "Stretch Flex Motion");
            enabled = cat.CreateEntry("Enabled", true);
            stretchiness = cat.CreateEntry("Stretchiness", 1.0f);
            flexibility = cat.CreateEntry("Flexibility", 1.0f);
            stretchSpeed = cat.CreateEntry("Stretch Speed", 4.0f);
            flexSpeed = cat.CreateEntry("Flex Speed", 4.0f);
            armStretch = cat.CreateEntry("Arm Stretch", 1.0f);
            legStretch = cat.CreateEntry("Leg Stretch", 1.0f);
            spineStretch = cat.CreateEntry("Spine Stretch", 1.0f);
            hipFlex = cat.CreateEntry("Hip Flexibility", 1.0f);
            maxBend = cat.CreateEntry("Maximum Bend", 30.0f);
            MelonLogger.Msg("[Stretch Flex Motion] Loaded 1.1.0; waiting for player rig.");
        }
        public override void OnLateInitializeMelon() => TryMenu();
        public override void OnDeinitializeMelon() => Restore();
        public override void OnUpdate()
        {
            if (!enabled.Value) { Restore(); return; }
            float now = GetTime();
            if (now >= nextScan) { nextScan = now + 0.25f; FindRig(); TryMenu(); }
        }
        public override void OnLateUpdate() { if (enabled.Value && rigRoot != null && bones != null) Apply(); }

        private void FindRig()
        {
            try
            {
                object root = null;
                var playerType = FindType("BoneLib.Player");
                var rm = GetStatic(playerType, "RigManager") ?? GetStatic(playerType, "rigManager");
                if (rm != null) root = GetMember(rm, "transform");
                if (root == null)
                {
                    var rmType = FindRigManagerType();
                    if (rmType != null)
                    {
                        var component = FindFirstObjectOfType(rmType);
                        if (component != null) root = GetMember(component, "transform");
                    }
                }
                if (root == null) return;
                if (!ReferenceEquals(root, rigRoot))
                {
                    Restore(); baseScale.Clear(); baseRotation.Clear(); rigRoot = root; loggedRig = false;
                }
                bones = GetChildren(root);
                lastY = GetFloat(GetMember(root, "position"), "y");
                if (!loggedRig && bones != null) { loggedRig = true; MelonLogger.Msg("[Stretch Flex Motion] Player rig acquired and bones scanned."); }
            }
            catch (Exception e) { MelonLogger.Warning("[Stretch Flex Motion] Rig scan: " + e.Message); }
        }

        private void Apply()
        {
            try
            {
                float time = GetTime(); float dt = Math.Max(GetDelta(), 0.001f);
                object pos = GetMember(rigRoot, "position"); float y = GetFloat(pos, "y");
                float velocity = Math.Abs((y - lastY) / dt); lastY = y;
                float motion = Clamp01((velocity * 0.45f) + 0.25f);
                float stretch = Clamp(stretchiness.Value, 0f, 3f) * (0.35f + motion * 0.65f);
                float flex = Clamp(flexibility.Value, 0f, 3f); float bendLimit = Clamp(maxBend.Value, 0f, 90f);
                float smooth = 1f - (float)Math.Exp(-Math.Max(stretchSpeed.Value, 0.1f) * dt);
                float rotSmooth = Clamp01((1f + Math.Max(flexSpeed.Value, 0.1f)) * dt);
                foreach (object tr in bones)
                {
                    if (tr == null || ReferenceEquals(tr, rigRoot)) continue;
                    string name = GetName(tr).ToLowerInvariant(); float group = GroupAmount(name); if (group <= 0f) continue;
                    if (!baseScale.ContainsKey(tr)) baseScale[tr] = GetMember(tr, "localScale");
                    if (!baseRotation.ContainsKey(tr)) baseRotation[tr] = GetMember(tr, "localRotation");
                    object scale = baseScale[tr];
                    if (scale != null)
                    {
                        float factor = 1f + stretch * group * 0.30f;
                        object targetScale = ScaleVector(scale, factor, name); object currentScale = GetMember(tr, "localScale");
                        if (currentScale != null && targetScale != null) SetMember(tr, "localScale", LerpVector(currentScale, targetScale, smooth));
                    }
                    object originalRot = baseRotation[tr]; if (originalRot == null) continue;
                    float phase = (GetId(tr) % 1000) * 0.017f;
                    float wave = (float)Math.Sin(time * Math.Max(flexSpeed.Value, 0.1f) + phase);
                    float side = (float)Math.Cos(time * Math.Max(flexSpeed.Value, 0.1f) * 0.7f + phase);
                    float bend = wave * bendLimit * 0.35f * flex * group; float twist = side * bendLimit * 0.20f * flex * group;
                    object targetRot = MultiplyRotation(originalRot, Euler(bend, twist, bend * 0.25f)); object currentRot = GetMember(tr, "localRotation");
                    if (currentRot != null && targetRot != null) SetMember(tr, "localRotation", Slerp(currentRot, targetRot, rotSmooth));
                }
            }
            catch (Exception e) { MelonLogger.Warning("[Stretch Flex Motion] Motion update: " + e.Message); }
        }

        private float GroupAmount(string n)
        {
            if (n.Contains("upperarm") || n.Contains("upper_arm") || n.Contains("lowerarm") || n.Contains("lower_arm") || n.Contains("forearm") || n.Contains("arm") || n.Contains("hand")) return Clamp(armStretch.Value, 0f, 3f);
            if (n.Contains("thigh") || n.Contains("upperleg") || n.Contains("upper_leg") || n.Contains("lowerleg") || n.Contains("lower_leg") || n.Contains("calf") || n.Contains("shin") || n.Contains("leg") || n.Contains("foot") || n.Contains("toe")) return Clamp(legStretch.Value, 0f, 3f);
            if (n.Contains("spine") || n.Contains("chest") || n.Contains("abdomen") || n.Contains("torso") || n.Contains("neck")) return Clamp(spineStretch.Value, 0f, 3f);
            if (n.Contains("pelvis") || n.Contains("hip") || n.Contains("hips")) return Clamp(hipFlex.Value, 0f, 3f);
            return 0f;
        }
        private void Restore()
        {
            try { foreach (var p in baseScale) if (p.Value != null) SetMember(p.Key, "localScale", p.Value); foreach (var p in baseRotation) if (p.Value != null) SetMember(p.Key, "localRotation", p.Value); } catch { }
        }
        private void TryMenu()
        {
            if (menuMade) return;
            try
            {
                var pageType = FindType("BoneLib.BoneMenu.Page"); if (pageType == null) return;
                object root = pageType.GetProperty("Root", BindingFlags.Public | BindingFlags.Static)?.GetValue(null); if (root == null) return;
                var colorType = FindType("UnityEngine.Color"); if (colorType == null) return;
                object cyan = Activator.CreateInstance(colorType, new object[] { 0.1f, 0.9f, 1f, 1f });
                MethodInfo createPage = pageType.GetMethod("CreatePage", new[] { typeof(string), colorType, typeof(int), typeof(bool) }); if (createPage == null) return;
                object page = createPage.Invoke(root, new object[] { "Stretch Flex Motion", cyan, 0, true }); if (page == null) return;
                CreateBool(page, "Enabled", enabled.Value, v => enabled.Value = v);
                CreateFloat(page, "Stretchiness", stretchiness.Value, 0.1f, 0f, 3f, v => stretchiness.Value = v);
                CreateFloat(page, "Flexibility", flexibility.Value, 0.1f, 0f, 3f, v => flexibility.Value = v);
                CreateFloat(page, "Stretch Speed", stretchSpeed.Value, 0.1f, 0.5f, 10f, v => stretchSpeed.Value = v);
                CreateFloat(page, "Flex Speed", flexSpeed.Value, 0.1f, 0.5f, 10f, v => flexSpeed.Value = v);
                CreateFloat(page, "Arm Stretch", armStretch.Value, 0.1f, 0f, 3f, v => armStretch.Value = v);
                CreateFloat(page, "Leg Stretch", legStretch.Value, 0.1f, 0f, 3f, v => legStretch.Value = v);
                CreateFloat(page, "Spine Stretch", spineStretch.Value, 0.1f, 0f, 3f, v => spineStretch.Value = v);
                CreateFloat(page, "Hip Flexibility", hipFlex.Value, 0.1f, 0f, 3f, v => hipFlex.Value = v);
                CreateFloat(page, "Maximum Bend", maxBend.Value, 1f, 0f, 90f, v => maxBend.Value = v); menuMade = true;
            }
            catch (Exception e) { MelonLogger.Warning("[Stretch Flex Motion] BoneMenu unavailable: " + e.Message); }
        }
        private void CreateBool(object page, string name, bool value, Action<bool> cb)
        { var colorType = FindType("UnityEngine.Color"); object white = Activator.CreateInstance(colorType, new object[] { 1f, 1f, 1f, 1f }); foreach (var method in page.GetType().GetMethods()) if (method.Name == "CreateBool" && method.GetParameters().Length == 4) { method.Invoke(page, new object[] { name, white, value, cb }); return; } }
        private void CreateFloat(object page, string name, float value, float step, float min, float max, Action<float> cb)
        { var colorType = FindType("UnityEngine.Color"); object white = Activator.CreateInstance(colorType, new object[] { 1f, 1f, 1f, 1f }); foreach (var method in page.GetType().GetMethods()) if (method.Name == "CreateFloat" && method.GetParameters().Length == 7) { method.Invoke(page, new object[] { name, white, value, step, min, max, cb }); return; } }

        private static Type FindRigManagerType()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var exact = a.GetType("Il2CppSLZ.Marrow.RigManager", false) ?? a.GetType("SLZ.Marrow.RigManager", false); if (exact != null) return exact;
                    foreach (var t in a.GetTypes()) if (t.Name == "RigManager") return t;
                }
                catch { }
            }
            return null;
        }
        private static object FindFirstObjectOfType(Type t)
        {
            try
            {
                var objectType = FindType("UnityEngine.Object");
                var method = objectType.GetMethod("FindFirstObjectByType", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null) ?? objectType.GetMethod("FindObjectOfType", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null);
                return method?.Invoke(null, new object[] { t });
            }
            catch { return null; }
        }
        private static System.Collections.IEnumerable GetChildren(object root)
        {
            try
            {
                var componentType = FindType("UnityEngine.Component"); var transformType = FindType("UnityEngine.Transform");
                var method = componentType.GetMethod("GetComponentsInChildren", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Type), typeof(bool) }, null);
                if (method != null) return method.Invoke(root, new object[] { transformType, true }) as System.Collections.IEnumerable;
            }
            catch { }
            return null;
        }
        private static Type FindType(string name)
        { foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType(name, false); if (t != null) return t; } return Type.GetType(name + ", UnityEngine.CoreModule") ?? Type.GetType(name + ", BoneLib") ?? Type.GetType(name); }
        private static object GetStatic(Type t, string name) { if (t == null) return null; var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); if (p != null) return p.GetValue(null); return t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null); }
        private static object GetMember(object o, string name) { if (o == null) return null; var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); if (p != null) return p.GetValue(o); var f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); return f?.GetValue(o); }
        private static void SetMember(object o, string name, object v) { if (o == null) return; var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); if (p != null && p.CanWrite) { p.SetValue(o, v); return; } o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(o, v); }
        private static string GetName(object o) => Convert.ToString(GetMember(o, "name")) ?? "";
        private static int GetId(object o) { try { return Convert.ToInt32(o.GetType().GetMethod("GetInstanceID")?.Invoke(o, null)); } catch { return 1; } }
        private static float GetFloat(object v, string p) { try { return Convert.ToSingle(GetMember(v, p)); } catch { return 0f; } }
        private static float GetTime() { try { return Convert.ToSingle(FindType("UnityEngine.Time").GetProperty("time").GetValue(null)); } catch { return 0f; } }
        private static float GetDelta() { try { return Convert.ToSingle(FindType("UnityEngine.Time").GetProperty("deltaTime").GetValue(null)); } catch { return 0.016f; } }
        private static object MakeVector(float x, float y, float z) => Activator.CreateInstance(FindType("UnityEngine.Vector3"), new object[] { x, y, z });
        private static float Component(object v, string p) => GetFloat(v, p);
        private static object ScaleVector(object v, float factor, string name) { float x = Component(v, "x"), y = Component(v, "y"), z = Component(v, "z"); if (name.Contains("upperarm") || name.Contains("forearm") || name.Contains("thigh") || name.Contains("calf") || name.Contains("shin") || name.Contains("spine") || name.Contains("chest") || name.Contains("abdomen") || name.Contains("leg") || name.Contains("arm")) return MakeVector(x, y * factor, z); return MakeVector(x * (1f + (factor - 1f) * 0.35f), y * factor, z * (1f + (factor - 1f) * 0.35f)); }
        private static object LerpVector(object a, object b, float t) => FindType("UnityEngine.Vector3").GetMethod("Lerp", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { a, b, t });
        private static object Euler(float x, float y, float z) => FindType("UnityEngine.Quaternion").GetMethod("Euler", new[] { typeof(float), typeof(float), typeof(float) }).Invoke(null, new object[] { x, y, z });
        private static object MultiplyRotation(object a, object b) => a.GetType().GetMethod("op_Multiply", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { a, b });
        private static object Slerp(object a, object b, float t) => FindType("UnityEngine.Quaternion").GetMethod("Slerp", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { a, b, t });
        private static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        private static float Clamp01(float v) => Clamp(v, 0f, 1f);
    }
}