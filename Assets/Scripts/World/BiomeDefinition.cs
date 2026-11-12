using System;
using UnityEngine;

namespace Ratms.World
{
    /// <summary>
    ///  DEFINICIÓN DE UN BIOMA — agrupa TODO lo visual coherente:
    ///  colores del terreno, del agua, y del fondo. Al elegir un bioma en
    ///  el menú, TerrainGenerator, WaterDeathZone y BackgroundManager
    ///  leen sus valores desde aquí para que la partida entera sea
    ///  visualmente coherente (campo, alcantarilla, hielo, desierto...).
    ///
    ///  Se usa como clase [Serializable] normal (no ScriptableObject) para
    ///  que el usuario no tenga que crear assets a mano — <see cref="BiomeManager"/>
    ///  incluye los biomas builtin (Meadow, Sewer) por defecto.
    /// </summary>
    [Serializable]
    public class BiomeDefinition
    {
        [Tooltip("Nombre visible para menús y debug.")]
        public string biomeName = "Meadow";

        [Tooltip("Tipo del bioma. Sirve para lógica que dependa del tema (p.ej. SFX distintos).")]
        public BiomeType type = BiomeType.Meadow;

        // ============= TERRENO =============
        [Header("Terreno")]
        [Tooltip("Color de la hierba/musgo/cubierta superior del terreno.")]
        public Color32 grassColor = new Color32(75, 150, 65, 255);

        [Tooltip("Outline brillante del borde superior — el amarillo Worms clásico.")]
        public Color32 grassOutlineColor = new Color32(200, 230, 90, 255);

        [Tooltip("Color principal de la tierra/hormigón.")]
        public Color32 dirtColor = new Color32(135, 90, 55, 255);

        [Tooltip("Color intermedio para tercer tono (marrón medio). Da más riqueza al grano.")]
        public Color32 dirtColorMid = new Color32(110, 72, 42, 255);

        [Tooltip("Color oscuro de la tierra para dar grano.")]
        public Color32 dirtColorDark = new Color32(85, 55, 30, 255);

        // ============= AGUA =============
        [Header("Agua")]
        public Color waterColor      = new Color(0.08f, 0.42f, 0.48f, 0.95f);
        public Color waterDeepColor  = new Color(0.02f, 0.15f, 0.25f, 1f);
        public Color foamColor       = new Color(1f, 1f, 1f, 1f);
        public Color foamAccentColor = new Color(0.75f, 0.9f, 1f, 0.9f);

        // ============= FONDO =============
        [Header("Fondo — cielo")]
        [Tooltip("Color superior del degradado del cielo.")]
        public Color skyTop = new Color(0.55f, 0.75f, 0.95f, 1f);

        [Tooltip("Color inferior del degradado del cielo (horizonte).")]
        public Color skyBottom = new Color(0.85f, 0.85f, 0.75f, 1f);

        [Header("Fondo — nubes")]
        [Tooltip("Si ON, se pintan nubes procedurales que scrollan.")]
        public bool hasClouds = true;
        public Color cloudColor = new Color(1f, 1f, 1f, 0.9f);
        [Range(0f, 1f)] public float cloudDensity = 0.55f;

        [Header("Fondo — parallax de siluetas")]
        [Tooltip("Silueta lejana (color y forma). Va detrás de todo.")]
        public Color silhouetteFar = new Color(0.4f, 0.3f, 0.35f, 0.7f);

        [Tooltip("Silueta cercana, algo más oscura y detallada.")]
        public Color silhouetteNear = new Color(0.25f, 0.2f, 0.25f, 0.9f);

        [Tooltip("Estilo del parallax: colinas suaves (Meadow) o edificios cuadrados (Sewer).")]
        public ParallaxStyle parallaxStyle = ParallaxStyle.Hills;

        [Header("Fondo — luces (solo Sewer)")]
        [Tooltip("Si ON, se pintan luces de neón difuminadas en las siluetas.")]
        public bool hasNeonLights = false;
        public Color neonColor = new Color(1f, 0.4f, 0.6f, 0.8f);
    }

    public enum BiomeType
    {
        Meadow = 0,   // clásico Worms: hierba, cielo azul, colinas
        Sewer  = 1,   // alcantarilla: hormigón, agua verdosa, edificios
    }

    public enum ParallaxStyle
    {
        Hills = 0,       // colinas suaves onduladas
        Buildings = 1,   // rascacielos/edificios rectangulares
    }
}
