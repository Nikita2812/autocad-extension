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

        // ─────────────────────────────────────────────────────────────────────
        // FINGERPRINT RULE LOADING
        // Loads block classification rules from fingerprints.json
        // ─────────────────────────────────────────────────────────────────────

        private List<FingerprintRule> _cachedFingerprintRules;
        private string _fingerprintsConfigPath;

        /// <summary>
        /// Loads fingerprint rules from the fingerprints.json configuration file.
        /// Rules are cached in memory after first load.
        /// All matching rules are returned separated by " / " (no priority ordering).
        /// </summary>
        public List<FingerprintRule> LoadFingerprintRules(string configPath = null)
        {
            // Return cached rules if already loaded
            if (_cachedFingerprintRules != null && !string.IsNullOrEmpty(_fingerprintsConfigPath) && File.Exists(_fingerprintsConfigPath))
            {
                return _cachedFingerprintRules;
            }

            _cachedFingerprintRules = new List<FingerprintRule>();

            try
            {
                // Determine fingerprints.json path
                string fingerprintsPath = configPath ?? GetDefaultFingerprintsPath();

                if (!File.Exists(fingerprintsPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Fingerprints config not found at: {fingerprintsPath}");
                    return _cachedFingerprintRules; // Return empty list
                }

                _fingerprintsConfigPath = fingerprintsPath;
                string json = File.ReadAllText(fingerprintsPath).Trim();
                Dictionary<string, object> config = ParseJsonToDictionary(json);

                // Extract the fingerprint_rules array
                if (config.ContainsKey("fingerprint_rules") && config["fingerprint_rules"] is List<object> rulesArray)
                {
                    foreach (var ruleObj in rulesArray)
                    {
                        if (ruleObj is Dictionary<string, object> ruleDict)
                        {
                            FingerprintRule rule = ParseFingerprintRule(ruleDict);
                            if (rule != null)
                            {
                                _cachedFingerprintRules.Add(rule);
                            }
                        }
                    }
                }

                // Return rules (no sorting - all matches are equally valid)
                System.Diagnostics.Debug.WriteLine($"Loaded {_cachedFingerprintRules.Count} fingerprint rules from {fingerprintsPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading fingerprint rules: {ex.Message}");
            }

            return _cachedFingerprintRules;
        }

        /// <summary>
        /// Gets the cached fingerprint rules. Loads them first if not already cached.
        /// </summary>
        public List<FingerprintRule> GetFingerprintRules()
        {
            if (_cachedFingerprintRules == null)
            {
                LoadFingerprintRules();
            }
            return _cachedFingerprintRules ?? new List<FingerprintRule>();
        }

        private static string GetDefaultFingerprintsPath()
        {
            // Try to find fingerprints.json in the same directory as the assembly
            string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
            string configPath = Path.Combine(assemblyDirectory, "fingerprints.json");

            if (!File.Exists(configPath))
            {
                // Fallback to temp directory
                configPath = Path.Combine(Path.GetTempPath(), "ai_review", "fingerprints.json");
            }

            return configPath;
        }

        private FingerprintRule ParseFingerprintRule(Dictionary<string, object> ruleDict)
        {
            try
            {
                var rule = new FingerprintRule
                {
                    AssignedName = GetStringFromDict(ruleDict, "assigned_name", "UNKNOWN"),
                    Description = GetStringFromDict(ruleDict, "description", ""),
                    Department = GetStringFromDict(ruleDict, "department", "")  // Empty = all departments
                };

                // Parse geometry_match constraints
                if (ruleDict.ContainsKey("geometry_match") && ruleDict["geometry_match"] is Dictionary<string, object> matchDict)
                {
                    rule.GeometryMatch = ParseGeometryConstraints(matchDict);
                }

                return rule;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing fingerprint rule: {ex.Message}");
                return null;
            }
        }

        private GeometryConstraints ParseGeometryConstraints(Dictionary<string, object> constraintsDict)
        {
            var constraints = new GeometryConstraints
            {
                // Try exact match format first (circles, lines, etc.), fallback to legacy range format (min_circles, max_circles)
                Circles = GetNullableIntFromDict(constraintsDict, "circles") ?? 
                          (GetNullableIntFromDict(constraintsDict, "min_circles") == GetNullableIntFromDict(constraintsDict, "max_circles") 
                              ? GetNullableIntFromDict(constraintsDict, "min_circles") 
                              : null),

                Lines = GetNullableIntFromDict(constraintsDict, "lines") ?? 
                        (GetNullableIntFromDict(constraintsDict, "min_lines") == GetNullableIntFromDict(constraintsDict, "max_lines") 
                            ? GetNullableIntFromDict(constraintsDict, "min_lines") 
                            : null),

                Polylines = GetNullableIntFromDict(constraintsDict, "polylines") ?? 
                            (GetNullableIntFromDict(constraintsDict, "min_polylines") == GetNullableIntFromDict(constraintsDict, "max_polylines") 
                                ? GetNullableIntFromDict(constraintsDict, "min_polylines") 
                                : null),

                Arcs = GetNullableIntFromDict(constraintsDict, "arcs") ?? 
                       (GetNullableIntFromDict(constraintsDict, "min_arcs") == GetNullableIntFromDict(constraintsDict, "max_arcs") 
                           ? GetNullableIntFromDict(constraintsDict, "min_arcs") 
                           : null),

                Hatches = GetNullableIntFromDict(constraintsDict, "hatches") ?? 
                          (GetNullableIntFromDict(constraintsDict, "min_hatches") == GetNullableIntFromDict(constraintsDict, "max_hatches") 
                              ? GetNullableIntFromDict(constraintsDict, "min_hatches") 
                              : null),

                Texts = GetNullableIntFromDict(constraintsDict, "texts") ?? 
                        (GetNullableIntFromDict(constraintsDict, "min_texts") == GetNullableIntFromDict(constraintsDict, "max_texts") 
                            ? GetNullableIntFromDict(constraintsDict, "min_texts") 
                            : null)
            };

            return constraints;
        }

        private string GetStringFromDict(Dictionary<string, object> dict, string key, string defaultValue)
        {
            if (dict.ContainsKey(key) && dict[key] != null)
            {
                string val = dict[key].ToString();
                return string.IsNullOrWhiteSpace(val) ? defaultValue : val;
            }
            return defaultValue;
        }

        private int GetIntFromDict(Dictionary<string, object> dict, string key, int defaultValue)
        {
            if (dict.ContainsKey(key) && dict[key] != null)
            {
                if (int.TryParse(dict[key].ToString(), out int val))
                {
                    return val;
                }
            }
            return defaultValue;
        }

        private int? GetNullableIntFromDict(Dictionary<string, object> dict, string key)
        {
            if (dict.ContainsKey(key) && dict[key] != null)
            {
                if (int.TryParse(dict[key].ToString(), out int val))
                {
                    return val;
                }
            }
            return null;
        }
    }
}
