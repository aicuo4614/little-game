using UnityEngine;

public class EditorTimelineMono : MonoBehaviour
{
    private System.Action onDrawGizmosCallback;

    public void Init(System.Action onDrawGizmosCallback)
    {
        this.onDrawGizmosCallback = onDrawGizmosCallback;
    }

    private void OnDrawGizmos()
    {
        onDrawGizmosCallback?.Invoke();
    }

    public void Clear()
    {
        this.onDrawGizmosCallback = null;
    }
}
