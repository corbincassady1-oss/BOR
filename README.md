# Bullet Overdrive

Quest-focused BONELAB code mod project.

Features:
- Configurable bullet damage multiplier
- Configurable bullet speed multiplier
- Mid-flight tracer/glow
- Impact spark burst
- BoneMenu settings

## Build references

The project intentionally does **not** commit proprietary BONELAB game assemblies. Put these files in `References/` before building:

- `BoneLib.dll`
- `MelonLoader.dll`
- `Assembly-CSharp.dll`
- `UnityEngine.dll`

The included GitHub Actions workflow builds `BulletOverdrive.dll` when those references are present.
