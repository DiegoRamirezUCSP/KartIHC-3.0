using KartIHC.Kart;
using UnityEngine;

public sealed class EscapeFinishZone : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        KartController kart = other.GetComponentInParent<KartController>();
        if (kart != null && EscapeModeNetworkBridge.Instance != null)
            EscapeModeNetworkBridge.Instance.ReportDriverFinished();
    }
}
