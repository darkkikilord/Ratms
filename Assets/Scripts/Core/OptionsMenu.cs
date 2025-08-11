using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using TMPro;

namespace Ratms.UI
{
    public class OptionsMenu : MonoBehaviour
    {
        [Header("Mixer")]
        [SerializeField] private AudioMixer mixer;   // Asigna RatmsAudioMixer
        [SerializeField] private string masterParam = "MasterVol";
        [SerializeField] private string musicParam = "MusicVol";
        [SerializeField] private string sfxParam = "SFXVol";

        [Header("UI")]
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private TMP_Text masterValueText;
        [SerializeField] private TMP_Text musicValueText;
        [SerializeField] private TMP_Text sfxValueText;

        [Header("Back")]
        [SerializeField] private MainMenuController mainMenu;

        private const string K_MASTER = "opt_master";
        private const string K_MUSIC = "opt_music";
        private const string K_SFX = "opt_sfx";

        private void OnEnable()
        {
            LoadPrefs();
            ApplyAll();
            UpdateLabels();
            HookEvents(true);
        }

        private void OnDisable()
        {
            HookEvents(false);
        }

        private void HookEvents(bool hook)
        {
            if (masterSlider != null)
            {
                if (hook) masterSlider.onValueChanged.AddListener(OnMaster);
                else masterSlider.onValueChanged.RemoveListener(OnMaster);
            }
            if (musicSlider != null)
            {
                if (hook) musicSlider.onValueChanged.AddListener(OnMusic);
                else musicSlider.onValueChanged.RemoveListener(OnMusic);
            }
            if (sfxSlider != null)
            {
                if (hook) sfxSlider.onValueChanged.AddListener(OnSFX);
                else sfxSlider.onValueChanged.RemoveListener(OnSFX);
            }
        }

        private void LoadPrefs()
        {
            if (masterSlider) masterSlider.value = PlayerPrefs.GetFloat(K_MASTER, 0.8f);
            if (musicSlider) musicSlider.value = PlayerPrefs.GetFloat(K_MUSIC, 0.8f);
            if (sfxSlider) sfxSlider.value = PlayerPrefs.GetFloat(K_SFX, 0.8f);
        }

        private void SavePrefs()
        {
            if (masterSlider) PlayerPrefs.SetFloat(K_MASTER, masterSlider.value);
            if (musicSlider) PlayerPrefs.SetFloat(K_MUSIC, musicSlider.value);
            if (sfxSlider) PlayerPrefs.SetFloat(K_SFX, sfxSlider.value);
            PlayerPrefs.Save();
        }

        private void ApplyAll()
        {
            SetDb(masterParam, masterSlider ? masterSlider.value : 0.8f);
            SetDb(musicParam, musicSlider ? musicSlider.value : 0.8f);
            SetDb(sfxParam, sfxSlider ? sfxSlider.value : 0.8f);
        }

        // [0..1] → dB (0 => -80 dB, 1 => 0 dB) con curva suave
        private void SetDb(string param, float value01)
        {
            if (!mixer) return;
            float dB = Mathf.Lerp(-80f, 0f, Mathf.Pow(Mathf.Clamp01(value01), 1.8f));
            mixer.SetFloat(param, dB);
        }

        private void OnMaster(float v)
        {
            SetDb(masterParam, v);
            if (masterValueText) masterValueText.text = Percent(v);
            SavePrefs();
        }

        private void OnMusic(float v)
        {
            SetDb(musicParam, v);
            if (musicValueText) musicValueText.text = Percent(v);
            SavePrefs();
        }

        private void OnSFX(float v)
        {
            SetDb(sfxParam, v);
            if (sfxValueText) sfxValueText.text = Percent(v);
            SavePrefs();
        }

        private string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        private void UpdateLabels()
        {
            if (masterValueText && masterSlider) masterValueText.text = Percent(masterSlider.value);
            if (musicValueText && musicSlider) musicValueText.text = Percent(musicSlider.value);
            if (sfxValueText && sfxSlider) sfxValueText.text = Percent(sfxSlider.value);
        }

        public void OnBack()
        {
            SavePrefs();
            if (mainMenu) mainMenu.OnBackFromOptions();
        }
    }
}
