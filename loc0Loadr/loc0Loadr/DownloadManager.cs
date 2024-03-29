﻿using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using loc0Loadr.Deezer;
using loc0Loadr.Enums;
using loc0Loadr.Models;

namespace loc0Loadr
{
    internal class DownloadManager
    {
        private DeezerHttp _deezerHttp;

        public async Task Run()
        {
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;
            if (string.IsNullOrWhiteSpace(Configuration.GetValue<string>("arl")))
            {
                Helpers.RedMessage("ARL is missing");

                string arl = Helpers.TakeInput("Enter ARL: ");

                if (!Configuration.UpdateConfig("arl", arl))
                {
                    Helpers.RedMessage("Failed to update config");
                    Environment.Exit(1);
                }
            }

            _deezerHttp = new DeezerHttp(Configuration.GetValue<string>("arl"));

            if (!await _deezerHttp.GetApiToken())
            {
                Helpers.RedMessage("Failed to get API token");
                Environment.Exit(1);
            }
            
            Helpers.GreenMessage("Success");
            
            while (true)
            {
                string choice = Helpers.TakeInput(1, 3, "Download via URL", "Search for media", "Exit");

                if (choice == "3")
                {
                    _deezerHttp.Dispose();
                    Environment.Exit(0);
                }
                
                string qualityChoice = Helpers.TakeInput(1, 4, "MP3 128", "MP3 256", "MP3 320", "FLAC");

                AudioQuality quality = DeezerHelpers.InputToAudioQuality[qualityChoice];

                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (choice == "1")
                {
                    await DownloadFromUrl(quality);
                }
                else if (choice == "2")
                {
                    await DownloadFromSearch(quality);
                }
            }
        }

        private async Task DownloadFromUrl(AudioQuality audioQuality)
        {
            string url = Helpers.TakeInput("Enter URL: ");

            string[] urlMatches = Regex.Split(url, @"https?:\/\/www\.deezer\..+\/(\w+)\/(\d+)"); // ty smloadr for the regex

            if (urlMatches.Length < 3)
            {
                Helpers.RedMessage("Invalid URL");
                return;
            }

            string type = urlMatches[1];
            string id = urlMatches[2];
            
            var deezerDownloader = new DeezerDownloader(_deezerHttp, audioQuality);

            switch (type)
            {
                case "track":
                    await deezerDownloader.ProcessTrack(id);
                    Console.Write("\n");
                    break;
                case "artist":
                    await deezerDownloader.ProcessArtist(id);
                    break;
                case "playlist":
                    await deezerDownloader.ProcessPlaylist(id);
                    break;
                case "album":
                    await deezerDownloader.ProcessAlbum(id);
                    break;
                default:
                    Helpers.RedMessage($"{type} is an unsupported type");
                    break;
            }
        }

        private async Task DownloadFromSearch(AudioQuality quality)
        {
            var deezerDownloader = new DeezerDownloader(_deezerHttp, quality);
            var deezerSearcher = new DeezerSearcher(_deezerHttp);
            
            string searchType = Helpers.TakeInput(1, 4, "Track", "Album", "Artist", "Playlist");
            string searchParam = Helpers.TakeInput("Enter search term: ");
            
            switch (searchType)
            {
                case "1":
                    SearchResult trackResult = await deezerSearcher.Search(searchParam, SearchType.Track);

                    if (trackResult != null)
                    {
                        var e = await deezerDownloader.ProcessTrack(trackResult.Id, trackResult.Json);
                    }
                    break;
                case "2":
                    SearchResult albumId = await deezerSearcher.Search(searchParam, SearchType.Album);
                    var i = await deezerDownloader.ProcessAlbum(albumId.Id);
                    break;
                case "3":
                    SearchResult artistId = await deezerSearcher.Search(searchParam, SearchType.Artist);
                    break;
                case "4":
                    SearchResult playlistId = await deezerSearcher.Search(searchParam, SearchType.Playlist);
                    break;
            }
        }

        private void ConsoleOnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            _deezerHttp?.Dispose();
        }
    }
}