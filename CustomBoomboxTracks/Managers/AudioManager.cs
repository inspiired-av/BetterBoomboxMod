using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Sources;
using BepInEx;
using CustomBoomboxTracks.Configuration;
using CustomBoomboxTracks.Utilities;
using DunGen;
using UnityEngine;
using UnityEngine.Networking;

namespace CustomBoomboxTracks.Managers
{
    public static class DownloadManager
    {
        static readonly string directory = Path.Combine(Paths.BepInExRootPath, "Custom Songs", "Boombox Music");
        private static int pendingDownloads = 0;
        private static Action onDownloadsComplete;

        // Path to track downloaded files (name and size)
        static readonly string trackFilePath = Path.Combine(Paths.BepInExRootPath, "Custom Songs", "downloadedFiles.txt");

        public static void GenerateFolders()
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                BoomboxPlugin.LogInfo($"Created directory at {directory}");
            }
        }

        public static void DownloadSongsFromConfig(List<string> downloadLinks, Action onComplete = null)
        {
            // Ensure the directory exists
            GenerateFolders();

            if (downloadLinks == null || downloadLinks.Count == 0)
            {
                BoomboxPlugin.LogWarning("No download links provided in the config!");
                onComplete?.Invoke();
                return;
            }

            pendingDownloads = downloadLinks.Count;
            onDownloadsComplete = onComplete;
            BoomboxPlugin.LogInfo($"Starting downloads: {pendingDownloads} files to download.");

            foreach (var link in downloadLinks)
            {
                SharedCoroutineStarter.StartCoroutine(HandleDownload(link));
            }
        }

        private static IEnumerator HandleDownload(string url)
        {
            string fileId = null;

            // Ensure fileId is extracted correctly
            if (IsGoogleDriveLink(url))
            {
                fileId = ExtractGoogleDriveFileId(url);
            }

            // If the fileId is null, log an error and exit
            if (string.IsNullOrEmpty(fileId))
            {
                BoomboxPlugin.LogError($"Failed to extract fileId from the URL: {url}");
                yield break; // Exit if fileId extraction failed
            }

            // Perform the check before proceeding with the download
            if (HasFileBeenDownloadedBefore(fileId))
            {
                BoomboxPlugin.LogInfo($"Skipping download, file with ID {fileId} has been previously downloaded.");
                pendingDownloads--; // Decrease the counter even if the file is skipped
                BoomboxPlugin.LogInfo($"Pending downloads: {pendingDownloads} remaining.");

                if (pendingDownloads == 0 && onDownloadsComplete != null)
                {
                    BoomboxPlugin.LogInfo("All downloads complete.");
                    onDownloadsComplete.Invoke();
                    onDownloadsComplete = null;
                }

                yield break; // Skip this download
            }
            BoomboxPlugin.LogInfo($"Pending downloads: {pendingDownloads}");

            // Proceed with retrieving metadata (this includes handling redirects)
            yield return GetFileMetadataBeforeDownload(url);

        }

        private static bool HasFileBeenDownloadedBefore(string fileId)
        {
            BoomboxPlugin.LogInfo($"Checking if file {fileId} has been downloaded before.");
            if (!File.Exists(trackFilePath)) return false;

            foreach (var line in File.ReadAllLines(trackFilePath))
            {
                var data = line.Split(',');
                if (data.Length == 1 && data[0] == fileId)
                {
                    return true;
                }
            }
            BoomboxPlugin.LogInfo($"File {fileId} has not been downloaded before.");
            return false;
        }

        private static IEnumerator GetFileMetadataBeforeDownload(string url)
        {
            string fileId = IsGoogleDriveLink(url) ? ExtractGoogleDriveFileId(url) : null;
            string fileName = Path.GetFileName(url); // Default file name

            using (UnityWebRequest request = UnityWebRequest.Head(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    // Attempt to extract filename from Content-Disposition header if available
                    fileName = GetSanitizedFileName(request, fileName);

                    BoomboxPlugin.LogInfo($"Remote file for {fileName} (Google Drive ID: {fileId})");

                    // Proceed with the download logic (check for redirect, handle accordingly)
                    yield return IsGoogleDriveLink(url)
                        ? DownloadGoogleDriveFile(url, fileName, fileId)
                        : DownloadFile(url, fileName);
                }
                else
                {
                    BoomboxPlugin.LogError($"Failed to retrieve metadata for {url}: {request.error}");
                }
            }
        }

        private static string GetSanitizedFileName(UnityWebRequest request, string fileName)
        {
            // Attempt to extract filename from Content-Disposition header
            string disposition = request.GetResponseHeader("Content-Disposition");
            if (!string.IsNullOrEmpty(disposition) && disposition.Contains("filename="))
            {
                int fileNameIndex = disposition.IndexOf("filename=") + "filename=".Length;
                fileName = disposition.Substring(fileNameIndex).Trim('"');
            }

            // Fallback to GUID if filename is invalid or empty
            if (string.IsNullOrEmpty(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                fileName = $"{Guid.NewGuid()}.unknown";
            }

            // Sanitize file name by removing invalid characters
            return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        }

        private static bool IsGoogleDriveLink(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                return uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false; // Invalid URL
            }
        }

        private static string ExtractGoogleDriveFileId(string url)
        {
            // Regular expression to match the 'id' query parameter in the Google Drive URL
            string pattern = @"[?&]id=([a-zA-Z0-9_-]+)";
            Match match = Regex.Match(url, pattern);

            return match.Success ? match.Groups[1].Value : null;
        }

        private static IEnumerator DownloadGoogleDriveFile(string url, string fileName, string fileId)
        {
            BoomboxPlugin.LogInfo($"Processing Google Drive link: {url}");

            // Store the original URL for retries
            string originalUrl = url;

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                // Disable automatic redirect handling
                request.redirectLimit = 0;

                // Send the initial request
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    // No redirect, proceed with normal download
                    BoomboxPlugin.LogInfo("No redirect detected. Proceeding with normal download.");
                    yield return DownloadFile(url, fileName, fileId);
                }
                else if (request.responseCode == 302 || request.responseCode == 303)
                {
                    // Redirect detected, handle it
                    string redirectUrl = request.GetResponseHeader("Location");
                    BoomboxPlugin.LogInfo($"Redirect detected. Redirecting to: {redirectUrl}");

                    // Check if the redirect URL corresponds to a virus scan page
                    if (redirectUrl.Contains("drive.usercontent.google.com") && redirectUrl.Contains("download"))
                    {
                        // Fetch the HTML content of the redirected page
                        using (UnityWebRequest virusScanRequest = UnityWebRequest.Get(redirectUrl))
                        {
                            yield return virusScanRequest.SendWebRequest();

                            if (virusScanRequest.result == UnityWebRequest.Result.Success)
                            {
                                // Check if the page contains the virus scan warning message
                                if (virusScanRequest.downloadHandler.text.Contains("<p class=\"uc-warning-caption\">Google Drive can't scan this file for viruses.</p>"))
                                {
                                    BoomboxPlugin.LogInfo("Virus scan warning page detected.");
                                    yield return HandleVirusScanRedirect(redirectUrl, fileName, fileId);
                                }
                                else
                                {
                                    BoomboxPlugin.LogInfo("No virus scan warning detected. Proceeding with direct download.");
                                    yield return DownloadFile(originalUrl, fileName, fileId);
                                }
                            }
                            else
                            {
                                BoomboxPlugin.LogError($"Failed to check virus scan page: {virusScanRequest.error}");
                                yield return DownloadFile(originalUrl, fileName, fileId); // Proceed with the download
                            }
                        }
                    }
                    else
                    {
                        // Proceed with direct download from the redirect URL
                        BoomboxPlugin.LogInfo("Redirect is not a virus scan page. Proceeding with direct download.");
                        yield return DownloadFile(originalUrl, fileName, fileId);
                    }
                }
                else
                {
                    BoomboxPlugin.LogError($"Failed to download file {url}: {request.error}");
                }
            }
        }

        // Handles the virus scan warning page, extracting necessary parameters and constructing the final download URL
        private static IEnumerator HandleVirusScanRedirect(string url, string fileName, string fileId)
        {
            using (UnityWebRequest redirectRequest = UnityWebRequest.Get(url))
            {
                // Send a request to the virus scan warning page to fetch form data
                yield return redirectRequest.SendWebRequest();


                if (redirectRequest.result == UnityWebRequest.Result.Success)
                {
                    string responseHtml = redirectRequest.downloadHandler.text;

                    // Extract the filename from the HTML (if available)
                    string extractedFileName = ExtractFileNameFromHtml(responseHtml);
                    if (!string.IsNullOrEmpty(extractedFileName))
                    {
                        fileName = extractedFileName; // Use the extracted filename
                    }

                    // Extract the necessary parameters from the HTML response
                    string id = ExtractFormField(responseHtml, "name=\"id\" value=\"");
                    string export = ExtractFormField(responseHtml, "name=\"export\" value=\"");
                    string confirm = ExtractFormField(responseHtml, "name=\"confirm\" value=\"");
                    string uuid = ExtractFormField(responseHtml, "name=\"uuid\" value=\"");

                    // If fileId is not extracted from the HTML, use the one passed as a parameter
                    if (string.IsNullOrEmpty(fileId) && !string.IsNullOrEmpty(id))
                    {
                        fileId = id; // Use the id extracted from the form
                    }

                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(export) || string.IsNullOrEmpty(confirm) || string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(fileId))
                    {
                        BoomboxPlugin.LogWarning("Required parameters missing. Retrying with original URL...");
                        yield return DownloadFile(url, fileName, fileId); // Retry with original URL
                        yield break;
                    }

                    // Construct the final URL with the extracted parameters
                    string finalUrl = $"https://drive.usercontent.google.com/download?id={id}&export={export}&confirm={confirm}&uuid={uuid}";

                    BoomboxPlugin.LogInfo($"Constructed final URL: {finalUrl}");

                    // Proceed with downloading the file from the final URL
                    yield return DownloadFile(finalUrl, fileName, fileId);
                }
                else
                {
                    BoomboxPlugin.LogError($"Failed to retrieve virus scan warning page: {redirectRequest.error}");
                }
            }
        }


        // Extract the filename from the HTML content
        private static string ExtractFileNameFromHtml(string html)
        {
            string pattern = @"<span class=""uc-name-size""><a href=""/open\?id=[^""]+"">([^<]+)</a>";
            Match match = Regex.Match(html, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        // Helper function to extract form field values from the HTML response
        private static string ExtractFormField(string html, string fieldName)
        {
            int startIndex = html.IndexOf(fieldName);
            if (startIndex == -1)
                return null;

            startIndex += fieldName.Length;
            int endIndex = html.IndexOf("\"", startIndex);
            if (endIndex == -1)
                return null;

            return html.Substring(startIndex, endIndex - startIndex);
        }

        private static IEnumerator DownloadFile(string url, string fileName, string fileId = null)
        {
            string savePath = Path.Combine(directory, fileName);
            string fullPath = Path.GetFullPath(savePath);

            if (!fullPath.StartsWith(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"Invalid ZIP entry: {fileName}");
            }

            // Download the file
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                // Add headers to mimic a real browser
                request.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                request.timeout = 300;

                BoomboxPlugin.LogInfo($"Downloading {url}...");
                yield return request.SendWebRequest();

                float lastReportedProgress = 0f;  // To track the last reported progress

                while (!request.isDone)
                {
                    // Only log the progress when it's changed by a certain threshold (e.g., every 5%)
                    if (Mathf.Abs(request.downloadProgress - lastReportedProgress) >= 0.05f)
                    {
                        lastReportedProgress = request.downloadProgress;
                        BoomboxPlugin.LogInfo($"Download progress: {request.downloadProgress * 100:0.00}%");
                    }
                    yield return null;
                }

                // Handle errors
                if (request.result != UnityWebRequest.Result.Success)
                {
                    BoomboxPlugin.LogError($"Failed to download {url}: {request.error}");
                    yield break;
                }

                byte[] data = request.downloadHandler.data;

                if (data == null || data.Length == 0)
                {
                    BoomboxPlugin.LogError($"Downloaded data is empty for {url}");
                    yield break;
                }

                // Save the file to disk
                File.WriteAllBytes(savePath, data);
                BoomboxPlugin.LogInfo($"Downloaded and saved {fileName} to {savePath}");

                // Save metadata (using fileId)
                UpdateFileMetadata(fileId);

                // Validate size of the saved file
                FileInfo downloadedFileInfo = new FileInfo(savePath);
                if (Path.GetExtension(fileName).ToLower() == ".zip")
                {
                    ExtractZipAndSaveMetadata(savePath, directory);
                    File.Delete(savePath);
                    BoomboxPlugin.LogInfo($"ZIP file deleted: {savePath}");
                }
            }
        }

        private static void UpdateFileMetadata(string fileId)
        {
            var lines = File.Exists(trackFilePath) ? new List<string>(File.ReadAllLines(trackFilePath)) : new List<string>();

            // Check if the fileId already exists in the metadata
            bool fileExists = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var data = line.Split(',');

                // If this line contains the same fileId, append the new linkId to it
                if (data[0] == fileId)
                {
                    fileExists = true;
                    // Append the new fileId (linkId) to the same line, separated by commas
                    lines[i] = line + "," + fileId;
                    break;
                }
            }

            // If the fileId doesn't exist in the metadata, create a new line for it
            if (!fileExists)
            {
                lines.Add(fileId);
            }

            // Write the updated metadata back to the file
            File.WriteAllLines(trackFilePath, lines);

            BoomboxPlugin.LogInfo($"Metadata updated: {fileId}");
        }

        private static void ExtractZipAndSaveMetadata(string zipPath, string extractTo)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (!string.IsNullOrEmpty(entry.Name))
                        {
                            string destinationPath = Path.Combine(extractTo, entry.FullName);
                            string fullPath = Path.GetFullPath(destinationPath);

                            // Ensure the extracted file is within the target directory
                            if (!fullPath.StartsWith(Path.GetFullPath(extractTo), StringComparison.OrdinalIgnoreCase))
                            {
                                throw new UnauthorizedAccessException($"Invalid ZIP entry: {entry.FullName}");
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                            entry.ExtractToFile(fullPath, true);
                        }
                    }
                }
                BoomboxPlugin.LogInfo($"Extraction complete for {zipPath}");
            }
            catch (Exception ex)
            {
                BoomboxPlugin.LogError($"Error extracting ZIP: {ex.Message}");
            }
        }
    }

    internal static class AudioManager
    {
        static readonly string directory = Path.Combine(Paths.BepInExRootPath, "Custom Songs", "Boombox Music");
        public static event Action OnAllSongsLoaded;
        public static bool FinishedLoading => finishedLoading;

        static string[] allSongPaths;
        private static readonly List<AudioClip> audioClips = new List<AudioClip>();
        static List<AudioClip> clips = audioClips;
        static bool finishedLoading = false;

        public static bool HasNoSongs => allSongPaths.Length == 0;



        public static void Load()
        {
            // Check if this is the first time loading
            if (!firstRun) return;

            firstRun = false;

            BoomboxPlugin.LogInfo("Starting to load audio clips...");

            // Ensure the directory exists
            if (!Directory.Exists(directory))
            {
                BoomboxPlugin.LogError($"Directory {directory} does not exist.");
                return;
            }

            // Get all file paths in the directory
            allSongPaths = Directory.GetFiles(directory);

            if (allSongPaths.Length == 0)
            {
                BoomboxPlugin.LogWarning("No pre-existing songs found!");
                return;
            }

            // Load audio clips
            CheckAndLoadAudioClips();
        }

        private static bool firstRun = true; // Flag to ensure songs are loaded only once

        private static void CheckAndLoadAudioClips()
        {
            // Check if the downloaded files exist
            if (!Directory.Exists(directory))
            {
                BoomboxPlugin.LogError($"Directory {directory} does not exist.");
                return;
            }

            allSongPaths = Directory.GetFiles(directory);
            if (allSongPaths.Length == 0)
            {
                BoomboxPlugin.LogWarning("No pre-existing songs found!");
                return;
            }

            BoomboxPlugin.LogInfo("Preparing to load AudioClips...");

            var coroutines = new List<Coroutine>();
            foreach (var track in allSongPaths)
            {
                var coroutine = SharedCoroutineStarter.StartCoroutine(LoadAudioClip(track));
                coroutines.Add(coroutine);
            }

            SharedCoroutineStarter.StartCoroutine(WaitForAllClips(coroutines));
        }

        private static IEnumerator LoadAudioClip(string filePath)
        {
            BoomboxPlugin.LogInfo($"Loading {filePath}!");

            var audioType = GetAudioType(filePath);
            if (audioType == AudioType.UNKNOWN)
            {
                BoomboxPlugin.LogError($"Failed to load AudioClip from {filePath}\nUnsupported file extension!");
                yield break;
            }

            var loader = UnityWebRequestMultimedia.GetAudioClip(filePath, GetAudioType(filePath));

            if (Configuration.Config.StreamFromDisk) (loader.downloadHandler as DownloadHandlerAudioClip).streamAudio = true;

            loader.SendWebRequest();

            while (!loader.isDone)
            {
                yield return null;
            }

            if (loader.error != null)
            {
                BoomboxPlugin.LogError($"Error loading clip from path: {filePath}\n{loader.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(loader);
            if (clip && clip.loadState == AudioDataLoadState.Loaded)
            {
                BoomboxPlugin.LogInfo($"Successfully loaded: {filePath}");
                clip.name = Path.GetFileName(filePath);
                clips.Add(clip);
            }
            else
            {
                BoomboxPlugin.LogError($"Failed to load clip at: {filePath}");
            }
        }

        private static IEnumerator WaitForAllClips(List<Coroutine> coroutines)
        {
            foreach (var coroutine in coroutines)
            {
                yield return coroutine;
            }

            BoomboxPlugin.LogInfo("Finished loading all clips!");

            clips.Sort((first, second) => first.name.CompareTo(second.name));

            // Log the sorted clip names
            foreach (var clip in clips)
            {
                BoomboxPlugin.LogInfo($"Clip loaded: {clip.name}");
            }

            finishedLoading = true;
            OnAllSongsLoaded?.Invoke();
            OnAllSongsLoaded = null;
        }

        public static void ApplyClips(BoomboxItem __instance)
        {
            BoomboxPlugin.LogInfo($"Applying clips!");

            if (Configuration.Config.UseDefaultSongs)
                __instance.musicAudios = __instance.musicAudios.Concat(clips).ToArray();
            else
                __instance.musicAudios = clips.ToArray();

            BoomboxPlugin.LogInfo($"Total Clip Count: {__instance.musicAudios.Length}");
        }

        private static AudioType GetAudioType(string path)
        {
            var extension = Path.GetExtension(path).ToLower();

            if (extension == ".wav")
                return AudioType.WAV;
            if (extension == ".ogg")
                return AudioType.OGGVORBIS;
            if (extension == ".mp3")
                return AudioType.MPEG;

            BoomboxPlugin.LogError($"Unsupported extension type: {extension}");
            return AudioType.UNKNOWN;
        }
    }
}
