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
            changed |= Ensure<NetworkTrenchArsenal>(root);   // 0.21: arsenal de trinchera

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

    private const string GrenadePrefabPath = "Assets/Prefabs/Prefabs multiplayer/Grenade.prefab";
    private const string GadgetPrefabPath = "Assets/Prefabs/Prefabs multiplayer/Gadget.network.prefab";
    private const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

    /// <summary>
    ///  0.21: crea el prefab de red <c>Gadget.network</c> (proyectiles del arsenal
    ///  de trinchera) COPIANDO el de la granada (misma física, collider y
    ///  NetworkTransform) y cambiando NetworkGrenade por NetworkGadget. Lo
    ///  registra en la lista de prefabs de red. Idempotente.
    /// </summary>
    [MenuItem("Ratms/Setup/Crear prefab Gadget (arsenal de trinchera)")]
    public static void EnsureGadgetPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(GadgetPrefabPath) == null)
        {
            if (!AssetDatabase.CopyAsset(GrenadePrefabPath, GadgetPrefabPath))
            {
                Debug.LogError("[RatmsPrefabSetup] No pude copiar el prefab de la granada.");
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(GadgetPrefabPath);
            try
            {
                root.name = "Gadget.network";
                foreach (var g in root.GetComponentsInChildren<NetworkGrenade>(true)) Object.DestroyImmediate(g);
                if (root.GetComponent<NetworkGadget>() == null) root.AddComponent<NetworkGadget>();
                PrefabUtility.SaveAsPrefabAsset(root, GadgetPrefabPath);
                Debug.Log("[RatmsPrefabSetup] Creado " + GadgetPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GadgetPrefabPath);
        var list = AssetDatabase.LoadAssetAtPath<Unity.Netcode.NetworkPrefabsList>(NetworkPrefabsListPath);
        if (list == null || prefab == null) { Debug.LogError("[RatmsPrefabSetup] Falta la lista de prefabs de red o el prefab."); return; }
        bool already = false;
        foreach (var p in list.PrefabList) if (p.Prefab == prefab) already = true;
        if (!already)
        {
            list.Add(new Unity.Netcode.NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssetIfDirty(list);   // (SaveAssets solo no lo escribía)
            AssetDatabase.SaveAssets();
            Debug.Log("[RatmsPrefabSetup] Gadget.network registrado en la lista de prefabs de red.");
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
