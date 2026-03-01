using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HelloWorldNET
{
    public class ConfigManager
    {
        private static ConfigManager _instance;
        private Dictionary<string, object> _config;
        private readonly string _configPath;

        private ConfigManager(string configPath)
        {
            _configPath = configPath;
            LoadConfiguration();
        }

        public static ConfigManager GetInstance(string configPath = null)
        {
            if (_instance == null)
            {
                string path = configPath ?? GetDefaultConfigPath();
                _instance = new ConfigManager(path);
            }
            return _instance;
        }

        private static string GetDefaultConfigPath()
        {
            // Try to find config.json in the same directory as the assembly
            string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
            string configPath = Path.Combine(assemblyDirectory, "config.json");
            
            if (!File.Exists(configPath))
            {
                // Fallback to temp directory
                configPath = Path.Combine(Path.GetTempPath(), "ai_review", "config.json");
            }
            
            return configPath;
        }

        private void LoadConfiguration()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    _config = GetDefaultConfig();
                    return;
                }

                string json = File.ReadAllText(_configPath).Trim();
                _config = ParseJsonToDictionary(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
                _config = GetDefaultConfig();
            }
        }

        private Dictionary<string, object> GetDefaultConfig()
        {
            return new Dictionary<string, object>
            {
                { "api", new Dictionary<string, object>
                    {
                        { "endpoint", "https://sesphase2.backend.testing.env.thelinkai.com/drawings/review" },
                        { "timeout_seconds", 300 },
                        { "model", "google/gemini-3-flash-preview" },
                        { "save_report", true }
                    }
                },
                { "defaults", new Dictionary<string, object>
                    {
                        { "participant_id", "852821f6-1214-4dae-a35f-0c5a4df09555" }
                    }
                }
            };
        }

        public string GetApiEndpoint()
        {
            return GetString("api", "endpoint", "https://sesphase2.backend.testing.env.thelinkai.com/drawings/review");
        }

        public int GetApiTimeoutSeconds()
        {
            return GetInt("api", "timeout_seconds", 300);
        }

        public string GetApiModel()
        {
            return GetString("api", "model", "google/gemini-3-flash-preview");
        }

        public bool GetApiSaveReport()
        {
            return GetBool("api", "save_report", true);
        }

        public string GetDefaultParticipantId()
        {
            return GetString("defaults", "participant_id", "852821f6-1214-4dae-a35f-0c5a4df09555");
        }

        private string GetString(string section, string key, string defaultValue)
        {
            try
            {
                if (_config.ContainsKey(section) && _config[section] is Dictionary<string, object> sectionDict)
                {
                    if (sectionDict.ContainsKey(key) && sectionDict[key] != null)
                    {
                        string value = sectionDict[key].ToString();
                        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        private int GetInt(string section, string key, int defaultValue)
        {
            try
            {
                if (_config.ContainsKey(section) && _config[section] is Dictionary<string, object> sectionDict)
                {
                    if (sectionDict.ContainsKey(key) && sectionDict[key] != null)
                    {
                        if (int.TryParse(sectionDict[key].ToString(), out int value))
                        {
                            return value;
                        }
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        private bool GetBool(string section, string key, bool defaultValue)
        {
            try
            {
                if (_config.ContainsKey(section) && _config[section] is Dictionary<string, object> sectionDict)
                {
                    if (sectionDict.ContainsKey(key) && sectionDict[key] != null)
                    {
                        if (bool.TryParse(sectionDict[key].ToString(), out bool value))
                        {
                            return value;
                        }
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        private Dictionary<string, object> ParseJsonToDictionary(string json)
        {
            json = json.Trim();
            if (json.StartsWith("{") && json.EndsWith("}"))
            {
                return ParseJsonObject(json);
            }
            return new Dictionary<string, object>();
        }

        private Dictionary<string, object> ParseJsonObject(string json)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            json = json.Substring(1, json.Length - 2).Trim();

            int braceCount = 0;
            int bracketCount = 0;
            int quoteCount = 0;
            int lastIndex = 0;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                    quoteCount++;

                if (quoteCount % 2 == 0)
                {
                    if (c == '{') braceCount++;
                    else if (c == '}') braceCount--;
                    else if (c == '[') bracketCount++;
                    else if (c == ']') bracketCount--;
                    else if (c == ',' && braceCount == 0 && bracketCount == 0)
                    {
                        string pair = json.Substring(lastIndex, i - lastIndex).Trim();
                        ParseKeyValue(pair, result);
                        lastIndex = i + 1;
                    }
                }
            }

            if (lastIndex < json.Length)
            {
                string pair = json.Substring(lastIndex).Trim();
                ParseKeyValue(pair, result);
            }

            return result;
        }

        private void ParseKeyValue(string pair, Dictionary<string, object> dict)
        {
            int colonIndex = pair.IndexOf(':');
            if (colonIndex <= 0) return;

            string key = pair.Substring(0, colonIndex).Trim().Trim('"');
            string valueStr = pair.Substring(colonIndex + 1).Trim();

            object value = ParseJsonValue(valueStr);
            dict[key] = value;
        }

        private object ParseJsonValue(string value)
        {
            value = value.Trim();

            if (value == "null")
                return null;
            else if (value == "true")
                return true;
            else if (value == "false")
                return false;
            else if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                return value.Substring(1, value.Length - 2);
            else if (value.StartsWith("{") && value.EndsWith("}"))
                return ParseJsonObject(value);
            else if (value.StartsWith("[") && value.EndsWith("]"))
                return ParseJsonArray(value);
            else if (int.TryParse(value, out int intVal))
                return intVal;
            else if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double doubleVal))
                return doubleVal;
            else
                return value;
        }

        private List<object> ParseJsonArray(string json)
        {
            List<object> result = new List<object>();
            json = json.Substring(1, json.Length - 2).Trim();

            if (string.IsNullOrEmpty(json))
                return result;

            int braceCount = 0;
            int bracketCount = 0;
            int quoteCount = 0;
            int lastIndex = 0;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                    quoteCount++;

                if (quoteCount % 2 == 0)
                {
                    if (c == '{') braceCount++;
                    else if (c == '}') braceCount--;
                    else if (c == '[') bracketCount++;
                    else if (c == ']') bracketCount--;
                    else if (c == ',' && braceCount == 0 && bracketCount == 0)
                    {
                        string item = json.Substring(lastIndex, i - lastIndex).Trim();
                        result.Add(ParseJsonValue(item));
                        lastIndex = i + 1;
                    }
                }
            }

            if (lastIndex < json.Length)
            {
                string item = json.Substring(lastIndex).Trim();
                result.Add(ParseJsonValue(item));
            }

            return result;
        }
    }
}
