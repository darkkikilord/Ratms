using UnityEngine;

namespace Ratms.Networking
{
    /// <summary>
    ///  Viste un proyectil (misil, granada) con su dibujo de <see cref="WeaponArt"/>
    ///  en vez del sprite del prefab. Es solo visual y local: en cada cliente
    ///  el dibujo se orienta según hacia dónde se MUEVE el objeto (el misil
    ///  apunta a su trayectoria, la granada da vueltas), así queda igual de
    ///  suave en el host que en los clientes (que solo reciben la posición).
    /// </summary>
    public class ProjectileSkin : MonoBehaviour
    {
        private Transform art;
        private bool faceVelocity;
        private float spinPerUnit;
        private Vector3 lastPos;
        private float angle;

        public static ProjectileSkin Apply(GameObject host, Sprite sprite, float scale, bool faceVelocity, float spinPerUnit = 0f, int order = 50)
        {
            foreach (var r in host.GetComponentsInChildren<SpriteRenderer>()) r.enabled = false;
            var skin = host.AddComponent<ProjectileSkin>();
            var go = new GameObject("Arte");
            go.transform.SetParent(host.transform, worldPositionStays: false);
            Vector3 ls = host.transform.lossyScale;
            go.transform.localScale = new Vector3(scale / Mathf.Max(0.0001f, Mathf.Abs(ls.x)), scale / Mathf.Max(0.0001f, Mathf.Abs(ls.y)), 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            skin.art = go.transform;
            skin.faceVelocity = faceVelocity;
            skin.spinPerUnit = spinPerUnit;
            skin.lastPos = host.transform.position;
            return skin;
        }

        private void LateUpdate()
        {
            if (art == null) return;
            Vector3 p = transform.position;
            Vector2 d = p - lastPos;
            lastPos = p;
            if (faceVelocity)
            {
                if (d.sqrMagnitude > 1e-6f)
                    angle = Mathf.LerpAngle(angle, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 0.5f);
            }
            else if (spinPerUnit != 0f)
                angle -= d.x * spinPerUnit;   // rueda hacia donde va
            art.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
