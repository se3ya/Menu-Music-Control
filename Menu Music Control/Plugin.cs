using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.Audio;
using TMPro;
using HarmonyLib;
using System;

namespace MainMenuMusicVolumeMod
{
    [BepInPlugin("seeya.MenuMusicControl", "Menu Music Control", "1.0.0")]
    public class MainMenuMusicVolumeMod : BaseUnityPlugin
    {
        public ConfigEntry<float> volumeConfig;
        public ConfigEntry<bool> disableSlider;
        private Slider volumeSlider;
        private Harmony harmony;
        private bool mainMenuSliderSetup = false;
        private AudioSource menuMusicSource;
        private AudioMixer audioMixer;
        private float pendingVolume = -1f;
        private bool isDraggingSlider = false;

        public void Awake()
        {
            volumeConfig = Config.Bind(
                "Main Menu Music",
                "Volume",
                0.5f,
                new ConfigDescription("Main menu music volume (0.0 to 1.0)", new AcceptableValueRange<float>(0f, 1f))
            );

            disableSlider = Config.Bind(
                "Main Menu Music",
                "DisableSlider",
                false,
                "Set to true to hide the volume slider in the main menu settings. Volume can then only be adjusted via this config file."
            );

            harmony = new Harmony("seeya.MenuMusicControl");
            harmony.PatchAll(typeof(MenuMusicPatches));

            SceneManager.activeSceneChanged += OnSceneChanged;

            audioMixer = Resources.Load<AudioMixer>("MainMixer");
            ApplyVolumeToMenuMusic(volumeConfig.Value);
        }

        private void Start()
        {
            if (SceneManager.GetActiveScene().name == "MainMenu")
            {
                ApplyVolumeToMenuMusic(volumeConfig.Value);
            }
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
        }

        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            if (newScene.name != "MainMenu")
            {
                mainMenuSliderSetup = false;
                volumeSlider = null;
                if (menuMusicSource != null && volumeConfig.Value == 0f)
                {
                    menuMusicSource.Stop();
                }
                else if (menuMusicSource != null)
                {
                    menuMusicSource.volume = volumeConfig.Value;
                }
            }
            if (newScene.name == "MainMenu" || newScene.name == "SampleSceneRelay")
            {
                ApplyVolumeToMenuMusic(volumeConfig.Value);
            }
        }

        private void Update()
        {
            if (SceneManager.GetActiveScene().name == "MainMenu")
            {
                GameObject menuContainer = GameObject.Find("Canvas/MenuContainer");
                if (menuContainer != null)
                {
                    Transform settingsPanel = menuContainer.transform.Find("SettingsPanel");
                    if (settingsPanel != null && settingsPanel.gameObject.activeInHierarchy && !disableSlider.Value)
                    {
                        if (!mainMenuSliderSetup)
                        {
                            SetupVolumeSlider(settingsPanel);
                            mainMenuSliderSetup = true;
                        }
                    }
                    else
                    {
                        mainMenuSliderSetup = false;
                    }
                }

                if (!isDraggingSlider && pendingVolume >= 0f)
                {
                    volumeConfig.Value = pendingVolume;
                    ApplyVolumeToMenuMusic(pendingVolume);
                    pendingVolume = -1f;
                }
                else if (!isDraggingSlider)
                {
                    ApplyVolumeToMenuMusic(volumeConfig.Value);
                }
            }
        }

        private void SetupVolumeSlider(Transform settingsPanel)
        {
            if (disableSlider.Value) return;

            Transform existingVolumeSetting = settingsPanel.Find("VolumeSetting");
            if (existingVolumeSetting != null)
            {
                UnityEngine.Object.Destroy(existingVolumeSetting.gameObject);
            }

            Transform brightnessSetting = settingsPanel.Find("BrightnessSetting");
            if (brightnessSetting == null)
            {
                Logger.LogError("BrightnessSetting not found in SettingsPanel!");
                return;
            }

            GameObject volumeSetting = UnityEngine.Object.Instantiate(brightnessSetting.gameObject, settingsPanel);
            volumeSetting.name = "VolumeSetting";

            RectTransform volumeRect = volumeSetting.GetComponent<RectTransform>();
            RectTransform brightnessRect = brightnessSetting.GetComponent<RectTransform>();
            Vector2 brightnessPos = brightnessRect.anchoredPosition;
            volumeRect.anchoredPosition = new Vector2(brightnessPos.x, brightnessPos.y - 50f);

            Transform textTransform = volumeSetting.transform.Find("Text (1)");
            if (textTransform != null)
            {
                TMP_Text textComponent = textTransform.GetComponent<TMP_Text>();
                if (textComponent != null)
                {
                    textComponent.text = "Menu Volume:";
                }
                else
                {
                    Logger.LogError("TMP_Text component not found on Text (1)!");
                }
            }
            else
            {
                Logger.LogError("Text (1) GameObject not found in VolumeSetting!");
            }

            Transform sliderTransform = volumeSetting.transform.Find("Slider");
            if (sliderTransform != null)
            {
                volumeSlider = sliderTransform.GetComponent<Slider>();
                if (volumeSlider != null)
                {
                    int listenerCount = volumeSlider.onValueChanged.GetPersistentEventCount();
                    if (listenerCount > 0)
                    {
                        volumeSlider.onValueChanged.SetPersistentListenerState(0, UnityEventCallState.Off);
                    }
                    volumeSlider.onValueChanged.RemoveAllListeners();

                    volumeSlider.minValue = 0f;
                    volumeSlider.maxValue = 100f;
                    volumeSlider.wholeNumbers = false;
                    volumeSlider.value = volumeConfig.Value * 100f;

                    ApplyVolumeToMenuMusic(volumeConfig.Value);

                    var eventTrigger = volumeSlider.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    var beginDragEntry = new UnityEngine.EventSystems.EventTrigger.Entry
                    {
                        eventID = UnityEngine.EventSystems.EventTriggerType.BeginDrag
                    };
                    beginDragEntry.callback.AddListener((data) => { isDraggingSlider = true; });
                    eventTrigger.triggers.Add(beginDragEntry);

                    var endDragEntry = new UnityEngine.EventSystems.EventTrigger.Entry
                    {
                        eventID = UnityEngine.EventSystems.EventTriggerType.EndDrag
                    };
                    endDragEntry.callback.AddListener((data) => { isDraggingSlider = false; });
                    eventTrigger.triggers.Add(endDragEntry);

                    volumeSlider.onValueChanged.AddListener((value) =>
                    {
                        float normalizedValue = value / 100f;
                        pendingVolume = normalizedValue;
                        ApplyVolumeToMenuMusic(normalizedValue);
                    });

                    Transform confirmButtonTransform = settingsPanel.Find("Confirm");
                    if (confirmButtonTransform != null)
                    {
                        Button confirmButton = confirmButtonTransform.GetComponent<Button>();
                        if (confirmButton != null)
                        {
                            confirmButton.onClick.AddListener(() =>
                            {
                                if (volumeSlider != null)
                                {
                                    volumeSlider.value = volumeConfig.Value * 100f;
                                    ApplyVolumeToMenuMusic(volumeConfig.Value);
                                    if (menuMusicSource != null && volumeConfig.Value == 0f)
                                    {
                                        menuMusicSource.Stop();
                                    }
                                }
                            });
                        }
                        else
                        {
                            Logger.LogError("Confirm GameObject found, but no Button component!");
                        }
                    }
                    else
                    {
                        Logger.LogError("Confirm GameObject not found in SettingsPanel!");
                    }
                }
                else
                {
                    Logger.LogError("Slider component not found in VolumeSetting!");
                    return;
                }
            }
            else
            {
                Logger.LogError("Slider GameObject not found in VolumeSetting!");
                return;
            }
        }

        private void ApplyVolumeToMenuMusic(float volume)
        {
            bool appliedToRelevantSource = false;

            GameObject menuManager = GameObject.Find("MenuManager");
            if (menuManager != null)
            {
                Transform menu1Transform = menuManager.transform.Find("Menu1");
                if (menu1Transform != null)
                {
                    AudioSource menu1Source = menu1Transform.GetComponent<AudioSource>();
                    if (menu1Source != null)
                    {
                        menu1Source.volume = volume;
                        appliedToRelevantSource = true;
                        if (menuMusicSource == null || (menu1Source.isPlaying && menuMusicSource != menu1Source))
                        {
                            menuMusicSource = menu1Source;
                        }
                    }
                }

                AudioSource managerSource = menuManager.GetComponent<AudioSource>();
                if (managerSource != null)
                {
                    managerSource.volume = volume;
                    appliedToRelevantSource = true;
                    if (menuMusicSource == null && managerSource.isPlaying) menuMusicSource = managerSource;
                }
            }

            GameObject[] candidates = {
                GameObject.Find("Music"),
                GameObject.Find("AudioManager"),
                GameObject.Find("SoundManager"),
                GameObject.Find("MenuMusic")
            };
            foreach (var candidate in candidates)
            {
                if (candidate != null)
                {
                    AudioSource source = candidate.GetComponent<AudioSource>();
                    if (source != null)
                    {
                        source.volume = volume;
                        appliedToRelevantSource = true;
                        if (menuMusicSource == null && source.isPlaying) menuMusicSource = source;
                    }
                }
            }

            if (menuMusicSource != null)
            {
                menuMusicSource.volume = volume;
                appliedToRelevantSource = true;
            }

            AudioSource[] allSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
            foreach (AudioSource source in allSources)
            {
                if (source.isPlaying && source != menuMusicSource)
                {
                    source.volume = volume;
                    appliedToRelevantSource = true;
                }
            }

            if (audioMixer != null)
            {
                float mixerVolume = Mathf.Lerp(-80f, 0f, volume);
                if (audioMixer.SetFloat("MusicVolume", mixerVolume))
                {
                    appliedToRelevantSource = true;
                }
                else if (audioMixer.SetFloat("MasterVolume", mixerVolume))
                {
                    appliedToRelevantSource = true;
                }
                else if (audioMixer.SetFloat("MenuMusicVolume", mixerVolume))
                {
                    appliedToRelevantSource = true;
                }
                else
                {
                    Logger.LogWarning("Failed to apply volume to any AudioMixer group");
                }
            }

            if (!appliedToRelevantSource)
            {
                Logger.LogWarning("No relevant AudioSource or AudioMixer found to apply volume");
            }
        }

        [HarmonyPatch]
        private static class MenuMusicPatches
        {
            [HarmonyPatch(typeof(MenuManager), "Awake")]
            [HarmonyPrefix]
            public static void PreAwake(MenuManager __instance)
            {
                MainMenuMusicVolumeMod instance = UnityEngine.Object.FindObjectOfType<MainMenuMusicVolumeMod>();
                if (instance == null) return;

                AudioSource[] sources = __instance.GetComponents<AudioSource>();
                foreach (var source in sources)
                {
                    source.volume = instance.volumeConfig.Value;
                    if (instance.menuMusicSource == null && source.isPlaying) instance.menuMusicSource = source;
                }

                Transform menu1 = __instance.transform.Find("Menu1");
                if (menu1 != null)
                {
                    AudioSource menu1Source = menu1.GetComponent<AudioSource>();
                    if (menu1Source != null)
                    {
                        menu1Source.volume = instance.volumeConfig.Value;
                        if (instance.menuMusicSource == null && menu1Source.isPlaying) instance.menuMusicSource = menu1Source;
                    }
                }
            }

            [HarmonyPatch(typeof(MenuManager), "Start")]
            [HarmonyPrefix]
            public static void PreStart(MenuManager __instance)
            {
                MainMenuMusicVolumeMod instance = UnityEngine.Object.FindObjectOfType<MainMenuMusicVolumeMod>();
                if (instance == null) return;

                AudioSource[] sources = __instance.GetComponents<AudioSource>();
                foreach (var source in sources)
                {
                    source.volume = instance.volumeConfig.Value;
                    if (instance.menuMusicSource == null) instance.menuMusicSource = source;
                }

                Transform menu1 = __instance.transform.Find("Menu1");
                if (menu1 != null)
                {
                    AudioSource menu1Source = menu1.GetComponent<AudioSource>();
                    if (menu1Source != null)
                    {
                        menu1Source.volume = instance.volumeConfig.Value;
                        if (instance.menuMusicSource == null && menu1Source.isPlaying) instance.menuMusicSource = menu1Source;
                    }
                }
            }

            [HarmonyPatch(typeof(MenuManager), "Awake")]
            [HarmonyPostfix]
            public static void PatchMainMenuAwake(MenuManager __instance)
            {
                MainMenuMusicVolumeMod instance = UnityEngine.Object.FindObjectOfType<MainMenuMusicVolumeMod>();
                if (instance == null || instance.disableSlider.Value) return;

                GameObject menuContainer = GameObject.Find("Canvas/MenuContainer");
                if (menuContainer == null) return;

                Transform settingsPanel = menuContainer.transform.Find("SettingsPanel");
                if (settingsPanel == null) return;

                instance.SetupVolumeSlider(settingsPanel);
            }

            [HarmonyPatch(typeof(AudioSource), "Play", new Type[] { })]
            [HarmonyPrefix]
            public static void PreAudioSourcePlay(AudioSource __instance)
            {
                MainMenuMusicVolumeMod instance = UnityEngine.Object.FindObjectOfType<MainMenuMusicVolumeMod>();
                if (instance == null) return;

                if (SceneManager.GetActiveScene().name == "MainMenu")
                {
                    __instance.volume = instance.volumeConfig.Value;
                    if (__instance.isPlaying && instance.menuMusicSource != __instance)
                    {
                        instance.menuMusicSource = __instance;
                    }
                }
            }
        }
    }
}