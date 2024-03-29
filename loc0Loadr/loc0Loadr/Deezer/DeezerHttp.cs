﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ByteSizeLib;
using Konsole;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace loc0Loadr.Deezer
{
    internal class DeezerHttp : IDisposable
    {
        private readonly HttpClient _httpClient;
        private string _apiToken;
        
        private const string ApiUrl = "https://www.deezer.com/ajax/gw-light.php";

        public DeezerHttp(string arl)
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer()
            };

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("user-agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.132 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("cookie", $"arl={arl}");
        }

        public async Task<bool> GetApiToken()
        {
            Console.WriteLine("Grabbing API token...");
            using (FormUrlEncodedContent formContent = DeezerHelpers.BuildDeezerApiContent("", "deezer.getUserData"))
            {
                using (HttpResponseMessage apiRequest = await _httpClient.PostAsync(ApiUrl, formContent))
                {
                    if (!apiRequest.IsSuccessStatusCode)
                    {
                        Helpers.RedMessage("Failed to contact initial API");
                        return false;
                    }

                    string apiRequestBody = await apiRequest.Content.ReadAsStringAsync();

                    JObject apiRequestJson = JObject.Parse(apiRequestBody);

                    if (apiRequestJson == null)
                    {
                        Helpers.RedMessage("Failed to parse API token request JSON");
                        return false;
                    }

                    apiRequestJson.DisplayDeezerErrors("API Token");

                    if (apiRequestJson["results"]?["USER"]?["USER_ID"].Value<int>() == 0)
                    {
                        Helpers.RedMessage("Invalid credentials");
                        return false;
                    }

                    if (apiRequestJson["results"]?["checkForm"] != null)
                    {
                        _apiToken = apiRequestJson["results"]["checkForm"].Value<string>();
                        return true;
                    }

                    Helpers.RedMessage("Unable to get checkform");
                    return false;
                }
            }
        }

        public async Task<JObject> HitUnofficialApi(string method, JObject data, int retries = 3)
        {
            string queryString = await DeezerHelpers.BuildDeezerApiQueryString(_apiToken, method);
            string url = $"{ApiUrl}?{queryString}";

            string bodyData = JsonConvert.SerializeObject(data);

            var body = new StringContent(bodyData, Encoding.UTF8, "application/json");

            var attempts = 1;

            while (attempts <= retries)
            {
                try
                {
                    using (HttpResponseMessage apiResponse = await _httpClient.PostAsync(url, body))
                    {
                        if (apiResponse.IsSuccessStatusCode)
                        {
                            string bodyContent = await apiResponse.Content.ReadAsStringAsync();

                            if (!string.IsNullOrWhiteSpace(bodyContent))
                            {
                                try
                                {
                                    return JObject.Parse(bodyContent);
                                }
                                catch (JsonReaderException ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }
                            }
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine(ex.Message);
                }
                
                attempts++;
                Helpers.RedMessage("Request failed, waiting 5s...");
                await Task.Delay(5000);
            }

            return null;
        }

        public async Task<JObject> HitOfficialApi(string path, string id, int retires = 3)
        {
            var attempts = 1;

            while (attempts <= retires)
            {
                try
                {
                    using (HttpResponseMessage albumResponse =
                        await _httpClient.GetAsync($"https://api.deezer.com/{path}/{id}"))
                    {
                        if (albumResponse.IsSuccessStatusCode)
                        {
                            string albumResponseContent = await albumResponse.Content.ReadAsStringAsync();

                            if (!string.IsNullOrWhiteSpace(albumResponseContent))
                            {
                                try
                                {
                                    return JObject.Parse(albumResponseContent);
                                }
                                catch (JsonReaderException ex)
                                {
                                    Console.WriteLine(ex.Message);
                                }
                            }
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine(ex.Message);
                }

                attempts++;
                Helpers.RedMessage("Request failed, waiting 5s...");
                await Task.Delay(5000);
            }

            return null;
        }

        public async Task<byte[]> DownloadTrack(string url, IProgressBar trackProgress, string title, int retries = 3)
        {
            var attempts = 1;

            while (attempts <= retries)
            {
                try
                {
                    using (HttpResponseMessage downloadResponse = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (downloadResponse.IsSuccessStatusCode && downloadResponse.Content.Headers.ContentLength.HasValue)
                        {
                            return await DownloadWithProgress(downloadResponse, trackProgress, title);
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine(ex.Message);
                }

                attempts++;
                trackProgress.Next("Request failed, waiting 5s...");
                await Task.Delay(5000);
            }

            return null;
        }

        private async Task<byte[]> DownloadWithProgress(HttpResponseMessage response, IProgressBar trackProgress, string title)
        {
            // thanks stackoverflow
            using (Stream fileStream = await response.Content.ReadAsStreamAsync())
            {
                // ReSharper disable once PossibleInvalidOperationException
                long total = response.Content.Headers.ContentLength.Value;
                double totalMegabytes = ByteSize.FromBytes(total).MegaBytes;
                totalMegabytes = Math.Round(totalMegabytes, 2);
                
                var finalBytes = new byte[total];
                var totalRead = 0L;
                var buffer = new byte[4096];
                var isMoreToRead = true;
                
                do
                {
                    int read = await fileStream.ReadAsync(buffer, 0, buffer.Length);

                    if (read == 0)
                    {
                        trackProgress.Next($"{title} | Download Complete");
                        isMoreToRead = false;
                    }
                    else
                    {
                        var data = new byte[read];
                        buffer.ToList().CopyTo(0, data, 0, read);
                        data.CopyTo(finalBytes, totalRead);
                        
                        totalRead += read;

                        double percent = totalRead * 1d / (total * 1d) * 100;

                        double totalReadMegabytes = ByteSize.FromBytes(totalRead).MegaBytes;
                        totalReadMegabytes = Math.Round(totalReadMegabytes, 2);
                        
                        trackProgress.Refresh(Convert.ToInt32(percent), $"{title} | {totalReadMegabytes}MB/{totalMegabytes}MB");
                    }
                                    
                } while (isMoreToRead);

                return finalBytes;
            }
        }

        public async Task<byte[]> GetAlbumArt(string albumPictureId, int retries = 3)
        {
            string url = $"https://e-cdns-images.dzcdn.net/images/cover/{albumPictureId}/1400x1400-000000-94-0-0.jpg";

            var attempts = 1;

            while (attempts <= retries)
            {
                try
                {
                    using (HttpResponseMessage albumCoverResponse = await _httpClient.GetAsync(url))
                    {
                        if (albumCoverResponse.IsSuccessStatusCode)
                        {
                            return await albumCoverResponse.Content.ReadAsByteArrayAsync();
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine(ex.Message);
                }

                attempts++;
                Helpers.RedMessage("Request failed, waiting 5s...");
                await Task.Delay(5000);
            }
            
            return new byte[0];
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}