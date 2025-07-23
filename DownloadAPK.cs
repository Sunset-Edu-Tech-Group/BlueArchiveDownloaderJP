using System.Diagnostics;
using PuppeteerSharp;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Text;


namespace BAdownload
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 解析參數：假設程式呼叫方式為
            // BAdownload.exe -f 1.456789
            // 則需要抓取 -f 後的值來組合下載連結。

            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding  = Encoding.UTF8;
            bool reDownload = false;
            bool UnAudioFlag = false;
            string rootDirectory = Directory.GetCurrentDirectory();
            if (!Directory.Exists(Path.Combine(rootDirectory, "Downloads", "XAPK")))
            {
                Directory.CreateDirectory(Path.Combine(rootDirectory, "Downloads", "XAPK"));
            }

            var downloadPath = Path.Combine(rootDirectory, "Downloads", "XAPK");
            if (Directory.Exists(downloadPath))
            {
                var files = Directory.GetFiles(downloadPath);
                foreach (var file in files)
                {
                    if (file.EndsWith(".xapk"))
                    {
                        continue;
                    }
                    else
                    {
                        File.Delete(file);
                    }
                }
            }
            string versionArg = null;
            bool directDownload = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-f", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    versionArg = args[i + 1];
                    i++;
                }
                else if (args[i].Equals("-d", StringComparison.OrdinalIgnoreCase))
                {
                    directDownload = true;
                }
                else if (args[i].Equals("-r", StringComparison.OrdinalIgnoreCase))
                {
                    reDownload = true;
                }
                else if (args[i].Equals("-a", StringComparison.OrdinalIgnoreCase))
                {
                    UnAudioFlag = true;
                }
            }
            if (UnAudioFlag)
            {
                Console.WriteLine("UnAudio flag detected; extracting audio files.");
                await UnAudio.Audio();
                return;
            }
            if (directDownload && !reDownload)
            {
                Console.WriteLine(
                    "Detected download-related flag but no re-download flag; skipping APK re-download because an existing XAPK was found."
                );
            }

            bool xapkExists = false;
            string existingXapkFile = null;
            if (Directory.Exists(downloadPath))
            {
                var xapkFiles = Directory.GetFiles(downloadPath, "*.xapk");
                if (xapkFiles.Length > 0)
                {
                    xapkExists = true;
                    existingXapkFile = xapkFiles[0];
                    Console.WriteLine($"Existing XAPK file found: {Path.GetFileName(existingXapkFile)}");

                    if (!reDownload)
                    {
                        Console.WriteLine("Skipping download; using existing file.");
                        foreach (var dir in new[] { "Unzip", "Processed" })
                        {
                            var path = Path.Combine(rootDirectory, "Downloads", "XAPK", dir);
                            if (Directory.Exists(path))
                                Directory.Delete(path, true);
                        }
                        await UnXAPK.UnXAPKMain(args);
                        return;
                    }
                    else
                    {
                        Console.WriteLine("Detected -r flag; deleting existing XAPK file and re-downloading.");
                        File.Delete(existingXapkFile);
                        //also delete Unzip and Processed folder
                        foreach (var dir in new[] { "Unzip", "Processed" })
                        {
                            var path = Path.Combine(rootDirectory, "Downloads", "XAPK", dir);
                            if (Directory.Exists(path))
                                Directory.Delete(path, true);
                        }
                    }
                }
            }

            // 根據是否有 versionArg 來決定組合的網址
            string downloadUrl;
            if (!string.IsNullOrEmpty(versionArg) && versionArg.StartsWith("1."))
            {
                // 取小數點後的部分。例如 "1.53.323417" 只取 "323417"
                var versionCode = versionArg.Substring(5); // 從 index=5 開始擷取
                downloadUrl = $"https://d.apkpure.com/b/XAPK/com.YostarJP.BlueArchive?versionCode={versionCode}&nc=arm64-v8a&sv=24";
            }
            else
            {
                // 沒有指定 -f 參數，或格式不符合，改用 latest
                downloadUrl = "https://d.apkpure.com/b/XAPK/com.YostarJP.BlueArchive?version=latest";
            }

            Console.WriteLine($"Preparing download URL: {downloadUrl}");

            if (directDownload)
            {
                // 1. 下載並解析 Protobuf → JSON 
                const string pbUrl =
                    "https://api.pureapk.com/m/v3/cms/app_version?hl=en-US&package_name=com.YostarJP.BlueArchive";
                using var http = new HttpClient();
                http.Timeout = Timeout.InfiniteTimeSpan;
                http.DefaultRequestHeaders.Add("x-sv", "29");
                http.DefaultRequestHeaders.Add("x-abis", "arm64-v8a,armeabi-v7a,armeabi");
                http.DefaultRequestHeaders.Add("x-gp", "1");

                Console.WriteLine("Downloading response.pb ...");
                var pbBytes = await http.GetByteArrayAsync(pbUrl);
                // 假設 response.pb 已成功下載並寫入檔案
                await File.WriteAllBytesAsync("response.pb", pbBytes);  // 原始碼已有
                Console.WriteLine("Saved response.pb");

                var protocPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "protoc.exe");
                var psi = new ProcessStartInfo
                {
                    FileName = protocPath,
                    Arguments = "--decode_raw",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Console.WriteLine("Decoding with protoc --decode_raw ...");
                using var proc = Process.Start(psi);
                await proc.StandardInput.BaseStream.WriteAsync(pbBytes, 0, pbBytes.Length);
                proc.StandardInput.Close();
                string textProto = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                await File.WriteAllTextAsync("response.txt", textProto); // 您的原始碼已有

                // 2. 解析到 JObject (ParseMessage 函數您已提供)
                var lines = textProto.Split('\n');
                int idx = 0;
                JObject root = ParseMessage(lines, ref idx, 0); // ParseMessage 是您提供的本地函數
                // await File.WriteAllTextAsync("response.json", root.ToString()); // 您的原始碼已有
                Console.WriteLine("Generated response.json");

                // 3. 從 JSON 中擷取 version_list
                var array = root.SelectToken("$['1']['7']['2']") as JArray;

                if (array == null)
                {
                    Console.Error.WriteLine("Failed to parse version_list from response.json (path $['1']['7']['2'] is invalid or does not exist).");
                    return;
                }

                // 解析所有版本資訊，僅以 5 欄位（versionCode）為主
                var entries = array
                    .Select(item =>
                    {
                        var itemObj = item as JObject;
                        if (itemObj == null) return null;
                        var threeToken = itemObj["3"] as JObject;
                        if (threeToken == null) return null;
                        var info = threeToken["2"] as JObject;
                        if (info == null) return null;
                        var versionCodeToken = info["5"];
                        var urlToken = info?["24"]?["9"];
                        if (versionCodeToken == null || urlToken == null) return null;
                        if (urlToken.Type != JTokenType.String) return null;
                        string versionCode = versionCodeToken.Value<string>();
                        // 直接用 versionCode 組合版本號：1.XX.versionCode
                        string minor = versionCode.Length >= 6 ? versionCode.Substring(1, 2) : "00";
                        string versionStr = $"1.{minor}.{versionCode}";
                        string url = urlToken.Value<string>();
                        return new { Version = versionStr, VersionCode = versionCode, Url = url };
                    })
                    .Where(x => x != null && !string.IsNullOrEmpty(x.Url) && x.Url.StartsWith("https://download.pureapk.com/b/XAPK/"))
                    .ToList();

                if (!entries.Any())
                {
                    Console.Error.WriteLine("No valid XAPK download links or version information found in response.json.");
                    return;
                }

                string selectedUrl = null;
                string selectedVersion = null;

                if (!string.IsNullOrEmpty(versionArg)) // -f 1.XX.XXXXXX 只比對 XXXXXX
                {
                    var versionCode = versionArg.Split('.').Last();
                    var match = entries.FirstOrDefault(x => x.VersionCode == versionCode);
                    if (match == null)
                    {
                        Console.Error.WriteLine($"The specified versionCode {versionCode} was not found in the list or its URL is not in a valid XAPK format.");
                        return;
                    }
                    selectedUrl = match.Url;
                    selectedVersion = match.Version;
                    Console.WriteLine($"Selected specified version {selectedVersion}");
                }
                else
                {
                    // 只比對 versionCode 最大的
                    var latestEntry = entries.OrderByDescending(e => long.Parse(e.VersionCode)).FirstOrDefault();
                    if (latestEntry == null)
                    {
                        Console.Error.WriteLine("Unable to determine the latest version from the available list.");
                        return;
                    }
                    selectedUrl = latestEntry.Url;
                    selectedVersion = latestEntry.Version;
                    Console.WriteLine($"Selected latest version {selectedVersion}");
                }
                JObject ParseMessage(string[] lines, ref int idx, int indent)
                {
                    var obj = new JObject();
                    var fieldRe = new Regex(@"^\s*(\d+):\s*(?:""([^""\\]*)""|(\d+))");
                    while (idx < lines.Length)
                    {
                        var line = lines[idx];
                        int curIndent = line.TakeWhile(c => c == ' ').Count();
                        if (curIndent < indent) break;
                        if (string.IsNullOrWhiteSpace(line) || line.Trim() == "}") { idx++; continue; }
                        if (line.TrimEnd().EndsWith("{"))
                        {
                            var numMatch = Regex.Match(line, @"^\s*(\d+)\s*\{");
                            if (!numMatch.Success) { idx++; continue; }
                            var key = numMatch.Groups[1].Value; idx++;
                            var child = ParseMessage(lines, ref idx, curIndent + 2);
                            if (obj.TryGetValue(key, out var exist))
                            {
                                if (exist is JArray arr) arr.Add(child);
                                else obj[key] = new JArray(exist, child);
                            }
                            else obj[key] = child;
                        }
                        else
                        {
                            var m = fieldRe.Match(line);
                            if (m.Success)
                            {
                                var key = m.Groups[1].Value;
                                var valText = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                                JToken val = valText;
                                if (obj.TryGetValue(key, out var exist))
                                {
                                    if (exist is JArray ja) ja.Add(val);
                                    else obj[key] = new JArray(exist, val);
                                }
                                else obj[key] = val;
                            }
                            idx++;
                        }
                    }
                    return obj;
                }

                // 4. 下載 XAPK
                if (selectedUrl != null)
                {
                    Console.WriteLine($"Preparing to download XAPK from the following URL: {selectedUrl}");
                    Console.WriteLine("Downloading XAPK ...");
                    var xapkBytes = await http.GetByteArrayAsync(selectedUrl);
                    string filePath = Path.Combine(downloadPath, "BlueArchive.XAPK"); // downloadPath 是您程式碼中定義的下載路徑
                    await File.WriteAllBytesAsync(filePath, xapkBytes);
                    Console.WriteLine($"XAPK has been saved to: {filePath}");
                }
                else
                {
                    Console.Error.WriteLine("Failed to select a valid download URL.");
                }
                await UnXAPK.UnXAPKMain(args); 
                return;
            }    

            // 下載/更新 Chromium
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync(); // 不加任何參數

            // 啟動瀏覽器（Headless = false 方便除錯觀察；若要隱藏視窗可以設為 true）
            var launchOptions = new LaunchOptions
            {
                Headless = false,  // 顯示瀏覽器視窗
                DefaultViewport = null  // 不限制視窗大小
            };

            using var browser = await Puppeteer.LaunchAsync(launchOptions);
            var page = await browser.NewPageAsync();

            // 設定下載行為：將檔案下載到程式執行目錄 (或指定其他資料夾)
            var client = await page.Target.CreateCDPSessionAsync();

            var parameters = new Dictionary<string, object>
            {
                ["behavior"] = "allow",
                ["downloadPath"] = Path.Combine(rootDirectory, "Downloads", "XAPK")
            };

            await client.SendAsync("Page.setDownloadBehavior", parameters);

            // 導向至下載頁面，等待網頁載入完成
            // 由於 Cloudflare 可能有「五秒盾」或 JS Challenge，可以用 NetworkIdle2/NetworkIdle0 盡量等待網頁完成
            try
            {
                Console.WriteLine("Attempting to load the page and awaiting Cloudflare verification...");
                await page.GoToAsync(downloadUrl, WaitUntilNavigation.Networkidle2);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while loading the page: {ex.Message}");
            }


            await Task.Delay(5000); // 等待 5 秒看是否需要額外 Cloudflare 檢查
            Console.WriteLine("Waiting for file download to complete...");

            string downloadedFile = await WaitForDownloadedFileAsync(downloadPath, TimeSpan.FromSeconds(600));
            if (downloadedFile != null)
            {
                Console.WriteLine($"Download complete: {downloadedFile}");
            }
            else
            {
                Console.WriteLine("Download timed out or no file detected.");
            }

            Console.WriteLine("Download process complete; closing browser.");
            await browser.CloseAsync();
            await UnXAPK.UnXAPKMain(args);
        }

        public static async Task<string> WaitForDownloadedFileAsync(string downloadDir, TimeSpan timeout)
        {
            var watch = Stopwatch.StartNew();
            var initialFiles = Directory.GetFiles(downloadDir).ToHashSet();

            while (watch.Elapsed < timeout)
            {
                var currentFiles = Directory.GetFiles(downloadDir).ToHashSet();
                var newFiles = currentFiles.Except(initialFiles).ToList();
                if (newFiles.Count > 0)
                {
                    var newFile = newFiles[0];
                    bool isFileReady = false;
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            using (var fileStream = File.Open(newFiles[0], FileMode.Open, FileAccess.Read, FileShare.None))
                            {
                                isFileReady = fileStream.Length > 0;
                                break;
                            }
                        }
                        catch (Exception)
                        {
                            await Task.Delay(1000);
                        }
                        if (isFileReady) break;
                    }
                    return newFile;
                }
                await Task.Delay(500);

            }
            return null;
        }

    }
}
