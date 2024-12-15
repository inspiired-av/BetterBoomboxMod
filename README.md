# BetterBoomboxMod
Allows players to add to or replace boombox tracks in Lethal Company. Adds additional support for uploading tracks via [direct Google Drive links](https://sites.google.com/site/gdocs2direct/) in the config file. Automatically extracts .zip files if necessary. Original mod by [DeadlyKitten](https://github.com/DeadlyKitten/LC-Boombox/)

## Installing Songs when Installed via r2modman

When mods are installed with r2modman, BepInEx gets configured to place mod files in a different location

You can find the correct folder by going to `Settings`, clicking `Browse profile folder`, then navigating to `BepInEx\Custom Songs\Boombox Music`.

![image](https://github.com/DeadlyKitten/LC-Boombox/assets/9684760/ef378cdc-c2af-4ba4-82ef-d2aa29a9af31)

## Manual Installation
Place the latest release into the `BepInEx/plugins` folder. Run the game once to generate content folders.

![Screenshot 2024-12-14 163912](https://github.com/user-attachments/assets/9334ed9f-116d-40ae-a090-1cddfe05cc9d)

Update the boombox.cfg config file located in `BepInEx\config\` and paste the [direct download](https://sites.google.com/site/gdocs2direct/) URLs in the `Song Download URLs =` field. If you have multiple links, seperate them by commas.

**URL Format should look like this:** 

`https://drive.google.com/uc?id=12345&export=download, https://drive.google.com/uc?id=67890&export=download`

![Screenshot 2024-12-14 164112](https://github.com/user-attachments/assets/238d4a4f-ae56-4fe0-8e10-c68ef50833b1)

Make sure that Link Access is set to `Anyone with the link` if uploading from Google Drive

![Screenshot 2024-12-14 175620](https://github.com/user-attachments/assets/8f047ff2-3148-4872-9bf5-a4f3bce9e1fd)

-----
 
### Valid file types are as follows:
- WAV
- OGG
- MP3
- ZIP (Automatic extraction of contents)

## 🔧 Developing

Clone the project, then create a file in the root of the project directory named:

`CustomBoomboxTracks.csproj.user`

Here you need to set the `GameDir` property to match your install directory.

Example:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <!-- Set "YOUR OWN" game folder here to resolve most of the dependency paths! -->
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Lethal Company</GameDir>
  </PropertyGroup>
</Project>
```

Now when you build the mod, it should resolve your references automatically, and the build event will copy the plugin into your `BepInEx\plugins` folder!
