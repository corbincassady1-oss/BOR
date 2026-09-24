using UnityEngine;
namespace Il2CppSLZ.Marrow {
    public class Hand : Component { public GameObject m_CurrentAttachedGO; }
    public class PhysicsRig : Component {
        public Hand leftHand;
        public Hand rightHand;
        public Transform m_pelvis;
        public Transform m_chest;
        public Transform m_spine;
    }
    public class RigManager : Component {
        public PhysicsRig physicsRig;
        public object health;
    }
}