using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

[assembly: MelonInfo(typeof(FlowMotionNew.FlowMotionMod), "Flow Motion", "1.0.1", "BOR")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace FlowMotionNew
{
    public sealed class FlowMotionMod : MelonMod
    {
        private MelonPreferences_Entry<bool> enabled;
        private MelonPreferences_Entry<float> stretchiness;
        private MelonPreferences_Entry<float> flexibility;
        private MelonPreferences_Entry<float> stretchSpeed;
        private MelonPreferences_Entry<float> flexSpeed;
        private MelonPreferences_Entry<float> armStretch;
        private MelonPreferences_Entry<float> legStretch;
        private MelonPreferences_Entry<float> torsoStretch;
        private MelonPreferences_Entry<float> hipFlex;
        private MelonPreferences_Entry<float> maxBend;
        private MelonPreferences_Entry<float> movementInfluence;
        private MelonPreferences_Entry<float> idleMotion;

        private readonly Dictionary<object, object> baseScales = new Dictionary<object, object>();
        private readonly Dictionary<object, object> baseRotations = new Dictionary<object, object>();
        private readonly List<object> bones = new List<object>();

        private Type unityTransform;
        private Type unityVector3;
        private Type unityQuaternion;
        private Type unityTime;
        private PropertyInfo trName;
        private PropertyInfo trLocalScale;
        private PropertyInfo trLocalRotation;
        private PropertyInfo trPosition;
        private PropertyInfo vecX;
        private PropertyInfo vecY;
        private PropertyInfo vecZ;
        private MethodInfo getChildren;
        private MethodInfo quatEuler;
        private MethodInfo quatSlerp;
        private MethodInfo quatMultiply;
        private PropertyInfo timeProp;
        private PropertyInfo deltaTimeProp;

        private object rigTransform;
        private string rigSignature;
        private float nextScan;
        private float lastY;
        private bool initialized;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("Flow Motion");
            enabled = cat.CreateEntry("Enabled", true);
            stretchiness = cat.CreateEntry("Stretchiness", 1.25f);
            flexibility = cat.CreateEntry("Flexibility", 1.25f);
            stretchSpeed = cat.CreateEntry("Stretch Speed", 4f);
            flexSpeed = cat.CreateEntry("Flex Speed", 3f);
            armStretch = cat.CreateEntry("Arm Stretch", 1f);
            legStretch = cat.CreateEntry("Leg Stretch", 1f);
            torsoStretch = cat.CreateEntry("Torso Stretch", 1f);
            hipFlex = cat.CreateEntry("Hip Flex", 1f);
            maxBend = cat.CreateEntry("Maximum Bend", 1f);
            movementInfluence = cat.CreateEntry("Movement Influence", 1f);
            idleMotion = cat.CreateEntry("Idle Motion", 0.35f);

            SetupReflection();
            MelonLogger.Msg("Flow Motion initialized - new stretchy/flexible body system.");
        }

        public override void OnUpdate()
        {
            try
            {
                if (!initialized || !enabled.Value) return;
                float now = GetFloat(timeProp);
                float dt = Math.Max(0.001f, GetFloat(deltaTimeProp));

                if (now >= nextScan)
                {
                    nextScan = now + 0.75f;
                    FindPlayerRig();
                }

                if (rigTransform == null || bones.Count == 0) return;
                ApplyMotion(now, dt);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("Flow Motion update error: " + ex.Message);
            }
        }

        private void SetupReflection()
        {
            unityTransform = FindType("UnityEngine.Transform");
            unityVector3 = FindType("UnityEngine.Vector3");
            unityQuaternion = FindType("UnityEngine.Quaternion");
            unityTime = FindType("UnityEngine.Time");
            if (unityTransform == null || unityVector3 == null || unityQuaternion == null || unityTime == null)
                throw new Exception("Unity types were not found.");

            trName = unityTransform.GetProperty("name");
            trLocalScale = unityTransform.GetProperty("localScale");
            trLocalRotation = unityTransform.GetProperty("localRotation");
            trPosition = unityTransform.GetProperty("position");
            vecX = unityVector3.GetProperty("x");
            vecY = unityVector3.GetProperty("y");
            vecZ = unityVector3.GetProperty("z");
            quatEuler = unityQuaternion.GetMethod("Euler", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(float), typeof(float), typeof(float) }, null);
            quatSlerp = unityQuaternion.GetMethod("Slerp", BindingFlags.Static | BindingFlags.Public, null, new[] { unityQuaternion, unityQuaternion, typeof(float) }, null);
            quatMultiply = unityQuaternion.GetMethod("op_Multiply", BindingFlags.Static | BindingFlags.Public, null, new[] { unityQuaternion, unityQuaternion }, null);
            timeProp = unityTime.GetProperty("time", BindingFlags.Static | BindingFlags.Public);
            deltaTimeProp = unityTime.GetProperty("deltaTime", BindingFlags.Static | BindingFlags.Public);

            foreach (var m in unityTransform.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (m.Name == "GetComponentsInChildren" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
                {
                    getChildren = m.MakeGenericMethod(unityTransform);
                    break;
                }
            }
            initialized = getChildren != null && trName != null && trLocalScale != null && trLocalRotation != null && trPosition != null && quatEuler != null && quatSlerp != null;
        }

        private void FindPlayerRig()
        {
            Type type = FindType("BoneLib.Player");
            if (type == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        type = asm.GetType("BoneLib.Player", false);
                        if (type != null) break;
                    }
                    catch { }
                }
            }
            if (type == null) return;

            object rm = null;
            var p = type.GetProperty("RigManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) rm = p.GetValue(null, null);
            if (rm == null)
            {
                var f = type.GetField("RigManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) rm = f.GetValue(null);
            }
            if (rm == null) return;

            object root = GetTransformFromComponent(rm);
            if (root == null) return;

            object[] all = (object[])getChildren.Invoke(root, new object[] { true });
            string sig = BuildSignature(all);
            if (rigTransform != root || sig != rigSignature)
            {
                RestoreAndClear();
                rigTransform = root;
                rigSignature = sig;
                CacheBones(all);
                lastY = GetVectorComponent(trPosition.GetValue(root, null), vecY);
                MelonLogger.Msg("Flow Motion found player rig: " + all.Length + " transforms.");
            }
        }

        private object GetTransformFromComponent(object component)
        {
            var p = component.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public);
            return p == null ? null : p.GetValue(component, null);
        }

        private string BuildSignature(object[] all)
        {
            int count = 0;
            unchecked
            {
                int hash = 17;
                foreach (var t in all)
                {
                    string n = (string)trName.GetValue(t, null);
                    if (IsTarget(n))
                    {
                        count++;
                        hash = hash * 31 + n.GetHashCode();
                    }
                }
                return count + ":" + hash;
            }
        }

        private void CacheBones(object[] all)
        {
            foreach (var t in all)
            {
                string n = ((string)trName.GetValue(t, null) ?? string.Empty).ToLowerInvariant();
                if (!IsTarget(n)) continue;
                baseScales[t] = trLocalScale.GetValue(t, null);
                baseRotations[t] = trLocalRotation.GetValue(t, null);
                bones.Add(t);
            }
        }

        private bool IsTarget(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            string n = raw.ToLowerInvariant();
            if (n.Contains("hand") || n.Contains("upperarm") || n.Contains("forearm") || n.Contains("arm")) return true;
            if (n.Contains("thigh") || n.Contains("calf") || n.Contains("shin") || n.Contains("foot") || n.Contains("leg")) return true;
            if (n.Contains("spine") || n.Contains("chest") || n.Contains("abdomen") || n.Contains("torso")) return true;
            if (n.Contains("pelvis") || n.Contains("hip") || n.Contains("waist")) return true;
            return false;
        }

        private void ApplyMotion(float time, float dt)
        {
            float y = GetVectorComponent(trPosition.GetValue(rigTransform, null), vecY);
            float verticalVelocity = Math.Abs((y - lastY) / dt);
            lastY = y;
            float motion = Clamp01(verticalVelocity / 2.5f) * movementInfluence.Value;
            float activity = Clamp01(motion + idleMotion.Value * 0.35f);
            float stretch = stretchiness.Value * (0.35f + activity);
            float flex = flexibility.Value * (0.35f + activity);
            float smooth = Clamp01(stretchSpeed.Value * dt);
            float rotSmooth = Clamp01(flexSpeed.Value * dt);

            foreach (var bone in bones)
            {
                if (bone == null || !baseScales.ContainsKey(bone)) continue;
                string n = ((string)trName.GetValue(bone, null) ?? string.Empty).ToLowerInvariant();
                float group = 1f;
                if (n.Contains("arm") || n.Contains("hand")) group = armStretch.Value;
                else if (n.Contains("leg") || n.Contains("thigh") || n.Contains("calf") || n.Contains("shin") || n.Contains("foot")) group = legStretch.Value;
                else if (n.Contains("spine") || n.Contains("chest") || n.Contains("abdomen") || n.Contains("torso")) group = torsoStretch.Value;
                else if (n.Contains("hip") || n.Contains("pelvis") || n.Contains("waist")) group = hipFlex.Value;

                object baseScale = baseScales[bone];
                float wave = (float)Math.Sin(time * stretchSpeed.Value + bone.GetHashCode() * 0.017f);
                float targetY = GetVectorComponent(baseScale, vecY) * (1f + 0.18f * stretch * group * (0.5f + 0.5f * wave));
                object targetScale = MakeVector3(GetVectorComponent(baseScale, vecX), targetY, GetVectorComponent(baseScale, vecZ));
                object currentScale = trLocalScale.GetValue(bone, null);
                trLocalScale.SetValue(bone, LerpVector(currentScale, targetScale, smooth));

                float phase = bone.GetHashCode() * 0.013f;
                float bend = (float)Math.Sin(time * flexSpeed.Value + phase) * maxBend.Value * flex * group * 10f;
                float twist = (float)Math.Cos(time * flexSpeed.Value * 0.73f + phase) * maxBend.Value * flex * group * 7f;
                if (n.Contains("spine") || n.Contains("chest") || n.Contains("torso")) bend *= 0.65f;
                object offset = quatEuler.Invoke(null, new object[] { bend, twist, bend * 0.3f });
                object targetRot = baseRotations[bone];
                if (quatMultiply != null) targetRot = quatMultiply.Invoke(null, new[] { baseRotations[bone], offset });
                object currentRot = trLocalRotation.GetValue(bone, null);
                trLocalRotation.SetValue(bone, quatSlerp.Invoke(null, new[] { currentRot, targetRot, rotSmooth }));
            }
        }

        private void RestoreAndClear()
        {
            foreach (var kv in baseScales)
            {
                try { trLocalScale.SetValue(kv.Key, kv.Value); } catch { }
            }
            foreach (var kv in baseRotations)
            {
                try { trLocalRotation.SetValue(kv.Key, kv.Value); } catch { }
            }
            baseScales.Clear();
            baseRotations.Clear();
            bones.Clear();
            rigTransform = null;
            rigSignature = null;
        }

        private object LerpVector(object a, object b, float t)
        {
            return MakeVector3(
                Lerp(GetVectorComponent(a, vecX), GetVectorComponent(b, vecX), t),
                Lerp(GetVectorComponent(a, vecY), GetVectorComponent(b, vecY), t),
                Lerp(GetVectorComponent(a, vecZ), GetVectorComponent(b, vecZ), t));
        }

        private object MakeVector3(float x, float y, float z)
        {
            return Activator.CreateInstance(unityVector3, new object[] { x, y, z });
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(fullName, false); if (t != null) return t; } catch { }
            }
            return Type.GetType(fullName, false);
        }

        private static float GetFloat(PropertyInfo p)
        {
            if (p == null) return 0f;
            return Convert.ToSingle(p.GetValue(null, null));
        }

        private static float GetVectorComponent(object v, PropertyInfo p)
        {
            return v == null || p == null ? 0f : Convert.ToSingle(p.GetValue(v, null));
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
