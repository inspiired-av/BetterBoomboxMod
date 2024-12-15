using System;
using System.Collections;
using BepInEx;
using BepInEx.Logging;
using CustomBoomboxTracks.Managers;
using CustomBoomboxTracks.Utilities;
using CustomBoomboxTracks.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace CustomBoomboxTracks
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class BoomboxPlugin : BaseUnityPlugin
    {
        private const string GUID = "inspiired.lethalcompany.boomboxmod";
        private const string NAME = "BetterBoomboxMusic";
        private const string VERSION = "1.0.2";

        private static BoomboxPlugin Instance;
        private static bool downloadsStarted = false;

        void Awake()
        {
            Instance = this;

            LogInfo("Loading...");

            DownloadManager.GenerateFolders();
            Configuration.Config.Init();

            // Listen for scene changes
            SceneManager.sceneLoaded += OnSceneLoaded;

            // If MainMenu is already loaded, trigger downloads immediately
            if (SceneManager.GetActiveScene().name == "MainMenu" && !downloadsStarted)
            {
                StartDownloads();
            }

            var harmony = new Harmony(GUID);
            harmony.PatchAll();

            LogInfo("Loading Complete!");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "MainMenu" && !downloadsStarted)
            {
                StartDownloads();
            }
        }

        private void StartDownloads()
        {
            if (downloadsStarted) return;

            downloadsStarted = true; // Ensure downloads are only triggered once

            LogInfo("MainMenu scene detected. Starting downloads...");

            var downloadLinks = Configuration.Config.SongDownloadLinks;

            if (downloadLinks.Count == 0)
            {
                LogWarning("No download links found in the configuration.");
                return;
            }

            DownloadManager.DownloadSongsFromConfig(downloadLinks);
        }

        #region logging
        internal static void LogDebug(string message) => Instance.Log(message, LogLevel.Debug);
        internal static void LogInfo(string message) => Instance.Log(message, LogLevel.Info);
        internal static void LogWarning(string message) => Instance.Log(message, LogLevel.Warning);
        internal static void LogError(string message) => Instance.Log(message, LogLevel.Error);
        internal static void LogError(Exception ex) => Instance.Log($"{ex.Message}\n{ex.StackTrace}", LogLevel.Error);
        private void Log(string message, LogLevel logLevel) => Logger.Log(logLevel, message);
        #endregion
    }
}
