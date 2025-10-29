using UnityEngine;

public class IKControl : MonoBehaviour
{
    [SerializeField] public GameObject target;
    [SerializeField] public GameObject targetToLook;
    [SerializeField] public GameObject targetToLook2;
    [SerializeField] private AvatarIKGoal goal;
    [SerializeField] private AvatarIKGoal goal2;
    public bool iKactive;
    [SerializeField] private Animator anim;

    private void OnAnimatorIK()
    {
        // Fix for IDE0051: Ensure this method is used by Unity's Animator system.
        if (iKactive)
        {
            anim.SetIKPositionWeight(goal, 1);
            anim.SetIKPositionWeight(goal2, -3f);
            anim.SetLookAtWeight(1);
            anim.SetIKPosition(goal, target.transform.position);
            anim.SetIKPosition(goal2, targetToLook2.transform.position);
            anim.SetLookAtPosition(targetToLook.transform.position);
            anim.SetIKRotationWeight(goal2, -3);
            anim.SetIKRotation(goal2, targetToLook2.transform.rotation);
        }
        // Uncommented code to reset IK weights when not active.
        else
        {
            anim.SetIKPositionWeight(goal, 0);
            anim.SetIKRotationWeight(goal, 0);
            anim.SetIKPositionWeight(goal2, 0);
            anim.SetIKRotationWeight(goal2, 0);
        }
    }
}

