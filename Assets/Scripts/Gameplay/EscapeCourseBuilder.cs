using System.Collections.Generic;
using KartIHC.Kart;
using UnityEngine;
using KartIHC.Gameplay;
/// <summary>Finds edit-time authored Escape objects. Course geometry lives in EscapeCourse.unity.</summary>
public static class EscapeCourseBuilder
{
    private static readonly List<EscapeDoor> doors = new List<EscapeDoor>();
    private static readonly List<Camera> cameras = new List<Camera>();
    public static IReadOnlyList<EscapeDoor> Doors => doors;
    public static IReadOnlyList<Camera> Cameras => cameras;

    public static void Build()
    {
        doors.Clear();
        cameras.Clear();
        foreach (EscapeDoor door in Object.FindObjectsByType<EscapeDoor>(FindObjectsSortMode.None)) doors.Add(door);
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (camera.CompareTag("MainCamera") == false && camera.name.StartsWith("EscapeSecurityCamera_")) cameras.Add(camera);
        doors.Sort((a, b) => a.Id.CompareTo(b.Id));
        cameras.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        KartController kart = Object.FindFirstObjectByType<KartController>();
        Transform spawn = GameObject.Find("EscapeKartSpawn")?.transform;
        if (kart != null && spawn != null)
        {
            kart.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            Rigidbody body = kart.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = spawn.position;
                body.rotation = spawn.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
        foreach (RaceCheckpoint checkpoint in Object.FindObjectsByType<RaceCheckpoint>(FindObjectsSortMode.None)) checkpoint.enabled = false;
        LapManager laps = Object.FindFirstObjectByType<LapManager>();
        if (laps != null) laps.enabled = false;
    }

    public static void RestoreStandardCourse() { doors.Clear(); cameras.Clear(); }
}
