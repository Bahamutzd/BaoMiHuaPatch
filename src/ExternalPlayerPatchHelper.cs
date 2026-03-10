using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BaoMiHuaPatch
{
    internal static class ExternalPlayerPatch
    {
        internal static void Trace(string message)
        {
            WriteTrace(message);
        }

        internal static bool HasConfiguredExternalPlayer()
        {
            return !string.IsNullOrEmpty(GetResolvedPlayerPath());
        }

        internal static bool TryPlay(object media)
        {
            WriteTrace("TryPlay enter");
            string playerPath = GetResolvedPlayerPath();
            if (string.IsNullOrEmpty(playerPath) || media == null)
            {
                WriteTrace("TryPlay aborted: player path empty or media null");
                return false;
            }

            List<string> extraHeaders = new List<string>();
            string mediaSource = GetMediaSource(media, extraHeaders);
            if (string.IsNullOrWhiteSpace(mediaSource))
            {
                WriteTrace("TryPlay aborted: media source empty");
                return false;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(playerPath);
                startInfo.UseShellExecute = true;
                startInfo.Arguments = BuildPlayerArguments(playerPath, media, mediaSource, extraHeaders);
                WriteTrace("TryPlay launching: " + playerPath);
                WriteTrace("TryPlay arguments: " + startInfo.Arguments);
                Process process = Process.Start(startInfo);
                if (process == null)
                {
                    WriteTrace("TryPlay launch failed: Process.Start returned null");
                    return false;
                }

                WriteTrace("TryPlay launch succeeded");
                TryBeginPlaybackProgressSync(playerPath, process, media, mediaSource);
                return true;
            }
            catch (Exception ex)
            {
                WriteTrace("TryPlay launch failed: " + ex);
                return false;
            }
        }

        private static string GetMediaSource(object media, List<string> extraHeaders)
        {
            PropertyInfo property = media.GetType().GetProperty(
                "Source",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = property != null ? property.GetValue(media, null) : null;
            if (value == null)
            {
                return null;
            }

            Uri uri = value as Uri;
            if (uri != null)
            {
                return ResolveMediaSource(NormalizeMediaSource(uri), media, extraHeaders);
            }

            string text = value.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            Uri parsedUri;
            if (Uri.TryCreate(text, UriKind.Absolute, out parsedUri))
            {
                return ResolveMediaSource(NormalizeMediaSource(parsedUri), media, extraHeaders);
            }

            return ResolveMediaSource(text, media, extraHeaders);
        }

        private static string ResolveMediaSource(string mediaSource, object media, List<string> extraHeaders)
        {
            if (string.IsNullOrWhiteSpace(mediaSource))
            {
                return null;
            }

            string resolvedSource = mediaSource.Trim();
            string strmResolvedSource = TryResolveStrmSource(resolvedSource, media, extraHeaders);
            if (!string.IsNullOrWhiteSpace(strmResolvedSource))
            {
                resolvedSource = strmResolvedSource;
            }

            return NormalizeExternalPlayerSource(resolvedSource, extraHeaders);
        }

        private static string BuildPlayerArguments(
            string playerPath,
            object media,
            string mediaSource,
            List<string> extraHeaders)
        {
            List<string> arguments = new List<string>();
            arguments.Add(QuoteArgument(mediaSource));

            if (IsPotPlayer(playerPath))
            {
                AppendPotPlayerStartPosition(arguments, media);
                AppendPotPlayerHttpOptions(arguments, media, extraHeaders);
            }

            return string.Join(" ", arguments);
        }

        private static bool IsPotPlayer(string playerPath)
        {
            if (string.IsNullOrWhiteSpace(playerPath))
            {
                return false;
            }

            string fileName = Path.GetFileName(playerPath);
            return !string.IsNullOrWhiteSpace(fileName) &&
                fileName.IndexOf("PotPlayer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AppendPotPlayerHttpOptions(List<string> arguments, object media, List<string> extraHeaders)
        {
            if (arguments == null || media == null)
            {
                return;
            }

            string userAgent = null;
            string referer = null;
            List<string> headers = new List<string>();

            foreach (string option in GetMediaVlcOptions(media))
            {
                string value = GetVlcOptionValue(option, ":http-user-agent=");
                if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(userAgent))
                {
                    userAgent = value;
                }

                value = GetVlcOptionValue(option, ":http-referrer=");
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = GetVlcOptionValue(option, ":http-referer=");
                }

                if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(referer))
                {
                    referer = value;
                }

                value = GetVlcOptionValue(option, ":http-header=");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    headers.Add(value);
                }
            }

            if (extraHeaders != null)
            {
                foreach (string header in extraHeaders)
                {
                    AddHeaderIfMissing(headers, header);
                }
            }

            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                arguments.Add("/user_agent=" + QuoteArgument(userAgent));
            }

            if (!string.IsNullOrWhiteSpace(referer))
            {
                arguments.Add("/referer=" + QuoteArgument(referer));
            }

            if (headers.Count > 0)
            {
                arguments.Add("/headers=" + QuoteArgument(string.Join("\r\n", headers)));
            }
        }

        private static void AppendPotPlayerStartPosition(List<string> arguments, object media)
        {
            if (arguments == null || media == null)
            {
                return;
            }

            string seekArgument = BuildPotPlayerSeekArgument(media);
            if (string.IsNullOrWhiteSpace(seekArgument))
            {
                return;
            }

            arguments.Add(seekArgument);
        }

        private static string BuildPotPlayerSeekArgument(object media)
        {
            TimeSpan position = GetMediaSeekPosition(media);
            if (position <= TimeSpan.Zero)
            {
                return null;
            }

            int totalHours = (int)Math.Floor(position.TotalHours);
            return string.Format(
                "/seek={0:00}:{1:00}:{2:00}.{3:000}",
                totalHours,
                position.Minutes,
                position.Seconds,
                position.Milliseconds);
        }

        private static TimeSpan GetMediaSeekPosition(object media)
        {
            object rawSeekPosition = GetValue(media, "SeekToPosition");
            if (rawSeekPosition is TimeSpan)
            {
                TimeSpan seekPosition = (TimeSpan)rawSeekPosition;
                if (seekPosition > TimeSpan.Zero)
                {
                    return seekPosition;
                }
            }

            object playRelatedInfo = GetPlayRelatedInfo(media);
            object baseFileInfo = GetValue(playRelatedInfo, "Base_File_Info");
            uint watchedDuration = ConvertToUInt32(GetValue(baseFileInfo, "watched_duration"));
            return watchedDuration > 0 ? TimeSpan.FromSeconds(watchedDuration) : TimeSpan.Zero;
        }

        private static void AddHeaderIfMissing(List<string> headers, string header)
        {
            if (headers == null || string.IsNullOrWhiteSpace(header))
            {
                return;
            }

            foreach (string existing in headers)
            {
                if (string.Equals(existing, header, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            headers.Add(header);
        }

        private static IEnumerable<string> GetMediaVlcOptions(object media)
        {
            FieldInfo field = media.GetType().GetField(
                "_vlcOptions",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = field != null ? field.GetValue(media) : null;
            IEnumerable options = value as IEnumerable;
            if (options == null)
            {
                return Array.Empty<string>();
            }

            List<string> results = new List<string>();
            foreach (object item in options)
            {
                string text = item as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    results.Add(text);
                }
            }

            return results;
        }

        private static string GetVlcOptionValue(string option, string prefix)
        {
            if (string.IsNullOrWhiteSpace(option) || string.IsNullOrWhiteSpace(prefix))
            {
                return null;
            }

            if (!option.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string value = option.Substring(prefix.Length).Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                value = value.Substring(1, value.Length - 2);
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string NormalizeMediaSource(Uri uri)
        {
            if (uri == null)
            {
                return null;
            }

            if (uri.IsFile)
            {
                return uri.LocalPath;
            }

            if (uri.IsAbsoluteUri)
            {
                return uri.AbsoluteUri;
            }

            return uri.ToString();
        }

        private static string NormalizeExternalPlayerSource(string mediaSource, List<string> extraHeaders)
        {
            if (string.IsNullOrWhiteSpace(mediaSource))
            {
                return null;
            }

            Uri uri;
            if (!Uri.TryCreate(mediaSource, UriKind.Absolute, out uri))
            {
                return mediaSource;
            }

            if (uri.IsFile)
            {
                return uri.LocalPath;
            }

            AppendAuthorizationHeaderFromUri(uri, extraHeaders);
            return StripUserInfo(uri);
        }

        private static string TryResolveStrmSource(string mediaSource, object media, List<string> extraHeaders)
        {
            if (!IsStrmSource(mediaSource))
            {
                return null;
            }

            WriteTrace("TryResolveStrmSource enter: " + mediaSource);

            try
            {
                Uri sourceUri;
                if (Uri.TryCreate(mediaSource, UriKind.Absolute, out sourceUri) && !sourceUri.IsFile)
                {
                    string content = DownloadRemoteText(sourceUri, GetPreferredUserAgent(media));
                    string resolved = ResolveStrmContent(content, sourceUri, null);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        InheritAuthorizationHeader(sourceUri, resolved, extraHeaders);
                        WriteTrace("TryResolveStrmSource remote resolved: " + resolved);
                    }

                    return resolved;
                }

                string localPath = mediaSource;
                if (Uri.TryCreate(mediaSource, UriKind.Absolute, out sourceUri) && sourceUri.IsFile)
                {
                    localPath = sourceUri.LocalPath;
                }

                if (!File.Exists(localPath))
                {
                    WriteTrace("TryResolveStrmSource local file missing: " + localPath);
                    return null;
                }

                string localContent = File.ReadAllText(localPath, Encoding.UTF8);
                string localResolved = ResolveStrmContent(localContent, null, localPath);
                if (!string.IsNullOrWhiteSpace(localResolved))
                {
                    WriteTrace("TryResolveStrmSource local resolved: " + localResolved);
                }

                return localResolved;
            }
            catch (Exception ex)
            {
                WriteTrace("TryResolveStrmSource failed: " + ex);
                return null;
            }
        }

        private static bool IsStrmSource(string mediaSource)
        {
            if (string.IsNullOrWhiteSpace(mediaSource))
            {
                return false;
            }

            Uri uri;
            if (Uri.TryCreate(mediaSource, UriKind.Absolute, out uri))
            {
                string path = uri.IsFile ? uri.LocalPath : uri.AbsolutePath;
                return path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase);
            }

            return mediaSource.EndsWith(".strm", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveStrmContent(string content, Uri sourceUri, string localPath)
        {
            string candidate = GetStrmTargetLine(content);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                WriteTrace("ResolveStrmContent: empty");
                return null;
            }

            if (candidate.StartsWith("//", StringComparison.Ordinal))
            {
                if (sourceUri == null)
                {
                    return "https:" + candidate;
                }

                return sourceUri.Scheme + ":" + candidate;
            }

            Uri absoluteUri;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out absoluteUri))
            {
                return NormalizeMediaSource(absoluteUri);
            }

            if (sourceUri != null)
            {
                Uri combinedUri;
                if (Uri.TryCreate(sourceUri, candidate, out combinedUri))
                {
                    return NormalizeMediaSource(combinedUri);
                }
            }

            if (!string.IsNullOrWhiteSpace(localPath))
            {
                string directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return Path.GetFullPath(Path.Combine(directory, candidate));
                }
            }

            return candidate;
        }

        private static string GetStrmTargetLine(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            string sanitized = content.Replace("\uFEFF", string.Empty);
            string[] lines = sanitized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                return line;
            }

            return null;
        }

        private static string DownloadRemoteText(Uri uri, string userAgent)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                request.UserAgent = userAgent;
            }

            string authorization = BuildAuthorizationHeader(uri);
            if (!string.IsNullOrWhiteSpace(authorization))
            {
                request.Headers[HttpRequestHeader.Authorization] = authorization;
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream memoryStream = new MemoryStream())
            {
                if (stream == null)
                {
                    return null;
                }

                stream.CopyTo(memoryStream);
                byte[] data = memoryStream.ToArray();
                Encoding encoding = Encoding.UTF8;
                if (!string.IsNullOrWhiteSpace(response.CharacterSet))
                {
                    try
                    {
                        encoding = Encoding.GetEncoding(response.CharacterSet);
                    }
                    catch
                    {
                    }
                }

                return encoding.GetString(data);
            }
        }

        private static string GetPreferredUserAgent(object media)
        {
            foreach (string option in GetMediaVlcOptions(media))
            {
                string userAgent = GetVlcOptionValue(option, ":http-user-agent=");
                if (!string.IsNullOrWhiteSpace(userAgent))
                {
                    return userAgent;
                }
            }

            return null;
        }

        private static void InheritAuthorizationHeader(Uri sourceUri, string resolvedSource, List<string> extraHeaders)
        {
            if (sourceUri == null || string.IsNullOrWhiteSpace(sourceUri.UserInfo))
            {
                return;
            }

            Uri resolvedUri;
            if (!Uri.TryCreate(resolvedSource, UriKind.Absolute, out resolvedUri))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(resolvedUri.UserInfo))
            {
                return;
            }

            if (!string.Equals(sourceUri.Scheme, resolvedUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(sourceUri.Host, resolvedUri.Host, StringComparison.OrdinalIgnoreCase) ||
                sourceUri.Port != resolvedUri.Port)
            {
                return;
            }

            AppendAuthorizationHeaderFromUri(sourceUri, extraHeaders);
        }

        private static void AppendAuthorizationHeaderFromUri(Uri uri, List<string> extraHeaders)
        {
            string authorization = BuildAuthorizationHeader(uri);
            if (string.IsNullOrWhiteSpace(authorization))
            {
                return;
            }

            AddHeaderIfMissing(extraHeaders, "Authorization: " + authorization);
        }

        private static string BuildAuthorizationHeader(Uri uri)
        {
            if (uri == null || string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                return null;
            }

            string userInfo = Uri.UnescapeDataString(uri.UserInfo);
            byte[] bytes = Encoding.UTF8.GetBytes(userInfo);
            return "Basic " + Convert.ToBase64String(bytes);
        }

        private static string StripUserInfo(Uri uri)
        {
            if (uri == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                return uri.AbsoluteUri;
            }

            UriBuilder builder = new UriBuilder(uri);
            builder.UserName = string.Empty;
            builder.Password = string.Empty;
            return builder.Uri.AbsoluteUri;
        }

        private static void BeginPlaybackProgressSync(string playerPath, Process process, object media, string mediaSource)
        {
            if (!IsPotPlayer(playerPath) || media == null)
            {
                return;
            }

            object playRelatedInfo = GetPlayRelatedInfo(media);
            if (playRelatedInfo == null)
            {
                WriteTrace("BeginPlaybackProgressSync skipped: play related info missing");
                return;
            }

            string playlistPath = GetPotPlayerPlaylistPath(playerPath);
            if (string.IsNullOrWhiteSpace(playlistPath))
            {
                WriteTrace("BeginPlaybackProgressSync skipped: playlist path empty");
                return;
            }

            WriteTrace("BeginPlaybackProgressSync start: playlist=" + playlistPath);
            ThreadPool.QueueUserWorkItem(
                MonitorPotPlayerProgressCallback,
                new object[] { playerPath, process, mediaSource, playlistPath, playRelatedInfo });
        }

        private static void MonitorPotPlayerProgressCallback(object state)
        {
            object[] values = state as object[];
            if (values == null || values.Length < 5)
            {
                WriteTrace("MonitorPotPlayerProgressCallback skipped: invalid state");
                return;
            }

            MonitorPotPlayerProgress(
                values[0] as string,
                values[1] as Process,
                values[2] as string,
                values[3] as string,
                values[4]);
        }

        private static object GetPlayRelatedInfo(object media)
        {
            if (media == null)
            {
                return null;
            }

            PropertyInfo property = media.GetType().GetProperty(
                "PlayRelatedInfo",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property != null ? property.GetValue(media, null) : null;
        }

        private static string GetPotPlayerPlaylistPath(string playerPath)
        {
            if (string.IsNullOrWhiteSpace(playerPath))
            {
                return null;
            }

            string directory = Path.GetDirectoryName(playerPath);
            string playerFileName = Path.GetFileNameWithoutExtension(playerPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(playerFileName))
            {
                return null;
            }

            return Path.Combine(directory, "Playlist", playerFileName + ".dpl");
        }

        private static void TryBeginPlaybackProgressSync(string playerPath, Process process, object media, string mediaSource)
        {
            try
            {
                BeginPlaybackProgressSync(playerPath, process, media, mediaSource);
            }
            catch (Exception ex)
            {
                WriteTrace("BeginPlaybackProgressSync failed: " + ex);
            }
        }

        private static void MonitorPotPlayerProgress(
            string playerPath,
            Process process,
            string launchedSource,
            string playlistPath,
            object playRelatedInfo)
        {
            try
            {
                string normalizedSource = NormalizeCompareValue(launchedSource);
                uint lastSyncedDuration = 0;
                DateTime startTime = DateTime.UtcNow;
                bool processExited = false;

                while ((DateTime.UtcNow - startTime).TotalHours < 12)
                {
                    if (!processExited && process != null)
                    {
                        try
                        {
                            processExited = process.HasExited;
                        }
                        catch
                        {
                            processExited = true;
                        }
                    }

                    PotPlayerPlaylistState state = ReadPotPlayerPlaylistState(playlistPath);
                    if (state != null &&
                        IsSameMediaSource(normalizedSource, state.PlayName) &&
                        state.PlayTime > 0 &&
                        state.PlayTime >= lastSyncedDuration)
                    {
                        if (TrySyncRecordWatched(playRelatedInfo, state.PlayTime, state.TotalDuration))
                        {
                            lastSyncedDuration = state.PlayTime;
                        }
                    }

                    if (processExited)
                    {
                        Process replacement = FindRunningPotPlayerProcess(playerPath);
                        if (replacement != null &&
                            (process == null || replacement.Id != process.Id))
                        {
                            WriteTrace("MonitorPotPlayerProgress switch process: " + replacement.Id);
                            process = replacement;
                            processExited = false;
                            continue;
                        }

                        Thread.Sleep(1000);

                        state = ReadPotPlayerPlaylistState(playlistPath);
                        if (state != null &&
                            IsSameMediaSource(normalizedSource, state.PlayName) &&
                            state.PlayTime > 0 &&
                            state.PlayTime >= lastSyncedDuration)
                        {
                            TrySyncRecordWatched(playRelatedInfo, state.PlayTime, state.TotalDuration);
                        }

                        break;
                    }

                    Thread.Sleep(5000);
                }
            }
            catch (Exception ex)
            {
                WriteTrace("MonitorPotPlayerProgress failed: " + ex);
            }
        }

        private static Process FindRunningPotPlayerProcess(string playerPath)
        {
            if (string.IsNullOrWhiteSpace(playerPath))
            {
                return null;
            }

            string processName = Path.GetFileNameWithoutExtension(playerPath);
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            try
            {
                foreach (Process candidate in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        string candidatePath = candidate.MainModule != null ? candidate.MainModule.FileName : null;
                        if (!string.IsNullOrWhiteSpace(candidatePath) &&
                            string.Equals(candidatePath, playerPath, StringComparison.OrdinalIgnoreCase) &&
                            !candidate.HasExited)
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static PotPlayerPlaylistState ReadPotPlayerPlaylistState(string playlistPath)
        {
            if (string.IsNullOrWhiteSpace(playlistPath) || !File.Exists(playlistPath))
            {
                return null;
            }

            try
            {
                PotPlayerPlaylistState state = new PotPlayerPlaylistState();
                string[] lines = File.ReadAllLines(playlistPath, Encoding.UTF8);
                foreach (string rawLine in lines)
                {
                    string line = rawLine != null ? rawLine.Trim() : null;
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (line.StartsWith("playname=", StringComparison.OrdinalIgnoreCase))
                    {
                        state.PlayName = line.Substring("playname=".Length).Trim();
                        continue;
                    }

                    if (line.StartsWith("playtime=", StringComparison.OrdinalIgnoreCase))
                    {
                        state.PlayTime = ParseUInt32Value(line.Substring("playtime=".Length));
                        continue;
                    }

                    if (line.StartsWith("1*duration2*", StringComparison.OrdinalIgnoreCase))
                    {
                        state.TotalDuration = ParseUInt32Value(line.Substring("1*duration2*".Length));
                    }
                }

                if (!string.IsNullOrWhiteSpace(state.PlayName))
                {
                    WriteTrace("ReadPotPlayerPlaylistState: playname=" + state.PlayName + " playtime=" + state.PlayTime + " total=" + state.TotalDuration);
                }

                return state;
            }
            catch (Exception ex)
            {
                WriteTrace("ReadPotPlayerPlaylistState failed: " + ex);
                return null;
            }
        }

        private static uint ParseUInt32Value(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            uint value;
            if (!uint.TryParse(text.Trim(), out value) || value == 0)
            {
                return 0;
            }

            uint seconds = value / 1000;
            return seconds > 0 ? seconds : 1;
        }

        private static bool IsSameMediaSource(string launchedSource, string playlistSource)
        {
            if (string.IsNullOrWhiteSpace(launchedSource) || string.IsNullOrWhiteSpace(playlistSource))
            {
                return false;
            }

            return string.Equals(launchedSource, NormalizeCompareValue(playlistSource), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCompareValue(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            Uri uri;
            if (Uri.TryCreate(source, UriKind.Absolute, out uri))
            {
                return uri.AbsoluteUri;
            }

            return source.Trim();
        }

        private static bool TrySyncRecordWatched(object playRelatedInfo, uint watchedDuration, uint totalDurationFromPlaylist)
        {
            try
            {
                object request = CreateRecordWatchedRequest(playRelatedInfo, watchedDuration, totalDurationFromPlaylist);
                if (request == null)
                {
                    WriteTrace("TrySyncRecordWatched skipped: request build failed");
                    return false;
                }

                Type ntHelperType = Type.GetType("Filmly.Helpers.NtHelper, BaoMiHua", false);
                if (ntHelperType == null)
                {
                    WriteTrace("TrySyncRecordWatched skipped: NtHelper type missing");
                    return false;
                }

                MethodInfo method = ntHelperType.GetMethod(
                    "Save_RecordWatched",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                {
                    WriteTrace("TrySyncRecordWatched skipped: Save_RecordWatched missing");
                    return false;
                }

                object task = method.Invoke(null, new[] { request });
                if (task == null)
                {
                    WriteTrace("TrySyncRecordWatched invoked: task null");
                    return true;
                }

                PropertyInfo awaiterProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
                object result = awaiterProperty != null ? awaiterProperty.GetValue(task, null) : null;
                if (result != null)
                {
                    PropertyInfo codeProperty = result.GetType().GetProperty("code", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    object code = codeProperty != null ? codeProperty.GetValue(result, null) : null;
                    WriteTrace("TrySyncRecordWatched result code=" + (code ?? "null"));
                }
                else
                {
                    WriteTrace("TrySyncRecordWatched result null");
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteTrace("TrySyncRecordWatched failed: " + ex);
                return false;
            }
        }

        private static object CreateRecordWatchedRequest(object playRelatedInfo, uint watchedDuration, uint totalDurationFromPlaylist)
        {
            if (playRelatedInfo == null)
            {
                return null;
            }

            Type requestType = Type.GetType("Filmly.Helpers.NtHelperRecord+RecordWatched_REQ, BaoMiHua", false);
            if (requestType == null)
            {
                return null;
            }

            object request = Activator.CreateInstance(requestType);
            object baseFileInfo = GetValue(playRelatedInfo, "Base_File_Info");

            SetValue(request, "file_id", GetValue(baseFileInfo, "id") as string);
            SetValue(request, "media_type", ConvertToInt32(GetValue(playRelatedInfo, "MediaType")));
            SetValue(request, "tmdb_id", GetValue(playRelatedInfo, "TmdbId") as string);
            SetValue(request, "collection_id", GetValue(playRelatedInfo, "CollectionId") as string);
            SetValue(request, "media_id", GetValue(playRelatedInfo, "MediaId") as string);
            SetValue(request, "season_index", ConvertToInt32(GetValue(playRelatedInfo, "SeasonIndex")));
            SetValue(request, "episode_index", ConvertToInt32(GetValue(playRelatedInfo, "EpisodeIndex")));
            SetValue(request, "watched_duration", watchedDuration);

            uint totalDuration = ConvertToUInt32(GetValue(baseFileInfo, "total_duration"));
            if (totalDuration == 0)
            {
                totalDuration = totalDurationFromPlaylist;
            }

            if (totalDuration < watchedDuration)
            {
                totalDuration = watchedDuration;
            }

            SetValue(request, "total_duration", totalDuration);
            SetValue(request, "player_title", GetPlayerTitle(playRelatedInfo));

            WriteTrace("CreateRecordWatchedRequest: fileId=" + (GetValue(baseFileInfo, "id") ?? "null") + " watched=" + watchedDuration + " total=" + totalDuration);
            return request;
        }

        private static string GetPlayerTitle(object playRelatedInfo)
        {
            string title = GetValue(playRelatedInfo, "PlayerTitle") as string;
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            object baseFileInfo = GetValue(playRelatedInfo, "Base_File_Info");
            return GetValue(baseFileInfo, "id") as string;
        }

        private static object GetValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null)
            {
                return property.GetValue(instance, null);
            }

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            return null;
        }

        private static void SetValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
            {
                return;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null)
            {
                property.SetValue(instance, value, null);
                return;
            }

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                field.SetValue(instance, value);
            }
        }

        private static int ConvertToInt32(object value)
        {
            try
            {
                return value != null ? Convert.ToInt32(value) : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static uint ConvertToUInt32(object value)
        {
            try
            {
                return value != null ? Convert.ToUInt32(value) : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string GetResolvedPlayerPath()
        {
            string rawPath = ReadSettingValue();
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                WriteTrace("GetResolvedPlayerPath: setting empty");
                return null;
            }

            rawPath = NormalizeStoredString(rawPath);
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                WriteTrace("GetResolvedPlayerPath: normalized path empty");
                return null;
            }

            rawPath = Environment.ExpandEnvironmentVariables(rawPath.Trim());
            bool exists = File.Exists(rawPath);
            WriteTrace("GetResolvedPlayerPath: " + rawPath + " exists=" + exists);
            return exists ? rawPath : null;
        }

        private static string ReadSettingValue()
        {
            string settingsPath = ResolveLocalSettingsPath();
            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(settingsPath, Encoding.UTF8);
                return FindJsonStringProperty(json, "Values/ExternalPlayerPath");
            }
            catch
            {
            }

            return null;
        }

        private static string ResolveLocalSettingsPath()
        {
            string applicationDataFolder = "BaoMiHua/ApplicationData";
            string localSettingsFile = "LocalSettings.json";

            try
            {
                string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (File.Exists(appSettingsPath))
                {
                    string json = File.ReadAllText(appSettingsPath, Encoding.UTF8);
                    string folderValue = FindJsonStringProperty(json, "ApplicationDataFolder");
                    if (!string.IsNullOrWhiteSpace(folderValue))
                    {
                        applicationDataFolder = folderValue;
                    }

                    string fileValue = FindJsonStringProperty(json, "LocalSettingsFile");
                    if (!string.IsNullOrWhiteSpace(fileValue))
                    {
                        localSettingsFile = fileValue;
                    }
                }
            }
            catch
            {
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, applicationDataFolder, localSettingsFile);
        }

        private static string NormalizeStoredString(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }

            return trimmed;
        }

        private static string FindJsonStringProperty(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            string pattern = "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return null;
            }

            Group valueGroup = match.Groups["value"];
            return valueGroup.Success ? DecodeJsonString(valueGroup.Value) : null;
        }

        private static string DecodeJsonString(string value)
        {
            if (value == null)
            {
                return null;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (current != '\\' || index == value.Length - 1)
                {
                    builder.Append(current);
                    continue;
                }

                index++;
                char escaped = value[index];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escaped);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (index + 4 < value.Length)
                        {
                            string hex = value.Substring(index + 1, 4);
                            int codePoint;
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out codePoint))
                            {
                                builder.Append((char)codePoint);
                                index += 4;
                                break;
                            }
                        }

                        builder.Append("\\u");
                        break;
                    default:
                        builder.Append(escaped);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void WriteTrace(string message)
        {
            try
            {
                string path = GetTraceFilePath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                    " pid=" + Process.GetCurrentProcess().Id +
                    " " + message +
                    Environment.NewLine;
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string GetTraceFilePath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "BaoMiHua", "log", "external-player.log");
        }

        private sealed class PotPlayerPlaylistState
        {
            internal string PlayName;
            internal uint PlayTime;
            internal uint TotalDuration;
        }
    }
}
