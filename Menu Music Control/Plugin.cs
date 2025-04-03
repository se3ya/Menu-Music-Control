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
using BepInEx.Logging;
using System.Collections;
using System.Reflection;

namespace MainMenuMusicVolumeMod
{
    [BepInPlugin("seeya.MenuMusicControl", "Menu Music Control", "1.0.0")]
    [BepInDependency("ShaosilGaming.GeneralImprovements", BepInDependency.DependencyFlags.SoftDependency)]
    public class MainMenuMusicVolumeMod : BaseUnityPlugin
    {
        public static ConfigEntry<float> volumeConfig;
        internal new static ManualLogSource Logger { get; private set; } = null;
        public static ConfigEntry<bool> disableSlider;
        private static Slider volumeSlider;
        private Harmony harmony;
        private bool mainMenuSliderSetup = false;
        private static AudioSource menuMusicSource;
        private static AudioMixer audioMixer;
        private static float pendingVolume = -1f;
        private static bool isDraggingSlider = false;
        private static bool hasUnconfirmedChanges = false;
        private static TMP_Text changesNotAppliedText;
        private float timeSinceStartup = 0f;
        private bool isInitialStartup = true;
        private bool isFirstTimeSetup = true;
        private static bool isGeneralImprovementsLoaded = false;

        public void Awake()
        {
            Logger = base.Logger;

            // check for GeneralImprovements
            isGeneralImprovementsLoaded = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("ShaosilGaming.GeneralImprovements");
            Logger.LogInfo($"GeneralImprovements detected: {isGeneralImprovementsLoaded}");

            AudioListener.volume = 0f;

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
                "Set to true to hide the volume slider in the main menu settings."
            );

            harmony = new Harmony("seeya.MenuMusicControl");
            harmony.PatchAll(typeof(MenuMusicPatches));

            audioMixer = Resources.Load<AudioMixer>("MainMixer");

            StartCoroutine(MuteStartupAudio());
        }

        private IEnumerator MuteStartupAudio()
        {
            Logger.LogInfo("Muting all AudioSources during startup.");
            AudioListener.volume = 0f;

            AudioSource[] allSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (var source in allSources)
            {
                source.mute = true;
            }

            yield return new WaitForSecondsRealtime(3f);

            ApplyVolumeToMenuMusic(volumeConfig.Value, false);

            allSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (var source in allSources)
            {
                if (source != null)
                {
                    source.mute = false;
                }
            }

            yield return new WaitForEndOfFrame();
            AudioListener.volume = 1f;

            isInitialStartup = false;
            Logger.LogInfo("Startup muting complete.");
        }

        private void Update()
        {
            timeSinceStartup += Time.deltaTime;

            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == "InitScene" || currentScene == "MainMenu")
            {
                GameObject menuContainer = GameObject.Find("Canvas/MenuContainer");
                if (menuContainer != null)
                {
                    Transform settingsPanel = menuContainer.transform.Find("SettingsPanel");
                    if (settingsPanel != null && settingsPanel.gameObject.activeInHierarchy && !disableSlider.Value)
                    {
                        if (!mainMenuSliderSetup)
                        {
                            Logger.LogInfo($"Setting up volume slider in scene: {currentScene}");
                            SetupVolumeSlider(settingsPanel);
                            mainMenuSliderSetup = true;
                        }
                    }
                    else
                    {
                        mainMenuSliderSetup = false;
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (mainMenuSliderSetup && volumeSlider != null && !isDraggingSlider && !hasUnconfirmedChanges)
            {
                float targetValue = volumeConfig.Value * 100f;
                if (Mathf.Abs(volumeSlider.value - targetValue) > 0.01f)
                {
                    Logger.LogInfo($"LateUpdate syncing slider value to config: {targetValue}");
                    volumeSlider.value = targetValue;
                    volumeSlider.SetValueWithoutNotify(targetValue);
                    ApplyVolumeToMenuMusic(volumeConfig.Value, false);
                }
            }
        }

        private static void SetupVolumeSlider(Transform settingsPanel)
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
            volumeSetting.SetActive(true);

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
                    volumeSlider.onValueChanged.RemoveAllListeners();
                    volumeSlider.minValue = 0f;
                    volumeSlider.maxValue = 100f;
                    
                    SettingsOption settingsOption = sliderTransform.GetComponent<SettingsOption>();
                    if (settingsOption != null)
                    {
                        UnityEngine.Object.Destroy(settingsOption);
                        Logger.LogInfo("Removed SettingsOption component from Slider.");
                    }

                    float currentVolume = volumeConfig.Value * 100f;
                    LayoutRebuilder.ForceRebuildLayoutImmediate(volumeSlider.GetComponent<RectTransform>());

                    MainMenuMusicVolumeMod instance = UnityEngine.Object.FindFirstObjectByType<MainMenuMusicVolumeMod>();
                    bool isFirstSetup = instance != null && instance.isFirstTimeSetup;

                    volumeSlider.value = currentVolume;
                    volumeSlider.SetValueWithoutNotify(currentVolume);

                    volumeSlider.gameObject.AddComponent<SliderVisualFixer>().Initialize(currentVolume, 0.5f);

                    Logger.LogInfo($"Set slider value to config value on setup: {currentVolume}");

                    var eventTrigger = volumeSlider.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>() ?? volumeSlider.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    eventTrigger.triggers.Clear();

                    var beginDragEntry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.BeginDrag };
                    beginDragEntry.callback.AddListener((data) => { isDraggingSlider = true; });
                    eventTrigger.triggers.Add(beginDragEntry);

                    var endDragEntry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.EndDrag };
                    endDragEntry.callback.AddListener((data) => { isDraggingSlider = false; });
                    eventTrigger.triggers.Add(endDragEntry);

                    Transform changesNotAppliedTransform = settingsPanel.Find("Headers/ChangesNotApplied");
                    if (changesNotAppliedTransform != null)
                    {
                        changesNotAppliedText = changesNotAppliedTransform.GetComponent<TMP_Text>();
                        if (changesNotAppliedText != null)
                        {
                            changesNotAppliedText.enabled = false;
                            Logger.LogInfo("Found ChangesNotApplied text component.");
                        }
                    }

                    volumeSlider.onValueChanged.AddListener((value) =>
                    {
                        if (isFirstSetup)
                        {
                            isFirstSetup = false;
                            instance.isFirstTimeSetup = false;
                            return;
                        }

                        Logger.LogInfo($"Slider value changed to: {value}");
                        float normalizedValue = value / 100f;
                        pendingVolume = normalizedValue;
                        hasUnconfirmedChanges = true;
                        ApplyVolumeToMenuMusic(normalizedValue, false);
                        if (changesNotAppliedText != null)
                        {
                            changesNotAppliedText.enabled = true;
                            CanvasRenderer canvasRenderer = changesNotAppliedText.GetComponent<CanvasRenderer>();
                            if (canvasRenderer != null)
                            {
                                canvasRenderer.cullTransparentMesh = false;
                            }
                            CanvasGroup[] canvasGroups = changesNotAppliedText.GetComponentsInParent<CanvasGroup>();
                            foreach (var canvasGroup in canvasGroups)
                            {
                                if (canvasGroup.alpha < 1f)
                                {
                                    canvasGroup.alpha = 1f;
                                }
                            }
                            Logger.LogInfo("Enabled 'Unconfirmed changes' text on slider change.");
                        }
                    });

                    Transform confirmButtonTransform = settingsPanel.Find("Confirm");
                    if (confirmButtonTransform != null)
                    {
                        Button confirmButton = confirmButtonTransform.GetComponent<Button>();
                        if (confirmButton != null)
                        {
                            confirmButton.onClick.AddListener(() =>
                            {
                                MainMenuMusicVolumeMod modInstance = UnityEngine.Object.FindFirstObjectByType<MainMenuMusicVolumeMod>();
                                if (modInstance != null && modInstance.timeSinceStartup < 5f)
                                {
                                    Logger.LogWarning("Confirm button clicked within 5 seconds of startup, ignoring.");
                                    return;
                                }

                                Logger.LogInfo("Confirm button onClick event triggered.");
                                if (pendingVolume >= 0f)
                                {
                                    volumeConfig.Value = pendingVolume;
                                    pendingVolume = -1f;
                                    hasUnconfirmedChanges = false;
                                    if (changesNotAppliedText != null)
                                    {
                                        changesNotAppliedText.enabled = false;
                                    }
                                    ApplyVolumeToMenuMusic(volumeConfig.Value, true);
                                    Logger.LogInfo("Saved menu music volume to config on confirm.");
                                }
                            });
                        }
                    }
                }
            }
        }

        private static void ApplyVolumeToMenuMusic(float volume, bool saveToConfig)
        {
            Logger.LogInfo($"Applying volume to menu music: {volume} (Save to config: {saveToConfig})");
            bool appliedToRelevantSource = false;
            
            IngamePlayerSettings.Instance.SettingsAudio.PlayOneShot(GameNetworkManager.Instance.buttonTuneSFX);
            
            GameObject menuManager = GameObject.Find("MenuManager");
            if (menuManager != null)
            {
                // target all AudioSources on MenuManager including those added by GeneralImprovements
                AudioSource[] allSources = menuManager.GetComponents<AudioSource>();
                foreach (var source in allSources)
                {
                    source.volume = volume;
                    appliedToRelevantSource = true;
                    if (menuMusicSource == null || (source.isPlaying && menuMusicSource != source))
                    {
                        menuMusicSource = source;
                        Logger.LogDebug($"Assigned menuMusicSource to {source.gameObject.name}");
                    }
                }

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
                    }
                }
            }

            if (menuMusicSource != null)
            {
                menuMusicSource.volume = volume;
                appliedToRelevantSource = true;
            }

            if (audioMixer != null)
            {
                float mixerVolume = Mathf.Lerp(-80f, 0f, volume);
                if (audioMixer.SetFloat("MusicVolume", mixerVolume) ||
                    audioMixer.SetFloat("MasterVolume", mixerVolume) ||
                    audioMixer.SetFloat("MenuMusicVolume", mixerVolume))
                {
                    appliedToRelevantSource = true;
                }
            }

            if (!appliedToRelevantSource)
            {
                Logger.LogWarning("No primary AudioSource found, checking all sources...");
                AudioSource[] allSources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
                foreach (AudioSource source in allSources)
                {
                    if (source.isPlaying && source != menuMusicSource)
                    {
                        source.volume = volume;
                        appliedToRelevantSource = true;
                    }
                }
            }

            if (!appliedToRelevantSource)
            {
                Logger.LogWarning("No relevant AudioSource or AudioMixer found to apply volume.");
            }

            if (menuMusicSource != null)
            {
                if (volume == 0f)
                {
                    menuMusicSource.Stop();
                }
                else if (!menuMusicSource.isPlaying && volumeConfig.Value == 0f)
                {
                    MainMenuMusicVolumeMod instance = UnityEngine.Object.FindFirstObjectByType<MainMenuMusicVolumeMod>();
                    if (instance == null || !instance.isInitialStartup)
                    {
                        menuMusicSource.Play();
                    }
                }
            }

            if (saveToConfig)
            {
                volumeConfig.Value = volume;
            }

            if (volumeSlider != null)
            {
                volumeSlider.SetValueWithoutNotify(volume * 100f);
            }
        }

        private class SliderVisualFixer : MonoBehaviour
        {
            private float targetValue;
            private float delay;

            public void Initialize(float value, float delayTime)
            {
                targetValue = value;
                delay = delayTime;
                Invoke("UpdateSlider", delay);
            }

            private void UpdateSlider()
            {
                Slider slider = GetComponent<Slider>();
                if (slider != null)
                {
                    slider.SetValueWithoutNotify(targetValue);
                    MainMenuMusicVolumeMod.Logger.LogInfo($"SliderVisualFixer updated slider to: {targetValue} after delay of {delay} seconds");
                }
                Destroy(this);
            }
        }

        [HarmonyPatch(typeof(Button))]
        [HarmonyPatch("Press")]
        private static class ButtonSoundPatch
        {
            private static bool Prefix()
            {
                MainMenuMusicVolumeMod instance = UnityEngine.Object.FindFirstObjectByType<MainMenuMusicVolumeMod>();
                if (instance != null && instance.isInitialStartup)
                {
                    Logger.LogInfo("Blocking button sound during startup.");
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Slider))]
        [HarmonyPatch("OnPointerDown")]
        private static class SliderSoundPatch
        {
            private static bool Prefix()
            {
                MainMenuMusicVolumeMod instance = UnityEngine.Object.FindFirstObjectByType<MainMenuMusicVolumeMod>();
                if (instance != null && instance.isInitialStartup)
                {
                    Logger.LogInfo("Blocking slider sound during startup.");
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch]
        private static class MenuMusicPatches
        {
            [HarmonyPatch(typeof(MenuManager), "Awake")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.Low)] // Run after GeneralImprovements
            public static void PreAwake(MenuManager __instance)
            {
                MainMenuMusicVolumeMod instance = UnityEngine.Object.FindFirstObjectByType<MainMenuMusicVolumeMod>();
                if (instance != null && instance.isInitialStartup)
                {
                    Logger.LogInfo("MenuManager Awake called during startup, deferring volume application.");
                    return;
                }

                ApplyVolumeToAllMenuSources(__instance);
            }

            [HarmonyPatch(typeof(MenuManager), "Start")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.Low)] // Run after GeneralImprovements
            public static void PreStart(MenuManager __instance)
            {
                MainMenuMusicVolumeMod instance = UnityEngine.Object.FindFirstObjectByType<MainMenuMusicVolumeMod>();
                if (instance != null && instance.isInitialStartup)
                {
                    Logger.LogInfo("MenuManager Start called during startup, deferring volume application.");
                    return;
                }

                ApplyVolumeToAllMenuSources(__instance);
            }

            [HarmonyPatch(typeof(MenuManager), "Start")]
            [HarmonyPostfix]
            public static void PatchMainMenuAwake(MenuManager __instance)
            {
                if (MainMenuMusicVolumeMod.disableSlider.Value) return;

                string currentScene = SceneManager.GetActiveScene().name;
                if (currentScene != "InitScene" && currentScene != "MainMenu") return;

                GameObject menuContainer = GameObject.Find("Canvas/MenuContainer");
                if (menuContainer == null) return;

                Transform settingsPanel = menuContainer.transform.Find("SettingsPanel");
                if (settingsPanel == null) return;

                MainMenuMusicVolumeMod.SetupVolumeSlider(settingsPanel);
            }

            [HarmonyPatch(typeof(AudioSource), "Play", new Type[] { })]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.Low)]
            public static void PreAudioSourcePlay(AudioSource __instance)
            {
                MainMenuMusicVolumeMod instance = UnityEngine.Object.FindFirstObjectByType<MainMenuMusicVolumeMod>();
                if (instance == null) return;

                if (instance.isInitialStartup)
                {
                    Logger.LogInfo($"AudioSource.Play called during startup on {__instance.gameObject.name}, muting.");
                    __instance.mute = true;
                    return;
                }

                string currentScene = SceneManager.GetActiveScene().name;
                if (currentScene == "InitScene" || currentScene == "MainMenu")
                {
                    __instance.volume = MainMenuMusicVolumeMod.volumeConfig.Value;
                    if (__instance.isPlaying && MainMenuMusicVolumeMod.menuMusicSource != __instance)
                    {
                        MainMenuMusicVolumeMod.menuMusicSource = __instance;
                    }
                }
            }

            [HarmonyPatch(typeof(SettingsOption), "CancelSettings")]
            [HarmonyPostfix]
            public static void PostCancelSettings(SettingsOption __instance)
            {
                Logger.LogInfo("SettingsOption.CancelSettings method called.");
                if (hasUnconfirmedChanges)
                {
                    Logger.LogInfo($"Reverting volume to last saved value: {volumeConfig.Value}");
                    ApplyVolumeToMenuMusic(volumeConfig.Value, false);
                    pendingVolume = -1f;
                    hasUnconfirmedChanges = false;
                    if (volumeSlider != null)
                    {
                        volumeSlider.SetValueWithoutNotify(volumeConfig.Value * 100f);
                    }
                    if (changesNotAppliedText != null)
                    {
                        changesNotAppliedText.enabled = false;
                    }
                }
            }

            private static void ApplyVolumeToAllMenuSources(MenuManager __instance)
            {
                AudioSource[] sources = __instance.GetComponents<AudioSource>();
                foreach (var source in sources)
                {
                    source.volume = MainMenuMusicVolumeMod.volumeConfig.Value;
                    if (MainMenuMusicVolumeMod.menuMusicSource == null && source.isPlaying)
                    {
                        MainMenuMusicVolumeMod.menuMusicSource = source;
                        Logger.LogDebug($"Assigned menuMusicSource to {source.gameObject.name} in MenuManager");
                    }
                }

                Transform menu1 = __instance.transform.Find("Menu1");
                if (menu1 != null)
                {
                    AudioSource menu1Source = menu1.GetComponent<AudioSource>();
                    if (menu1Source != null)
                    {
                        menu1Source.volume = MainMenuMusicVolumeMod.volumeConfig.Value;
                        if (MainMenuMusicVolumeMod.menuMusicSource == null && menu1Source.isPlaying)
                        {
                            MainMenuMusicVolumeMod.menuMusicSource = menu1Source;
                        }
                    }
                }
            }
        }
    }
}
