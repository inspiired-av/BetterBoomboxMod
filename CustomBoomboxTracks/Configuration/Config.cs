using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using CustomBoomboxTracks.Managers;

namespace CustomBoomboxTracks.Configuration
{
    internal static class Config
    {
        private const string CONFIG_FILE_NAME = "boombox.cfg";

        private static ConfigFile _config;
        private static ConfigEntry<bool> _useDefaultSongs;
        private static ConfigEntry<bool> _streamAudioFromDisk;
        private static ConfigEntry<string> _songDownloadUrls;

        public static void Init()
        {
            BoomboxPlugin.LogInfo("Initializing config...");
            var filePath = Path.Combine(Paths.ConfigPath, CONFIG_FILE_NAME);
            _config = new ConfigFile(filePath, true);

            // Define the config entries
            _useDefaultSongs = _config.Bind(
                "General",
                "Use Default Songs",
                false,
                "Include the default songs in the rotation."
            );

            _streamAudioFromDisk = _config.Bind(
                "General",
                "Stream Audio From Disk",
                false,
                "Requires less memory and takes less time to load, but prevents playing the same song twice at once."
            );

            _songDownloadUrls = _config.Bind(
                "Downloads",
                "Song Download URLs",
                "",
                "Comma-separated list of URLs for song downloads. Supports direct Google Drive links (including .zip files). DOES NOT support Google Drive folder links. Make sure URL share settings are set to public."
            );

            BoomboxPlugin.LogInfo("Config initialized!");
        }

        private static void PrintConfig()
        {
            BoomboxPlugin.LogInfo($"Use Default Songs: {_useDefaultSongs.Value}");
            BoomboxPlugin.LogInfo($"Stream From Disk: {_streamAudioFromDisk.Value}");
            BoomboxPlugin.LogInfo($"Song Download URLs: {_songDownloadUrls.Value}");
        }

        // Properties to access the config values
        public static bool UseDefaultSongs => _useDefaultSongs.Value || AudioManager.HasNoSongs;
        public static bool StreamFromDisk => _streamAudioFromDisk.Value;

        // Returns a parsed list of URLs from the configuration
        public static List<string> SongDownloadLinks
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_songDownloadUrls.Value))
                {
                    return new List<string>();
                }

                return _songDownloadUrls.Value
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(url => url.Trim())
                    .ToList();
            }
        }
    }
}
