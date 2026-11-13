using Ratms.Networking;
using UnityEditor;
using UnityEngine;

/// <summary>
///  Utilidades de Editor para dejar los prefabs con los componentes que
///  el código necesita, sin tener que arrastrar nada a mano.
///
///  Se puede lanzar desde el menú (Ratms → Setup) o desde la línea de comandos:
///    Unity.exe -batchmode -quit -projectPath &lt;repo&gt; -executeMethod RatmsPrefabSetup.EnsureRatComponents
///
///  Es IDEMPOTENTE: si el componente ya está, no hace nada.
/// </summary>
public static class RatmsPrefabSetup
{
    private const string RatPrefabPath = "Assets/Prefabs/Prefabs multiplayer/rat.network.prefab";

    [MenuItem("Ratms/Setup/Añadir componentes que faltan a la rata")]
    public static void EnsureRatComponents()
    {
        var root = PrefabUtility.LoadPrefabContents(RatPrefabPath);
        try
        {
            bool changed = false;

            changed |= Ensure<NetworkBat>(root);
            changed |= Ensure<NetworkShotgun>(root);
            changed |= Ensure<NetworkTeleport>(root);
            changed |= Ensure<NetworkNinjaRope>(root);
            changed |= Ensure<NetworkArsenal>(root);   // 0.18: lanzallamas, metralleta, ataque aéreo

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, RatPrefabPath);
                Debug.Log("[RatmsPrefabSetup] Prefab de la rata guardado.");
            }
            else
            {
                Debug.Log("[RatmsPrefabSetup] La rata ya tenía todo. Nada que hacer.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool Ensure<T>(GameObject root) where T : Component
    {
        if (root.GetComponent<T>() != null) return false;
        root.AddComponent<T>();
        Debug.Log($"[RatmsPrefabSetup] Añadido {typeof(T).Name} a la rata.");
        return true;
    }
}
