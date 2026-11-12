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

            if (root.GetComponent<NetworkBat>() == null)
            {
                root.AddComponent<NetworkBat>();
                changed = true;
                Debug.Log("[RatmsPrefabSetup] Añadido NetworkBat a la rata.");
            }

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
}
