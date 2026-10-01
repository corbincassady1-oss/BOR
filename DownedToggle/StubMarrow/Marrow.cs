using UnityEngine;
namespace Il2CppSLZ.Marrow
{
 public class LevelInfo {}
 public class Grip { public bool HasAttachedHands()=>false; }
 public class BaseController { public bool GetThumbStickDown()=>false; }
 public class Player_Health {
  public float curr_Health;
  public object regenRoutine;
  public void StopCoroutine(object routine){}
  public void LifeSavingDamgeDealt(){}
  public void Dying(int reason){}
 }
 public class PhysLimb { public void ShutdownLimb(){} }
 public class PhysHand {
  public Grip gShoulder=new Grip();
  public Grip gElbow=new Grip();
  public PhysHand physHand=>this;
 }
 public class PhysTorso {
  public bool shutdown;
  public Grip gChest=new Grip(), gHead=new Grip(), gNeck=new Grip(), gPelvis=new Grip(), gSpine=new Grip();
 }
 public class PhysFeet { public Transform transform; }
 public class PhysKnee { public Transform transform; }
 public class PhysicsRig : MonoBehaviour {
  public PhysTorso torso=new PhysTorso();
  public PhysHand leftHand=new PhysHand(), rightHand=new PhysHand();
  public PhysLimb legLf=new PhysLimb(), legRt=new PhysLimb();
  public PhysFeet feet=new PhysFeet();
  public PhysKnee knee=new PhysKnee();
  public Transform m_pelvis;
  public bool ballLocoEnabled=true, shutdown;
  public void RagdollRig(){}
  public void DisableBallLoco(){}
  public void PhysicalLegs(){}
  public void TurnOnRig(){}
  public void UnRagdollRig(){}
  public void ShutdownRig(){}
 }
 public class RigManager : MonoBehaviour {
  public PhysicsRig physicsRig;
  public Player_Health health=new Player_Health();
 }
}