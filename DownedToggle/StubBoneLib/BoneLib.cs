using System;
using BoneLib;
using Il2CppSLZ.Marrow;
namespace BoneLib {
 public static class Player {
  public static RigManager RigManager;
  public static PhysicsRig PhysicsRig;
  public static BaseController RightController;
 }
 public static class Hooking {
  public static event Action<LevelInfo> OnLevelLoaded;
  public static event Action<RigManager,float> OnPlayerDamageReceived;
  public static event Action<RigManager> OnPlayerResurrected;
  public static event Action<RigManager> OnPlayerDeath;
 }
}