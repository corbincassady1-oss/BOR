using System;
using System.Reflection;

namespace HealthDisplayPatch
{
    public static class HealthColorCustomization
    {
        private static bool _menuBuilt;

        private static float _highR = 0f, _highG = 1f, _highB = 0f;
        private static float _midR = 1f, _midG = 1f, _midB = 0f;
        private static float _lowR = 1f, _lowG = 0f, _lowB = 0f;

        public static void BuildMenu()
        {
            if (_menuBuilt) return;
            _menuBuilt = true;

            Type pageType = Type.GetType("BoneLib.BoneMenu.Page, BoneLib");
            if (pageType == null) return;

            PropertyInfo rootProperty = pageType.GetProperty("Root", BindingFlags.Public | BindingFlags.Static);
            object root = rootProperty?.GetValue(null);
            if (root == null) return;

            object colorsPage = InvokeNamed(root, "CreatePage", new object[]
            {
                "Health Colors (RGB)", MakeColor(1f, 1f, 1f, 1f), 0, true
            });
            if (colorsPage == null) return;

            object highPage = InvokeNamed(colorsPage, "CreatePage", new object[]
            {
                "High HP Color", MakeColor(_highR, _highG, _highB, 1f), 0, true
            });
            object midPage = InvokeNamed(colorsPage, "CreatePage", new object[]
            {
                "Middle HP Color", MakeColor(_midR, _midG, _midB, 1f), 0, true
            });
            object lowPage = InvokeNamed(colorsPage, "CreatePage", new object[]
            {
                "Low HP Color", MakeColor(_lowR, _lowG, _lowB, 1f), 0, true
            });

            AddRGB(highPage, new[] { _highR, _highG, _highB },
                new[] { nameof(SetHighR), nameof(SetHighG), nameof(SetHighB) });
            AddRGB(midPage, new[] { _midR, _midG, _midB },
                new[] { nameof(SetMidR), nameof(SetMidG), nameof(SetMidB) });
            AddRGB(lowPage, new[] { _lowR, _lowG, _lowB },
                new[] { nameof(SetLowR), nameof(SetLowG), nameof(SetLowB) });
        }

        private static void AddRGB(object page, float[] values, string[] setterNames)
        {
            if (page == null) return;

            string[] channels = { "Red", "Green", "Blue" };
            for (int i = 0; i < 3; i++)
            {
                MethodInfo setter = typeof(HealthColorCustomization).GetMethod(
                    setterNames[i], BindingFlags.Public | BindingFlags.Static);
                Delegate callback = Delegate.CreateDelegate(typeof(Action<float>), setter);

                InvokeNamed(page, "CreateFloat", new object[]
                {
                    channels[i],
                    MakeColor(values[0], values[1], values[2], 1f),
                    values[i],
                    0.05f,
                    0f,
                    10f,
                    callback
                });
            }
        }

        private static object InvokeNamed(object target, string name, object[] args)
        {
            foreach (MethodInfo method in target.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name == name && method.GetParameters().Length == args.Length)
                    return method.Invoke(target, args);
            }
            return null;
        }

        private static object MakeColor(float r, float g, float b, float a)
        {
            Type colorType = Type.GetType("UnityEngine.Color, UnityEngine.CoreModule");
            if (colorType == null) return null;
            return Activator.CreateInstance(colorType, new object[] { r, g, b, a });
        }

        public static void ApplyColor()
        {
            Type modType = Type.GetType("HealthDisplay.HealthDisplayMod, HealthDisplay");
            if (modType == null) return;

            FieldInfo hudField = modType.GetField("_hudText",
                BindingFlags.NonPublic | BindingFlags.Static);
            object hudText = hudField?.GetValue(null);
            if (hudText == null) return;

            MethodInfo getHealth = modType.GetMethod("GetPlayerHealth",
                BindingFlags.Public | BindingFlags.Static);
            object health = getHealth?.Invoke(null, null);
            if (health == null) return;

            Type healthType = health.GetType();
            PropertyInfo currentProperty = healthType.GetProperty(
                "curr_Health", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo maxProperty = healthType.GetProperty(
                "max_Health", BindingFlags.Public | BindingFlags.Instance);
            if (currentProperty == null || maxProperty == null) return;

            float current = Math.Max(Convert.ToSingle(currentProperty.GetValue(health)), 0f);
            float max = Convert.ToSingle(maxProperty.GetValue(health));
            if (max <= 0f) max = 100f;
            float ratio = current / max;

            float r, g, b;
            if (ratio > 0.5f)
            {
                float t = (ratio - 0.5f) * 2f;
                r = Lerp(_midR, _highR, t);
                g = Lerp(_midG, _highG, t);
                b = Lerp(_midB, _highB, t);
            }
            else
            {
                float t = ratio * 2f;
                r = Lerp(_lowR, _midR, t);
                g = Lerp(_lowG, _midG, t);
                b = Lerp(_lowB, _midB, t);
            }

            PropertyInfo colorProperty = hudText.GetType().GetProperty(
                "color", BindingFlags.Public | BindingFlags.Instance);
            if (colorProperty != null)
                colorProperty.SetValue(hudText, MakeColor(r, g, b, 1f));
        }

        private static float Lerp(float a, float b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return a + (b - a) * t;
        }

        public static void SetHighR(float value) => _highR = value;
        public static void SetHighG(float value) => _highG = value;
        public static void SetHighB(float value) => _highB = value;

        public static void SetMidR(float value) => _midR = value;
        public static void SetMidG(float value) => _midG = value;
        public static void SetMidB(float value) => _midB = value;

        public static void SetLowR(float value) => _lowR = value;
        public static void SetLowG(float value) => _lowG = value;
        public static void SetLowB(float value) => _lowB = value;
    }
}