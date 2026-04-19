using System;
using UnityEngine;

public class LaundryActivationZone : MonoBehaviour
{
    public event Action<LaundryTrackedPerson> PlayerEntered;

    void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<LaundryTrackedPerson>();
        if (p == null) return;
        PlayerEntered?.Invoke(p);
    }
}
