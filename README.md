<h1 align="center">THIS PLUGIN IN NOW DEPRECATED</h1>
You probably noticed I haven't been updating this plugin since Jellyfin 10.8, sorry about that (10.9 is right around the corner wow!). I started the project as something to make tracking watched anime easier for me, but as my final year in university loomed closer, I started running out of time to keep up with maintaining the plugin... I think I should officially state that this plugin in deprecated and will go unmaintained for future versions of Jellyfin. Someday I may come back and pick up the project again, but at present, I don't see it happening in the near future.
As a replacement for this functionality this plugin brings, I would recommend the <a href="https://github.com/Fallenbagel/AnilistSync">fork</a> by <a href="https://github.com/Fallenbagel">FallenBagel</a> porting this plugin to Jellyfin 10.8 or <a href="https://github.com/vosmiic/jellyfin-ani-sync">jellyfin-ani-sync</a> by <a href="https://github.com/vosmiic">vosmiic</a> for extended features and on-paper much more accurate JF -> Anilist mapping (I use it, and its good).
Thank you to everyone that found use and enjoyed this plugin! This was my first time doing something like this and I wasn't expecting people to actually use it! Maybe we'll meet again sometime...

<h1 align="center">AnilistSync</h1>
<h3 align="center">Big thanks to Crobibero for writing the <a href="https://github.com/crobibero/jellyfin-plugin-simkl">Simkl Plugin</a></h3>

A Jellyfin plugin that scrobbles your anime watches to Anilist for easy automatic list updates.

## Features
- Multi-user support
- Scrobbling of anime shows/movies to Anilist
- Rewatch support


## Requirements
- [Anilist Plugin](https://github.com/jellyfin/jellyfin-plugin-anilist): This plugin relies entirely on the Anilist plugin obtaining the correct ID! If its wrong or not present, scrobbling with fail

## Install
### Repository
1. Add the repository URL to Jellyfin: `https://raw.githubusercontent.com/ARufenach/AnilistSync/master/manifest.json`
2. Install AnilistSync from the plugin catalog
### Github Release
1. Download the `.zip` release from [here](https://github.com/ARufenach/AnilistSync/releases/latest)
2. Extract the contents into a folder in the `plugins` folder of your Jellyfin install directory
