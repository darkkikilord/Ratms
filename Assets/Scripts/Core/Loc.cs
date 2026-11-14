using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ratms.UI
{
    /// <summary>
    ///  IDIOMAS (0.19). El juego está escrito en español y el español ES la
    ///  clave: <c>Loc.T("Jugar")</c> devuelve "Jugar" o "Play" según el idioma
    ///  elegido. Así el código sigue leyéndose en español y, si una frase no
    ///  tiene traducción todavía, simplemente sale en español (nunca vacía).
    ///
    ///  · Textos con datos: <c>Loc.F("Turno de {0}", nombre)</c> (como string.Format).
    ///  · Textos de la escena (TextMeshPro de los botones del menú): <see cref="LocText"/>.
    ///  · El idioma se guarda en PlayerPrefs "opt_lang" ("es"/"en"). La primera
    ///    vez se elige según el idioma del sistema.
    ///  · Las traducciones están en <c>Loc.English.cs</c> (parte partial).
    /// </summary>
    public static partial class Loc
    {
        public enum Lang { Es = 0, En = 1 }

        private static Lang? current;

        /// <summary>Avisa cuando cambia el idioma (para repintar textos de la escena).</summary>
        public static event Action Changed;

        public static Lang Current
        {
            get
            {
                if (current == null)
                {
                    string saved = PlayerPrefs.GetString("opt_lang", "");
                    if (saved == "es") current = Lang.Es;
                    else if (saved == "en") current = Lang.En;
                    else current = Application.systemLanguage == SystemLanguage.Spanish ||
                                   Application.systemLanguage == SystemLanguage.Catalan ||
                                   Application.systemLanguage == SystemLanguage.Basque
                                   ? Lang.Es : Lang.En;
                }
                return current.Value;
            }
            set
            {
                if (current == value) return;
                current = value;
                PlayerPrefs.SetString("opt_lang", value == Lang.En ? "en" : "es");
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static bool English => Current == Lang.En;

        /// <summary>
        ///  Cambia el idioma SOLO durante esta ejecución, sin guardarlo (la
        ///  prueba automática lo usa para no tocar las preferencias del jugador).
        /// </summary>
        public static void SetSessionLanguage(Lang lang)
        {
            current = lang;
            Changed?.Invoke();
        }

        /// <summary>Traduce una frase (en español) al idioma actual.</summary>
        public static string T(string es)
        {
            if (string.IsNullOrEmpty(es) || Current == Lang.Es) return es;
            if (en.TryGetValue(es, out var t)) return t;
            // Mismo texto en mayúsculas/minúsculas distintas (títulos de botón).
            if (enUpper.TryGetValue(es.ToUpperInvariant(), out t)) return Recase(es, t);
#if UNITY_EDITOR
            if (missing.Add(es)) Debug.Log($"[Loc] Sin traducción: \"{es}\"");
#endif
            return es;
        }

        /// <summary>Traduce un formato y lo rellena: Loc.F("Turno de {0}", nombre).</summary>
        public static string F(string esFormat, params object[] args)
        {
            try { return string.Format(T(esFormat), args); }
            catch (FormatException) { return string.Format(esFormat, args); }
        }

        // ------------------------------------------------------------------
        //  TEXTOS QUE VIAJAN POR RED (avisos del servidor, letreros "¡PAM!")
        // ------------------------------------------------------------------
        //  El servidor manda SIEMPRE la frase en español; cada PC la traduce al
        //  mostrarla, así un host en español y un amigo en inglés ven cada uno
        //  su idioma. Si la frase lleva datos (nombres), viaja "empaquetada":
        //  plantilla + datos separados por un carácter invisible.
        private const char Sep = '\u001F';

        /// <summary>(Quien la crea) Plantilla en español + datos, para traducir al mostrar.</summary>
        public static string Pack(string esFormat, params object[] args)
        {
            var sb = new System.Text.StringBuilder(esFormat);
            foreach (var a in args) sb.Append(Sep).Append(a);
            return sb.ToString();
        }

        /// <summary>(Al mostrar) Traduce una frase normal o empaquetada con <see cref="Pack"/>.</summary>
        public static string Show(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int i = s.IndexOf(Sep);
            if (i < 0) return T(s);
            var parts = s.Split(Sep);
            var args = new object[parts.Length - 1];
            // Los datos también: "Rojos" → "Reds", "nunca" → "never". Un nombre
            // propio (de rata o jugador) no está en el diccionario y queda igual.
            for (int k = 1; k < parts.Length; k++) args[k - 1] = T(parts[k]);
            return F(parts[0], args);
        }

        /// <summary>Elige directamente entre dos textos (para casos sueltos).</summary>
        public static string Pick(string es, string english) => Current == Lang.En ? english : es;

        // ------------------------------------------------------------------
        private static readonly Dictionary<string, string> en = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> enUpper = new(StringComparer.Ordinal);
#if UNITY_EDITOR
        private static readonly HashSet<string> missing = new();
#endif

        static Loc()
        {
            foreach (var (es, eng) in EnglishPairs())
            {
                en[es] = eng;
                enUpper[es.ToUpperInvariant()] = eng;
            }
        }

        /// <summary>Si la clave venía en MAYÚSCULAS, la traducción también.</summary>
        private static string Recase(string original, string translated)
        {
            bool allUpper = true;
            foreach (char c in original) if (char.IsLetter(c) && !char.IsUpper(c)) { allUpper = false; break; }
            return allUpper ? translated.ToUpperInvariant() : translated;
        }
    }

    /// <summary>
    ///  Pone en el idioma actual un texto de TextMeshPro de la ESCENA (botones
    ///  del menú principal, paneles del multijugador). Recuerda el texto
    ///  original en español y lo vuelve a traducir cuando cambia el idioma.
    ///  <see cref="LocText.Install"/> se lo añade a todos los textos de un objeto.
    /// </summary>
    public class LocText : MonoBehaviour
    {
        private TMPro.TMP_Text text;
        private string original;

        public static void Install(Transform root)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (t.GetComponent<LocText>() != null) continue;
                // Lo que ESCRIBE el jugador no se toca (el texto de ejemplo sí).
                var field = t.GetComponentInParent<TMPro.TMP_InputField>(true);
                if (field != null && field.textComponent == t) continue;
                t.gameObject.AddComponent<LocText>();
            }
        }

        /// <summary>Cambia el texto ORIGINAL (español) desde código y lo traduce.</summary>
        public void SetSpanish(string es)
        {
            original = es;
            Apply();
        }

        private void Awake()
        {
            text = GetComponent<TMPro.TMP_Text>();
            original = text != null ? text.text : null;
        }

        private void OnEnable() { Loc.Changed += Apply; Apply(); }
        private void OnDisable() => Loc.Changed -= Apply;

        private string lastShown;

        private void Apply()
        {
            if (text == null) return;
            // Si alguien cambió el texto por código desde la última vez, ese es
            // el nuevo original (si no, al volver a activarse pondríamos el viejo).
            if (lastShown != null && text.text != lastShown) original = text.text;
            if (string.IsNullOrEmpty(original)) return;
            lastShown = Loc.T(original);
            text.text = lastShown;
        }

        private void LateUpdate()
        {
            // Textos que el código cambia a menudo (estado del lobby...): se
            // traducen solos al frame siguiente.
            if (text != null && lastShown != null && text.text != lastShown) Apply();
        }
    }
}
