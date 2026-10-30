using UnityEngine;

public sealed class EscapeDoor : MonoBehaviour
{
    [SerializeField] private Transform panel;
    [SerializeField] private int id;
    [SerializeField] private Vector3 openEuler = new Vector3(0f, 90f, 0f);
    private Quaternion closedRotation;
    private Quaternion openRotation;
    private bool isOpen;

    public int Id => id;
    public bool IsOpen => isOpen;

    private void Awake()
    {
        if (panel == null && transform.childCount > 0) panel = transform.GetChild(0);
        if (panel != null)
        {
            closedRotation = panel.localRotation;
            openRotation = closedRotation * Quaternion.Euler(openEuler);
        }
    }

    public void Initialize(int doorId, Transform doorPanel)
    {
        id = doorId;
        panel = doorPanel;
        Awake();
    }

    public void SetOpen(bool open) => isOpen = open;

    private void Update()
    {
        if (panel != null)
            panel.localRotation = Quaternion.Slerp(panel.localRotation, isOpen ? openRotation : closedRotation, Time.deltaTime * 3f);
    }
}
