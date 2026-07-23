using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using UI.Common;
using UI.TraySetting;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private static async Task<string> BuildWeatherContextAsync()
    {
        if (!Plugin.WeatherEnabled.Value)
            return string.Empty;
        if (_cachedWeatherContext.Length > 0 && DateTimeOffset.Now - _weatherFetchedAt < TimeSpan.FromMinutes(10))
            return _cachedWeatherContext;

        try
        {
            await ResolveWeatherLocationFromIpAsync().ConfigureAwait(false);
            var latitude = Plugin.WeatherLatitude.Value.ToString(CultureInfo.InvariantCulture);
            var longitude = Plugin.WeatherLongitude.Value.ToString(CultureInfo.InvariantCulture);
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,is_day&temperature_unit=celsius&wind_speed_unit=kmh&precipitation_unit=mm&timezone=auto";
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var current = document.RootElement.GetProperty("current");
            var temperature = current.GetProperty("temperature_2m").GetDouble();
            var apparent = current.GetProperty("apparent_temperature").GetDouble();
            var humidity = current.GetProperty("relative_humidity_2m").GetDouble();
            var precipitation = current.GetProperty("precipitation").GetDouble();
            var wind = current.GetProperty("wind_speed_10m").GetDouble();
            var code = current.GetProperty("weather_code").GetInt32();
            var daylight = current.GetProperty("is_day").GetInt32() == 1 ? "白天" : "夜間";
            var condition = DescribeWeatherCode(code);
            _cachedWeatherContext = $"\n目前「{Plugin.WeatherLocationName.Value}」的即時天氣（Open-Meteo 模型資料）：{condition}，{daylight}，氣溫 {temperature:0.#}°C，體感 {apparent:0.#}°C，相對濕度 {humidity:0}% ，目前降水 {precipitation:0.##} mm，風速 {wind:0.#} km/h。被問到現在的天氣時依此回答，不要自行編造未提供的資訊。";
            _weatherFetchedAt = DateTimeOffset.Now;
            return _cachedWeatherContext;
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Weather lookup failed; chat continues without it: {exception.Message}");
            return "\n目前無法取得可靠的即時天氣；若被問到天氣，坦白說暫時看不到，不要猜測。";
        }
    }

    private static async Task ResolveWeatherLocationFromIpAsync()
    {
        if (_ipWeatherLocationResolved || !Plugin.WeatherAutoDetectFromIp.Value)
            return;
        _ipWeatherLocationResolved = true;

        try
        {
            const string url = "https://ipwho.is/?fields=success,message,city,region,country,latitude,longitude";
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = document.RootElement;
            if (root.TryGetProperty("success", out var success) && !success.GetBoolean())
            {
                var message = root.TryGetProperty("message", out var error) ? error.GetString() : "unknown error";
                throw new InvalidOperationException(message);
            }

            var latitude = root.GetProperty("latitude").GetDouble();
            var longitude = root.GetProperty("longitude").GetDouble();
            var city = root.TryGetProperty("city", out var cityElement) ? cityElement.GetString() : null;
            var region = root.TryGetProperty("region", out var regionElement) ? regionElement.GetString() : null;
            var country = root.TryGetProperty("country", out var countryElement) ? countryElement.GetString() : null;
            var locationParts = new[] { city, region, country }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var locationName = string.Join("，", locationParts);
            if (locationName.Length == 0)
                locationName = Plugin.WeatherLocationName.Value;

            Plugin.WeatherLatitude.Value = latitude;
            Plugin.WeatherLongitude.Value = longitude;
            Plugin.WeatherLocationName.Value = locationName;
            Plugin.PluginLog.LogInfo($"Weather location approximated from IP as '{locationName}' (the IP address was not stored)." );
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"IP weather location lookup failed; using configured location: {exception.Message}");
        }
    }

    private static string DescribeWeatherCode(int code)
    {
        if (code == 0) return "晴朗";
        if (code <= 3) return "多雲";
        if (code == 45 || code == 48) return "有霧";
        if (code >= 51 && code <= 57) return "毛毛雨";
        if (code >= 61 && code <= 67) return "下雨";
        if (code >= 71 && code <= 77) return "下雪";
        if (code >= 80 && code <= 82) return "陣雨";
        if (code >= 85 && code <= 86) return "陣雪";
        if (code >= 95) return "雷雨";
        return $"天氣代碼 {code}";
    }

}
