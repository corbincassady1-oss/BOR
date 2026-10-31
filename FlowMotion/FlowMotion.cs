using System;
using System.Collections.Generic;
using BoneLib;
using BoneLib.BoneMenu;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(FlowMotion.FlowMotionMod), "Flow Motion", "1.0.0", "BOR")]
[assembly: MelonGame("Stress Level Zero", "BONELAB")]

namespace FlowMotion
{
    public class FlowMotionMod : MelonMod
    {
        private MelonPreferences_Entry<bool> enabled;
        private MelonPreferences_Entry<float> stretchiness;
        private MelonPreferences_Entry<float> flexibility;
        private MelonPreferences_Entry<float> stretchSpeed;
        private MelonPreferences_Entry<float> flexSpeed;
        private MelonPreferences_Entry<float> armStretch;
        private MelonPreferences_Entry<float> legStretch;
        private MelonPreferences_Entry<float> spineFlex;
        private MelonPreferences_Entry<float> hipFlex;
        private MelonPreferences_Entry<float> maxBend;
        private readonly Dictionary<Transform, Vector3> baseScales = new Dictionary<Transform, Vector3>();
        private float nextScan;
        private Transform rigRoot;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("Flow Motion");
            enabled = cat.CreateEntry("Enabled", true);
            stretchiness = cat.CreateEntry("Stretchiness", 1.0f);
            flexibility = cat.CreateEntry("Flexibility", 1.0f);
            stretchSpeed = cat.CreateEntry("Stretch Speed", 4.0f);
            flexSpeed = cat.CreateEntry("Flex Speed", 4.0f);
            armStretch = cat.CreateEntry("Arm Stretch", 1.0f);
            legStretch = cat.CreateEntry("Leg Stretch", 1.0f);
            spineFlex = cat.CreateEntry("Spine Flex", 1.0f);
            hipFlex = cat.CreateEntry("Hip Flex", 1.0f);
            maxBend = cat.CreateEntry("Maximum Bend", 1.0f);

            var menu = BoneMenu.Create("Flow Motion");
            menu.Bool("Enabled", true, v => enabled.Value = v);
            menu.Float("Stretchiness", 0f, 3f, stretchiness.Value, v => stretchiness.Value = v);
            menu.Float("Flexibility", 0f, 3f, flexibility.Value, v => flexibility.Value = v);
            menu.Float("Stretch Speed", 0.5f, 10f, stretchSpeed.Value, v => stretchSpeed.Value = v);
            menu.Float("Flex Speed", 0.5f, 10f, flexSpeed.Value, v => flexSpeed.Value = v);
            menu.Float("Arm Stretch", 0f, 3f, armStretch.Value, v => armStretch.Value = v);
            menu.Float("Leg Stretch", 0f, 3f, legStretch.Value, v => legStretch.Value = v);
            menu.Float("Spine Flex", 0f, 3f, spineFlex.Value, v => spineFlex.Value = v);
            menu.Float("Hip Flex", 0f, 3f, hipFlex.Value, v => hipFlex.Value = v);
            menu.Float("Maximum Bend", 0f, 3f, maxBend.Value, v => maxBend.Value = v);
        }

        public override void OnUpdate()
        {
            if (!enabled.Value) return;
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 1f;
                FindRig();
            }
            if (rigRoot == null) return;
            ApplyFlow(Time.deltaTime);
        }

        private void FindRig()
        {
            try
            {
                var rm = Player.RigManager;
                if (rm != null) rigRoot = rm.transform;
            }
            catch { }
        }

        private void ApplyFlow(float dt)
        {
            if (rigRoot == null) return;
            var transforms = rigRoot.GetComponentsInChildren<Transform>(true);
            float t = Time.time;
            float motion = Mathf.Clamp01(PlayerMotion());
            float stretch = 1f + (stretchiness.Value * 0.08f * motion);
            float flex = flexibility.Value * maxBend.Value;
            float wave = Mathf.Sin(t * Mathf.Max(0.1f, flexSpeed.Value)) * 0.5f;

            foreach (var tr in transforms)
            {
                if (tr == rigRoot) continue;
                string n = tr.name.ToLowerInvariant();
                if (!baseScales.ContainsKey(tr)) baseScales[tr] = tr.localScale;
                Vector3 s = baseScales[tr];
                float amount = 0f;
                if (n.Contains("upperarm") || n.Contains("forearm") || n.Contains("hand")) amount = armStretch.Value;
                else if (n.Contains("thigh") || n.Contains("calf") || n.Contains("foot")) amount = legStretch.Value;
                else if (n.Contains("spine") || n.Contains("chest") || n.Contains("abdomen")) amount = spineFlex.Value;
                else if (n.Contains("pelvis") || n.Contains("hip")) amount = hipFlex.Value;
                if (amount <= 0f) continue;

                float localStretch = 1f + (stretch - 1f) * amount;
                float flexWave = 1f + (wave * 0.04f * flex * amount);
                float target = Mathf.Max(0.1f, localStretch * flexWave);
                float smooth = 1f - Mathf.Exp(-Mathf.Max(0.1f, stretchSpeed.Value) * dt);
                tr.localScale = Vector3.Lerp(tr.localScale, new Vector3(s.x, s.y * target, s.z), smooth);
            }
        }

        private float PlayerMotion()
        {
            try
            {
                var rb = Player.RigManager.physicsRig.m_chest.rb;
                return rb == null ? 0f : rb.velocity.magnitude / 4f;
            }
            catch { return 0f; }
        }
    }
}
