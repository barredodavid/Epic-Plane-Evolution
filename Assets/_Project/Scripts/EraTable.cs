using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Era
{
    public string name = "Biplane";
    public int startYear;
    [Tooltip("Plane model for this era (one copy per plane in the squadron).")]
    public GameObject model;
    [Tooltip("Metres flown after the ramp with a single plane.")]
    public float flightDistance = 300f;
    [Tooltip("Seconds the final flight lasts on screen.")]
    public float flightSeconds = 3f;
    [Tooltip("Nose-dives, bounces and tumbles instead of landing (the 'fail' ending).")]
    public bool crashes;
    [Tooltip("Never comes down: climbs into the sky instead.")]
    public bool reachesSpace;
}

// Every plane number lives here: rebalance the whole ad without touching code.
[CreateAssetMenu(menuName = "Plane Evolution/Era Table")]
public class EraTable : ScriptableObject
{
    [Tooltip("Sorted by startYear, oldest first.")]
    public List<Era> eras = new();

    public int IndexFor(int year)
    {
        int index = 0;
        for (int i = 0; i < eras.Count; i++)
            if (year >= eras[i].startYear) index = i;
        return index;
    }
}
