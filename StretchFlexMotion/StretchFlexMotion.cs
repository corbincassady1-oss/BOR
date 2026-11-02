using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(StretchFlexMotion.Mod), "Stretch Flex Motion", "1.6.0", "BOR")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace StretchFlexMotion
{
    public class Mod : MelonMod
    {
        private MelonPreferences_Entry<bool> enabled;
        private MelonPreferences_Entry<bool> flexibleWaist;
        private MelonPreferences_Entry<bool> safeDefaults;
        private MelonPreferences_Entry<float> stretchiness;
        private MelonPreferences_Entry<float> flexibility;
        private MelonPreferences_Entry<float> armFlex;
        private MelonPreferences_Entry<float> legFlex;
        private MelonPreferences_Entry<float> spineFlex;
        private MelonPreferences_Entry<float> hipFlex;
        private MelonPreferences_Entry<float> waistFlex;
        private MelonPreferences_Entry<float> maxBend;

        private const int Arms = 1;
        private const int Legs = 2;
        private const int Spine = 3;
        private const int Hips = 4;
        private const int Waist = 5;
        private const int Chest = 6;

        private sealed class JointState
        {
            public object Joint;
            public string Name;
            public int Group;
            public object AngularXDrive;
            public object AngularYZDrive;
            public object SlerpDrive;
            public object LowAngularXLimit;
            public object HighAngularXLimit;
            public object AngularYLimit;
            public object AngularZLimit;
            public object AngularXMotion;
            public object AngularYMotion;
            public object AngularZMotion;
        }

        private readonly List<JointState> jointStates = new List<JointState>();
        private object physicsRig;
        private object physicsRoot;
        private Type configurableJointType;
        private float nextScan;
        private bool menuMade;
        private bool wasEnabled;
        private bool waitingLogged;
        private bool scanComplete;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("StretchFlexMotion", "Stretch Flex Motion");
            enabled = cat.CreateEntry("Enabled", false);
            flexibleWaist = cat.CreateEntry("Flexible Waist", true);
            safeDefaults = cat.CreateEntry("SafeDefaults160", false);
            stretchiness = cat.CreateEntry("Stretchiness", 1.5f);
            flexibility = cat.CreateEntry("Flexibility", 1.5f);
            armFlex = cat.CreateEntry("Arm Flexibility", 1.5f);
            legFlex = cat.CreateEntry("Leg Flexibility", 1.5f);
            spineFlex = cat.CreateEntry("Chest Flexibility", 1.5f);
            hipFlex = cat.CreateEntry("Hip Flexibility", 1.5f);
            waistFlex = cat.CreateEntry("Waist Flexibility", 1.5f);
            maxBend = cat.CreateEntry("Maximum Bend", 25f);

            // New physics-joint implementation is opt-in, regardless of a prior build's saved toggle.
            if (!safeDefaults.Value)
            {
                enabled.Value = false;
                safeDefaults.Value = true;
            }

            MelonLogger.Msg("[Stretch Flex Motion] 1.6.0 loaded. One-shot physics-joint mode; disabled until enabled.");
        }

        public override void OnLateInitializeMelon()
        {
            TryMenu();
        }

        public override void OnUpdate()
        {
            if (!enabled.Value)
            {
                if (wasEnabled || jointStates.Count > 0)
                    RestoreJoints();

                wasEnabled = false;
                TryMenu();
                return;
            }

            if (!wasEnabled)
            {
                wasEnabled = true;
                nextScan = 0f;
                waitingLogged = false;
                jointStates.Clear();
                physicsRig = null;
                physicsRoot = null;
                configurableJointType = null;
                scanComplete = false;
            }

            // Poll only while waiting for the player physics rig. As soon as the rig
            // is found, scan once even if no recognized joints are present; there is
            // no repeated hierarchy scan or physics write afterwards.
            if (!scanComplete && TimeValue("time") >= nextScan)
            {
                nextScan = TimeValue("time") + 0.4f;
                if (TryFindPhysicsRig())
                {
                    scanComplete = true;
                    ApplyJointSettingsOnce();
                }
            }

            TryMenu();
        }

        public override void OnDeinitializeMelon()
        {
            RestoreJoints();
        }

        private bool TryFindPhysicsRig()
        {
            try
            {
                Type playerType = FindType("BoneLib.Player");
                object rig = GetStatic(playerType, "PhysicsRig");

                if (rig == null)
                {
                    MethodInfo getter = playerType?.GetMethod("GetPhysicsRig", BindingFlags.Public | BindingFlags.Static);
                    if (getter != null)
                        rig = getter.Invoke(null, null);
                }

                if (rig == null)
                {
                    LogWaiting("Waiting for BoneLib.Player.PhysicsRig.");
                    return false;
                }

                object root = MemberOrGetter(rig, "transform", "Transform");
                if (root == null)
                {
                    LogWaiting("PhysicsRig exists, but its transform is unavailable.");
                    return false;
                }

                configurableJointType = FindType("UnityEngine.ConfigurableJoint");
                if (configurableJointType == null)
                    configurableJointType = FindTypeBySimpleName("ConfigurableJoint");

                if (configurableJointType == null)
                {
                    LogWaiting("Unity ConfigurableJoint type was not found.");
                    return false;
                }

                physicsRig = rig;
                physicsRoot = root;
                return true;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stretch Flex Motion] Physics rig discovery: " + e.Message);
                return false;
            }
        }

        private void ApplyJointSettingsOnce()
        {
            try
            {
                int visited = CollectJoints(physicsRoot, 0);
                int modified = 0;
                int torsoCount = 0;

                foreach (JointState state in jointStates)
                {
                    if (state == null || state.Joint == null)
                        continue;

                    if (ApplyToJoint(state))
                    {
                        modified++;
                        if (state.Group == Waist || state.Group == Chest || state.Group == Hips)
                            torsoCount++;
                    }
                }

                MelonLogger.Msg("[Stretch Flex Motion] One-shot physics pass: modified " + modified +
                    " joints (" + torsoCount + " torso/waist/hip); visited " + visited +
                    " physics transforms. No per-frame writes are active.");

                if (modified == 0)
                    MelonLogger.Warning("[Stretch Flex Motion] No recognized body ConfigurableJoints were modified. Check the rig hierarchy in the log.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stretch Flex Motion] Joint apply failed: " + e.Message);
                RestoreJoints();
            }
        }

        private int CollectJoints(object parent, int depth)
        {
            if (parent == null || depth > 64 || jointStates.Count >= 256)
                return 0;

            int visited = 1;
            try
            {
                object joint = Call(parent, "GetComponent", configurableJointType);
                if (joint == null)
                {
                    object gameObject = MemberOrGetter(parent, "gameObject", "GameObject");
                    if (gameObject != null)
                        joint = Call(gameObject, "GetComponent", configurableJointType);
                }

                if (joint != null && !ContainsJoint(joint))
                {
                    string ownName = Name(parent);
                    object connectedBody = MemberOrGetter(joint, "connectedBody", "ConnectedBody");
                    object connectedTransform = connectedBody == null ? null : MemberOrGetter(connectedBody, "transform", "Transform");
                    string connectedName = Name(connectedTransform);
                    // Prefer the body carrying this joint. Only fall back to the connected
                    // body when the joint object's own transform has a generic name.
                    int group = ClassifyConnection(ownName, connectedName);

                    if (group != 0)
                    {
                        jointStates.Add(SnapshotJoint(joint, ownName + " <-> " + connectedName, group));
                    }
                }

                int count = Convert.ToInt32(MemberOrGetter(parent, "childCount", "ChildCount"));
                for (int i = 0; i < count && jointStates.Count < 256; i++)
                {
                    object child = Call(parent, "GetChild", i);
                    if (child != null)
                        visited += CollectJoints(child, depth + 1);
                }
            }
            catch { }

            return visited;
        }

        private bool ContainsJoint(object joint)
        {
            int id = GetId(joint);
            foreach (JointState state in jointStates)
            {
                if (SameObject(state.Joint, joint) || (id != 0 && GetId(state.Joint) == id))
                    return true;
            }
            return false;
        }

        private static JointState SnapshotJoint(object joint, string name, int group)
        {
            return new JointState
            {
                Joint = joint,
                Name = name,
                Group = group,
                AngularXDrive = CloneValue(MemberOrGetter(joint, "angularXDrive", "AngularXDrive")),
                AngularYZDrive = CloneValue(MemberOrGetter(joint, "angularYZDrive", "AngularYZDrive")),
                SlerpDrive = CloneValue(MemberOrGetter(joint, "slerpDrive", "SlerpDrive")),
                LowAngularXLimit = CloneValue(MemberOrGetter(joint, "lowAngularXLimit", "LowAngularXLimit")),
                HighAngularXLimit = CloneValue(MemberOrGetter(joint, "highAngularXLimit", "HighAngularXLimit")),
                AngularYLimit = CloneValue(MemberOrGetter(joint, "angularYLimit", "AngularYLimit")),
                AngularZLimit = CloneValue(MemberOrGetter(joint, "angularZLimit", "AngularZLimit")),
                AngularXMotion = MemberOrGetter(joint, "angularXMotion", "AngularXMotion"),
                AngularYMotion = MemberOrGetter(joint, "angularYMotion", "AngularYMotion"),
                AngularZMotion = MemberOrGetter(joint, "angularZMotion", "AngularZMotion")
            };
        }

        private bool ApplyToJoint(JointState state)
        {
            object joint = state.Joint;
            if (joint == null)
                return false;

            float control = GroupControl(state.Group);
            if (control <= 0f)
                return false;

            float amount = Clamp(control / 3f, 0f, 1f) * Clamp(flexibility.Value / 3f, 0f, 1f);
            float springScale = Clamp(1f - 0.78f * amount, 0.16f, 1f);
            float damperScale = Clamp(1f - 0.38f * amount, 0.45f, 1f);
            float limitScale = 1f + Clamp(stretchiness.Value, 0f, 3f) * 0.18f * Clamp(control / 3f, 0f, 1f);

            ApplyDrive(joint, "angularXDrive", state.AngularXDrive, springScale, damperScale);
            ApplyDrive(joint, "angularYZDrive", state.AngularYZDrive, springScale, damperScale);
            ApplyDrive(joint, "slerpDrive", state.SlerpDrive, springScale, damperScale);

            bool torsoJoint = state.Group == Waist || state.Group == Chest || state.Group == Hips;
            float requestedBend = Clamp(maxBend.Value, 5f, 65f);
            float baseBend = requestedBend * (state.Group == Waist ? 1f :
                                             state.Group == Chest ? 0.78f : 0.62f);
            if (torsoJoint)
            {
                ApplyMotionAndLimit(joint, "angularXMotion", state.AngularXMotion,
                    "lowAngularXLimit", state.LowAngularXLimit,
                    "highAngularXLimit", state.HighAngularXLimit,
                    limitScale, baseBend);

                ApplyMotionAndLimit(joint, "angularYMotion", state.AngularYMotion,
                    "angularYLimit", state.AngularYLimit,
                    limitScale, baseBend * 0.65f);

                ApplyMotionAndLimit(joint, "angularZMotion", state.AngularZMotion,
                    "angularZLimit", state.AngularZLimit,
                    limitScale, baseBend * 0.70f);
            }
            else
            {
                // For arms and legs, expand only axes already marked Limited.
                ExpandExistingLimit(joint, "angularXMotion", state.AngularXMotion,
                    "lowAngularXLimit", state.LowAngularXLimit,
                    "highAngularXLimit", state.HighAngularXLimit, limitScale);

                ExpandExistingLimit(joint, "angularYMotion", state.AngularYMotion,
                    "angularYLimit", state.AngularYLimit, limitScale);

                ExpandExistingLimit(joint, "angularZMotion", state.AngularZMotion,
                    "angularZLimit", state.AngularZLimit, limitScale);
            }

            return true;
        }

        private static void ApplyDrive(object joint, string property, object original, float springScale, float damperScale)
        {
            if (original == null)
                return;

            object changed = CloneValue(original);
            if (changed == null)
                return;

            object springObj = MemberOrGetter(changed, "positionSpring", "PositionSpring");
            object damperObj = MemberOrGetter(changed, "positionDamper", "PositionDamper");
            float spring = ToFloat(springObj);
            float damper = ToFloat(damperObj);

            SetMember(changed, "positionSpring", spring * springScale);
            SetMember(changed, "positionDamper", damper * damperScale);
            SetMember(joint, property, changed);
        }

        private static void ApplyMotionAndLimit(object joint, string motionProperty, object originalMotion,
            string limitProperty, object originalLimit, float scale, float minimumLimit)
        {
            if (originalMotion == null)
                return;

            int motion = EnumValue(originalMotion);
            if (motion == 0)
            {
                object limited = Enum.ToObject(originalMotion.GetType(), 1);
                SetMember(joint, motionProperty, limited);
            }

            ApplySoftLimit(joint, limitProperty, originalLimit, scale, minimumLimit);
        }

        private static void ApplyMotionAndLimit(object joint, string motionProperty, object originalMotion,
            string lowProperty, object originalLow, string highProperty, object originalHigh,
            float scale, float minimumLimit)
        {
            if (originalMotion == null)
                return;

            int motion = EnumValue(originalMotion);
            if (motion == 0)
            {
                object limited = Enum.ToObject(originalMotion.GetType(), 1);
                SetMember(joint, motionProperty, limited);
            }

            ApplySoftLimit(joint, lowProperty, originalLow, scale, -minimumLimit);
            ApplySoftLimit(joint, highProperty, originalHigh, scale, minimumLimit);
        }

        private static void ExpandExistingLimit(object joint, string motionProperty, object originalMotion,
            string limitProperty, object originalLimit, float scale)
        {
            if (originalMotion != null && EnumValue(originalMotion) == 1)
                ApplySoftLimit(joint, limitProperty, originalLimit, scale, 0f);
        }

        private static void ExpandExistingLimit(object joint, string motionProperty, object originalMotion,
            string lowProperty, object originalLow, string highProperty, object originalHigh, float scale)
        {
            if (originalMotion != null && EnumValue(originalMotion) == 1)
            {
                ApplySoftLimit(joint, lowProperty, originalLow, scale, 0f);
                ApplySoftLimit(joint, highProperty, originalHigh, scale, 0f);
            }
        }

        private static void ApplySoftLimit(object joint, string property, object original, float scale, float minimum)
        {
            if (original == null)
                return;

            object changed = CloneValue(original);
            if (changed == null)
                return;

            float originalLimit = ToFloat(MemberOrGetter(changed, "limit", "Limit"));
            float result = minimum < 0f
                ? Math.Min(originalLimit * scale, minimum)
                : Math.Max(Math.Abs(originalLimit) * scale, minimum);

            // Preserve original sign for existing signed axes, otherwise use the requested side.
            if (minimum == 0f && originalLimit < 0f)
                result = -Math.Abs(result);

            SetMember(changed, "limit", result);
            SetMember(joint, property, changed);
        }

        private float GroupControl(int group)
        {
            switch (group)
            {
                case Arms: return Clamp(armFlex.Value, 0f, 3f);
                case Legs: return Clamp(legFlex.Value, 0f, 3f);
                case Spine:
                case Chest: return Clamp(spineFlex.Value, 0f, 3f);
                case Hips: return Clamp(hipFlex.Value, 0f, 3f);
                case Waist: return flexibleWaist.Value ? Clamp(waistFlex.Value, 0f, 3f) : 0f;
                default: return 0f;
            }
        }

        private static int ClassifyConnection(string ownName, string connectedName)
        {
            string own = (ownName ?? "").ToLowerInvariant();
            string connected = (connectedName ?? "").ToLowerInvariant();
            string combined = own + " " + connected;

            bool hasPelvis = combined.Contains("pelvis") || combined.Contains("hip");
            bool hasUpperTorso = combined.Contains("chest") || combined.Contains("spine") ||
                                 combined.Contains("torso") || combined.Contains("abdomen");
            if (hasPelvis && hasUpperTorso)
                return Waist;

            int group = ClassifyJoint(ownName);
            if (group != 0)
                return group;

            group = ClassifyJoint(connectedName);
            if (group != 0)
                return group;

            return ClassifyJoint(combined);
        }

        private static int ClassifyJoint(string value)
        {
            string n = (value ?? "").ToLowerInvariant();

            if (n.Contains("waist") || n.Contains("abdomen") ||
                n.Contains("lowerback") || n.Contains("lower_back") ||
                n.Contains("lower-spine") || n.Contains("lower_spine") ||
                n.Contains("spine") || n.Contains("torso"))
                return Waist;

            if (n.Contains("chest") || n.Contains("upperchest") ||
                n.Contains("neck") || n.Contains("shoulderbase"))
                return Chest;

            if (n.Contains("pelvis") || n.Contains("hips") ||
                n.Contains("hip_l") || n.Contains("hip_r") ||
                n.Contains("lefthip") || n.Contains("righthip"))
                return Hips;

            if (n.Contains("upperarm") || n.Contains("lowerarm") ||
                n.Contains("forearm") || n.Contains("shoulder") ||
                n.Contains("elbow") || n.Contains("arm_l") ||
                n.Contains("arm_r") || n.Contains("bicep"))
                return Arms;

            if (n.Contains("thigh") || n.Contains("upperleg") ||
                n.Contains("lowerleg") || n.Contains("calf") ||
                n.Contains("shin") || n.Contains("knee") ||
                n.Contains("ankle") || n.Contains("foot") ||
                n.Contains("leglf") || n.Contains("legrt") ||
                n.Contains("leg_l") || n.Contains("leg_r"))
                return Legs;

            return 0;
        }

        private void RestoreJoints()
        {
            try
            {
                foreach (JointState state in jointStates)
                {
                    if (state == null || state.Joint == null)
                        continue;

                    object joint = state.Joint;
                    RestoreValue(joint, "angularXDrive", state.AngularXDrive);
                    RestoreValue(joint, "angularYZDrive", state.AngularYZDrive);
                    RestoreValue(joint, "slerpDrive", state.SlerpDrive);
                    RestoreValue(joint, "lowAngularXLimit", state.LowAngularXLimit);
                    RestoreValue(joint, "highAngularXLimit", state.HighAngularXLimit);
                    RestoreValue(joint, "angularYLimit", state.AngularYLimit);
                    RestoreValue(joint, "angularZLimit", state.AngularZLimit);
                    RestoreValue(joint, "angularXMotion", state.AngularXMotion);
                    RestoreValue(joint, "angularYMotion", state.AngularYMotion);
                    RestoreValue(joint, "angularZMotion", state.AngularZMotion);
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Stretch Flex Motion] Joint restore: " + e.Message);
            }

            jointStates.Clear();
            scanComplete = false;
            physicsRig = null;
            physicsRoot = null;
            configurableJointType = null;
        }

        private static void RestoreValue(object joint, string property, object value)
        {
            if (value != null)
                SetMember(joint, property, CloneValue(value));
        }

        private void LogWaiting(string message)
        {
            if (!waitingLogged)
            {
                MelonLogger.Msg("[Stretch Flex Motion] " + message);
                waitingLogged = true;
            }
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
                CreateBool(page, "Flexible Waist", flexibleWaist.Value, v => flexibleWaist.Value = v);
                CreateFloat(page, "Stretchiness", stretchiness.Value, 0.1f, 0f, 3f, v => stretchiness.Value = v);
                CreateFloat(page, "Flexibility", flexibility.Value, 0.1f, 0f, 3f, v => flexibility.Value = v);
                CreateFloat(page, "Waist Flexibility", waistFlex.Value, 0.1f, 0f, 3f, v => waistFlex.Value = v);
                CreateFloat(page, "Hip Flexibility", hipFlex.Value, 0.1f, 0f, 3f, v => hipFlex.Value = v);
                CreateFloat(page, "Chest Flexibility", spineFlex.Value, 0.1f, 0f, 3f, v => spineFlex.Value = v);
                CreateFloat(page, "Arm Flexibility", armFlex.Value, 0.1f, 0f, 3f, v => armFlex.Value = v);
                CreateFloat(page, "Leg Flexibility", legFlex.Value, 0.1f, 0f, 3f, v => legFlex.Value = v);
                CreateFloat(page, "Maximum Bend", maxBend.Value, 1f, 5f, 65f, v => maxBend.Value = v);
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

        private static object CloneValue(object value)
        {
            if (value == null)
                return null;

            try
            {
                MethodInfo clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
                return clone?.Invoke(value, null) ?? value;
            }
            catch { return value; }
        }

        private static bool SameObject(object a, object b)
        {
            if (a == null || b == null)
                return false;
            int x = GetId(a), y = GetId(b);
            return (x != 0 && x == y) || ReferenceEquals(a, b);
        }

        private static object MemberOrGetter(object instance, params string[] names)
        {
            if (instance == null)
                return null;

            foreach (string name in names)
            {
                object value = GetMember(instance, name);
                if (value != null)
                    return value;

                value = CallNoArg(instance, "get_" + name);
                if (value != null)
                    return value;
            }

            return null;
        }

        private static object CallNoArg(object instance, string method)
        {
            try
            {
                return instance?.GetType().GetMethod(method,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(instance, null);
            }
            catch { return null; }
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType(name, false);
                if (found != null)
                    return found;
            }

            return Type.GetType(name + ", UnityEngine.PhysicsModule") ??
                   Type.GetType(name + ", UnityEngine.CoreModule") ??
                   Type.GetType(name + ", BoneLib") ??
                   Type.GetType(name);
        }

        private static Type FindTypeBySimpleName(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type.Name == name)
                            return type;
                    }
                }
                catch { }
            }

            return null;
        }

        private static object GetStatic(Type type, string name)
        {
            try
            {
                if (type == null)
                    return null;

                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                if (property != null)
                    return property.GetValue(null);

                return type.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            }
            catch { return null; }
        }

        private static object GetMember(object instance, string name)
        {
            try
            {
                if (instance == null)
                    return null;

                PropertyInfo property = instance.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

                if (property != null)
                    return property.GetValue(instance);

                return instance.GetType().GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)?.GetValue(instance);
            }
            catch { return null; }
        }

        private static void SetMember(object instance, string name, object value)
        {
            try
            {
                if (instance == null)
                    return;

                PropertyInfo property = instance.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (property != null && property.CanWrite)
                {
                    property.SetValue(instance, value);
                    return;
                }

                instance.GetType().GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(instance, value);
            }
            catch { }
        }

        private static object Call(object instance, string name, params object[] args)
        {
            try
            {
                if (instance == null)
                    return null;

                foreach (MethodInfo method in instance.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
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

        private static int EnumValue(object value)
        {
            try { return Convert.ToInt32(value); }
            catch { return -1; }
        }

        private static float ToFloat(object value)
        {
            try { return Convert.ToSingle(value); }
            catch { return 0f; }
        }

        private static float TimeValue(string property)
        {
            try
            {
                Type timeType = FindType("UnityEngine.Time");
                return Convert.ToSingle(timeType.GetProperty(property,
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null));
            }
            catch { return 0f; }
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}