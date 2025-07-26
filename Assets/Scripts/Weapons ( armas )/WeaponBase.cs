using UnityEngine;

public abstract class WeaponBase : MonoBehaviour
{
    public string weaponName;
    public Sprite icon;

    public abstract void Fire(Transform firePoint, Vector2 direction);
}
