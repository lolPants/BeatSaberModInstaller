using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Linq;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using BeatSaberModManager.DataModels;
using BeatSaberModManager.Dependencies.SimpleJSON;
namespace BeatSaberModManager.Core
{
    public class RemoteLogic
    {
#if DEBUG
        private const string ModSaberURL = "https://staging.modsaber.org";
#else
        private const string ModSaberURL = "https://www.modsaber.org";
#endif

        private const string ApiVersion = "1.0";
        private readonly string ApiURL = $"{ModSaberURL}/api/v{ApiVersion}";

        private string currentGameVersion = string.Empty;
        public List<ReleaseInfo> releases;
        public RemoteLogic()
        {
            releases = new List<ReleaseInfo>();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        public void GetCurrentGameVersion()
        {
            string raw = Fetch($"{ApiURL}/site/gameversions");
            var decoded = JSON.Parse(raw);
            var current = decoded[0];
            var value = current["value"];
            currentGameVersion = value;
        }

        public void PopulateReleases()
        {
            const string TournamentCategory = "Tournament";
            string[] AllowedMods = new string[] { "song-loader", "scoresaber", "beatsaverdownloader" };

            string raw = GetModSaberReleases();
            if (raw != null)
            {
                var mods = JSON.Parse(raw);
                for (int i = 0; i < mods.Count; i++)
                {
                    var current = mods[i];
                    if (!AllowedMods.Contains((string)current["name"]))
                        continue;

                    List<ModLink> dependsOn = NodeToLinks(current["dependsOn"]);
                    List<ModLink> conflictsWith = NodeToLinks(current["conflictsWith"]);

                    // Tournament Edition Replacements
                    if (current["name"] == "scoresaber")
                    {
                        current["title"] = "Score Saber Tournament Edition";
                        current["category"] = TournamentCategory;

                        string v = current["version"];
                        current["version"] = $"{v}-te";

                        string steamURL = current["files"][0]["url"];
                        string oculusURL = current["files"][1]["url"];

                        string regex = @"(https:\/\/(?:staging|www)\.modsaber\.org)\/download\/(steam|oculus)\/([a-z]+)\/(?:[0-9.]+)";
                        string teVersion = "te";
                        string replacement = $"$1/cdn/tournament/$3-{teVersion}-$2.zip";

                        current["files"][0]["url"] = Regex.Replace(steamURL, regex, replacement);
                        current["files"][1]["url"] = Regex.Replace(oculusURL, regex, replacement);
                    }

                    var files = current["files"];
                    if (files.Count > 1)
                    {
                        var steam = files[0];
                        var oculus = files[1];

                        CreateRelease(
                            new ReleaseInfo(current["name"], current["title"], current["version"], current["author"],
                            current["description"], current["weight"], current["gameVersion"],
                            steam["url"], current["category"], Platform.Steam, dependsOn, conflictsWith));

                        CreateRelease(
                            new ReleaseInfo(current["name"], current["title"], current["version"], current["author"],
                            current["description"], current["weight"], current["gameVersion"],
                            oculus["url"], current["category"], Platform.Oculus, dependsOn, conflictsWith));
                    }
                    else
                    {
                        CreateRelease(
                            new ReleaseInfo(current["name"], current["title"], current["version"], current["author"],
                            current["description"], current["weight"], current["gameVersion"],
                            files["steam"]["url"], current["category"], Platform.Default, dependsOn, conflictsWith));
                    }

                    // Maps
                    if (current["name"] == "beatsaverdownloader")
                    {
                        CreateRelease(
                            new ReleaseInfo("tournament-playlist", "Tournament Maps Playlist", "1.0.0-te", "various-mappers",
                            "", 998, current["gameVersion"], $"{ModSaberURL}/cdn/tournament/maps.zip",
                            TournamentCategory, Platform.Default, new List<ModLink>(), new List<ModLink>()));
                    }
                }
            }
        }

        private string[] AsArray(JSONArray arrayJson)
        {
            string[] array = new string[arrayJson.Count];
            int index = 0;
            foreach (JSONNode node in arrayJson)
            {
                array[index] = (string)node.ToString().Trim('"');
                index += 1;
            }
            return array;
        }

        private List<ModLink> NodeToLinks(JSONNode node)
        {
            string[] arr = AsArray(node.AsArray);
            List<ModLink> links = new List<ModLink>();

            foreach (string str in arr)
            {
                string[] split = str.Split('@');
                ModLink link = new ModLink(split[0], split[1]);
                links.Add(link);
            }

            return links;
        }

        private string Fetch(string URL) => Helper.Get(URL);

        private string GetModSaberReleases()
        {
            string raw = Fetch($"{ApiURL}/mods/approved/latest");
            var decoded = JSON.Parse(raw);
            int lastPage = decoded["lastPage"];

            JSONArray final = new JSONArray();

            for (int i = 0; i <= lastPage; i++)
            {
                string page = Fetch($"{ApiURL}/mods/approved/latest/{i}");
                var pageDecoded = JSON.Parse(page);
                var mods = pageDecoded["mods"];

                foreach (var x in mods)
                    final.Add(x.Value);
            }
            return final.ToString();
        }

        private void CreateRelease(ReleaseInfo release)
        {
            if (release.gameVersion == currentGameVersion)
                releases.Add(release);
        }
    }
}
