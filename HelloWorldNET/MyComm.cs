using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

[assembly: CommandClass(typeof(HelloWorldNET.MyComm))]
namespace HelloWorldNET
{
    public class MyComm
    {

        [CommandMethod("extractJSON")]
        public void ExtractDrawingJson()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                List<Dictionary<string, object>> entities = new List<Dictionary<string, object>>();

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId objId in btr)
                    {
                        Entity entity = (Entity)tr.GetObject(objId, OpenMode.ForRead);
                        Dictionary<string, object> entityData = new Dictionary<string, object>
                        {
                            { "Type", entity.GetType().Name },
                            { "Layer", entity.Layer },
                            { "Color", entity.ColorIndex.ToString() },
                            { "Linetype", entity.Linetype }
                        };

                        // Add geometry info based on entity type
                        if (entity is Line line)
                        {
                            entityData["StartPoint"] = new { X = line.StartPoint.X, Y = line.StartPoint.Y, Z = line.StartPoint.Z };
                            entityData["EndPoint"] = new { X = line.EndPoint.X, Y = line.EndPoint.Y, Z = line.EndPoint.Z };
                        }
                        else if (entity is Circle circle)
                        {
                            entityData["Center"] = new { X = circle.Center.X, Y = circle.Center.Y, Z = circle.Center.Z };
                            entityData["Radius"] = circle.Radius;
                        }
                        else if (entity is Arc arc)
                        {
                            entityData["Center"] = new { X = arc.Center.X, Y = arc.Center.Y, Z = arc.Center.Z };
                            entityData["Radius"] = arc.Radius;
                            entityData["StartAngle"] = arc.StartAngle;
                            entityData["EndAngle"] = arc.EndAngle;
                        }
                        else if (entity is Polyline polyline)
                        {
                            List<Dictionary<string, double>> points = new List<Dictionary<string, double>>();
                            for (int i = 0; i < polyline.NumberOfVertices; i++)
                            {
                                Point3d pt = polyline.GetPoint3dAt(i);
                                points.Add(new Dictionary<string, double> { { "X", pt.X }, { "Y", pt.Y }, { "Z", pt.Z } });
                            }
                            entityData["Vertices"] = points;
                        }

                        entities.Add(entityData);
                    }

                    tr.Commit();
                }

                // Convert to JSON
                string json = ConvertToJson(entities);

                // Save to file
                string outputPath = Path.Combine(Path.GetDirectoryName(doc.Name), "drawing_data.json");
                File.WriteAllText(outputPath, json);

                ed.WriteMessage($"\nDrawing data extracted to: {outputPath}");

                // Prompt for project_id
                PromptStringOptions pso = new PromptStringOptions("\nEnter Project ID: ");
                pso.AllowSpaces = true;
                PromptResult pr = ed.GetString(pso);

                if (pr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nOperation cancelled.");
                    return;
                }

                string projectId = pr.StringResult;
                string drawingName = Path.GetFileNameWithoutExtension(doc.Name);

                // Call the API endpoint
                ed.WriteMessage("\nSending to API endpoint...");
                Task.Run(async () =>
                {
                    try
                    {
                        string response = await CallReviewEndpoint(outputPath, drawingName, projectId);
                        string formattedResponse = FormatApiResponse(response);
                        ShowScrollableReport(formattedResponse);
                    }
                    catch (System.Exception apiEx)
                    {
                        Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog($"API Error: {apiEx.Message}");
                    }
                });
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError: {ex.Message}");
                Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog($"Error extracting JSON: {ex.Message}");
            }
        }

        private async Task<string> CallReviewEndpoint(string jsonFilePath, string drawingName, string projectId)
        {
            using (HttpClient client = new HttpClient())
            {
                using (MultipartFormDataContent form = new MultipartFormDataContent())
                {
                    // Add JSON file
                    byte[] fileContent = File.ReadAllBytes(jsonFilePath);
                    ByteArrayContent fileStream = new ByteArrayContent(fileContent);
                    fileStream.Headers.Add("Content-Type", "application/json");
                    form.Add(fileStream, "json_file", Path.GetFileName(jsonFilePath));

                    // Add other form fields
                    form.Add(new StringContent(drawingName), "drawing_name");
                    form.Add(new StringContent(projectId), "project_id");
                    form.Add(new StringContent("google/gemini-3-flash-preview"), "model");
                    form.Add(new StringContent("true"), "save_report");
                    form.Add(new StringContent("852821f6-1214-4dae-a35f-0c5a4df09555"), "participant_id");

                    // Send POST request
                    HttpResponseMessage response = await client.PostAsync(
                        "https://sesphase2.backend.testing.env.thelinkai.com/drawings/review",
                        form);

                    // Read response content
                    string responseContent = await response.Content.ReadAsStringAsync();

                    // Return just the response content (JSON)
                    return responseContent;
                }
            }
        }

        private string ConvertToJson(List<Dictionary<string, object>> data)
        {
            StringBuilder json = new StringBuilder();
            json.AppendLine("[");

            for (int i = 0; i < data.Count; i++)
            {
                json.AppendLine("  {");
                var dict = data[i];
                var keys = dict.Keys.ToList();

                for (int j = 0; j < keys.Count; j++)
                {
                    string key = keys[j];
                    object value = dict[key];
                    string jsonValue = GetJsonValue(value);
                    string comma = j < keys.Count - 1 ? "," : "";
                    json.AppendLine($"    \"{key}\": {jsonValue}{comma}");
                }

                string entityComma = i < data.Count - 1 ? "," : "";
                json.AppendLine($"  }}{entityComma}");
            }

            json.AppendLine("]");
            return json.ToString();
        }

        private string GetJsonValue(object value)
        {
            if (value == null)
                return "null";
            else if (value is string)
                return $"\"{value}\"";
            else if (value is bool)
                return value.ToString().ToLower();
            else if (value is System.Collections.IEnumerable && !(value is string))
                return SerializeList(value as System.Collections.IEnumerable);
            else if (value.GetType().IsClass && value.GetType().Name != "String")
                return SerializeObject(value);
            else
                return value.ToString();
        }

        private string SerializeObject(object obj)
        {
            var props = obj.GetType().GetProperties();
            StringBuilder sb = new StringBuilder();
            sb.Append("{");

            for (int i = 0; i < props.Length; i++)
            {
                var prop = props[i];
                var val = prop.GetValue(obj);
                sb.Append($"\"{prop.Name}\": {GetJsonValue(val)}");
                if (i < props.Length - 1) sb.Append(", ");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private string SerializeList(System.Collections.IEnumerable list)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[");

            var items = list.Cast<object>().ToList();
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(GetJsonValue(items[i]));
                if (i < items.Count - 1) sb.Append(", ");
            }

            sb.Append("]");
            return sb.ToString();
        }

        private string FormatApiResponse(string jsonResponse)
        {
            StringBuilder formatted = new StringBuilder();
            formatted.AppendLine("================================================================================");
            formatted.AppendLine("                        DRAWING ANALYSIS REPORT");
            formatted.AppendLine("================================================================================\n");

            try
            {
                // Parse JSON manually for .NET 4.8 compatibility
                Dictionary<string, object> responseData = ParseJson(jsonResponse);

                if (responseData.Count == 0)
                {
                    formatted.AppendLine("Error: Could not parse API response");
                    formatted.AppendLine($"Raw response: {jsonResponse}");
                    return formatted.ToString();
                }

                // Status Section
                formatted.AppendLine("📋 STATUS INFORMATION");
                formatted.AppendLine("─────────────────────────────────────────────────────────────────────────────");
                formatted.AppendLine($"Success: {GetValue(responseData, "success")}");
                formatted.AppendLine($"Message: {GetValue(responseData, "message")}");
                formatted.AppendLine($"Drawing Name: {GetValue(responseData, "drawing_name")}");
                if (responseData.ContainsKey("error") && responseData["error"] != null && !string.IsNullOrEmpty(responseData["error"].ToString()))
                {
                    formatted.AppendLine($"Error: {GetValue(responseData, "error")}");
                }
                formatted.AppendLine();

                // Geometric Analysis Section
                if (responseData.ContainsKey("geometric_analysis") && responseData["geometric_analysis"] is Dictionary<string, object> geoAnalysis && geoAnalysis != null)
                {
                    formatted.AppendLine("📐 GEOMETRIC ANALYSIS");
                    formatted.AppendLine("─────────────────────────────────────────────────────────────────────────────");
                    formatted.AppendLine($"Total Elements: {GetValue(geoAnalysis, "total_elements")}");
                    formatted.AppendLine();

                    // Element Types
                    if (geoAnalysis.ContainsKey("element_types") && geoAnalysis["element_types"] is Dictionary<string, object> elementTypes && elementTypes != null && elementTypes.Count > 0)
                    {
                        formatted.AppendLine("Element Types:");
                        foreach (var kvp in elementTypes)
                        {
                            formatted.AppendLine($"  • {kvp.Key}: {kvp.Value}");
                        }
                        formatted.AppendLine();
                    }

                    // Structural Layers
                    if (geoAnalysis.ContainsKey("structural_layers") && geoAnalysis["structural_layers"] is Dictionary<string, object> layers && layers != null && layers.Count > 0)
                    {
                        formatted.AppendLine("Structural Layers:");
                        foreach (var kvp in layers)
                        {
                            formatted.AppendLine($"  • {kvp.Key}: {kvp.Value}");
                        }
                        formatted.AppendLine();
                    }

                    // Beam Types
                    if (geoAnalysis.ContainsKey("beam_types") && geoAnalysis["beam_types"] is Dictionary<string, object> beamTypes && beamTypes != null)
                    {
                        formatted.AppendLine("Beam Types:");
                        if (beamTypes.Count > 0)
                        {
                            foreach (var kvp in beamTypes)
                            {
                                formatted.AppendLine($"  • {kvp.Key}");
                            }
                        }
                        else
                        {
                            formatted.AppendLine("  (No beam types detected)");
                        }
                        formatted.AppendLine();
                    }

                    // Bolt Holes
                    if (geoAnalysis.ContainsKey("bolt_holes") && geoAnalysis["bolt_holes"] is Dictionary<string, object> boltHoles && boltHoles != null)
                    {
                        formatted.AppendLine("Bolt Holes:");
                        if (boltHoles.Count > 0)
                        {
                            foreach (var kvp in boltHoles)
                            {
                                formatted.AppendLine($"  • {kvp.Key}");
                            }
                        }
                        else
                        {
                            formatted.AppendLine("  (No bolt holes detected)");
                        }
                        formatted.AppendLine();
                    }

                    // Connection Angles
                    if (geoAnalysis.ContainsKey("connection_angles") && geoAnalysis["connection_angles"] is List<object> angles && angles != null)
                    {
                        formatted.AppendLine("Connection Angles:");
                        if (angles.Count > 0)
                        {
                            for (int i = 0; i < angles.Count; i++)
                            {
                                formatted.AppendLine($"  • Connection {i + 1}");
                            }
                        }
                        else
                        {
                            formatted.AppendLine("  (No connections detected)");
                        }
                        formatted.AppendLine();
                    }

                    // Spatial Conflicts
                    if (geoAnalysis.ContainsKey("spatial_conflicts") && geoAnalysis["spatial_conflicts"] is List<object> conflicts && conflicts != null)
                    {
                        formatted.AppendLine("⚠️  SPATIAL CONFLICTS:");
                        if (conflicts.Count > 0)
                        {
                            for (int i = 0; i < conflicts.Count; i++)
                            {
                                formatted.AppendLine($"  • Conflict {i + 1}");
                            }
                        }
                        else
                        {
                            formatted.AppendLine("  ✓ No spatial conflicts detected");
                        }
                        formatted.AppendLine();
                    }
                }

                // Token Usage Section
                if (responseData.ContainsKey("token_usage") && responseData["token_usage"] is Dictionary<string, object> tokens && tokens != null)
                {
                    formatted.AppendLine("🔧 TOKEN USAGE");
                    formatted.AppendLine("─────────────────────────────────────────────────────────────────────────────");
                    formatted.AppendLine($"Prompt Tokens: {GetValue(tokens, "prompt_tokens")}");
                    formatted.AppendLine($"Completion Tokens: {GetValue(tokens, "completion_tokens")}");
                    formatted.AppendLine($"Total Tokens: {GetValue(tokens, "total_tokens")}");
                    formatted.AppendLine();
                }

                // LLM Review Section
                if (responseData.ContainsKey("llm_review") && responseData["llm_review"] != null)
                {
                    formatted.AppendLine("🤖 LLM REVIEW");
                    formatted.AppendLine("─────────────────────────────────────────────────────────────────────────────");
                    string llmReview = responseData["llm_review"].ToString();
                    if (!string.IsNullOrEmpty(llmReview))
                    {
                        formatted.AppendLine(llmReview);
                    }
                    formatted.AppendLine();
                }

                // Full Report Section
                if (responseData.ContainsKey("full_report") && responseData["full_report"] != null)
                {
                    formatted.AppendLine("📄 FULL REPORT");
                    formatted.AppendLine("─────────────────────────────────────────────────────────────────────────────");
                    string fullReport = responseData["full_report"].ToString();
                    if (!string.IsNullOrEmpty(fullReport))
                    {
                        formatted.AppendLine(fullReport);
                    }
                    formatted.AppendLine();
                }

                // Metadata Section
                formatted.AppendLine("📅 METADATA");
                formatted.AppendLine("─────────────────────────────────────────────────────────────────────────────");
                formatted.AppendLine($"Analysis Timestamp: {GetValue(responseData, "analysis_timestamp")}");
                formatted.AppendLine($"Report Path: {GetValue(responseData, "report_path")}");
                formatted.AppendLine();

                formatted.AppendLine("================================================================================");
            }
            catch (System.Exception ex)
            {
                formatted.AppendLine($"Error formatting response: {ex.Message}");
                formatted.AppendLine($"\nRaw response:\n{jsonResponse}");
            }

            return formatted.ToString();
        }

        private Dictionary<string, object> ParseJson(string json)
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

        private string GetValue(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key))
                return "(not available)";

            object value = dict[key];
            return value == null ? "(null)" : value.ToString();
        }

        private void ShowScrollableReport(string reportContent)
        {
            try
            {
                // Use reflection to load WinForms dynamically
                Type formType = Type.GetType("System.Windows.Forms.Form, System.Windows.Forms");
                Type richTextBoxType = Type.GetType("System.Windows.Forms.RichTextBox, System.Windows.Forms");
                Type dockStyleType = Type.GetType("System.Windows.Forms.DockStyle, System.Windows.Forms");
                Type startPosType = Type.GetType("System.Windows.Forms.FormStartPosition, System.Windows.Forms");

                if (formType == null || richTextBoxType == null)
                {
                    // Fallback: Save to file and open in notepad
                    string reportPath = Path.Combine(Path.GetTempPath(), "drawing_analysis_report.txt");
                    File.WriteAllText(reportPath, reportContent);
                    System.Diagnostics.Process.Start(reportPath);
                    return;
                }

                dynamic form = Activator.CreateInstance(formType);
                form.Text = "Drawing Analysis Report";
                form.Width = 900;
                form.Height = 700;
                form.StartPosition = Enum.Parse(startPosType, "CenterScreen");

                dynamic textBox = Activator.CreateInstance(richTextBoxType);
                textBox.Dock = Enum.Parse(dockStyleType, "Fill");
                textBox.ReadOnly = true;
                textBox.Text = reportContent;

                form.Controls.Add(textBox);
                form.ShowDialog();
            }
            catch
            {
                // Fallback: Save to file and open
                string reportPath = Path.Combine(Path.GetTempPath(), "drawing_analysis_report.txt");
                File.WriteAllText(reportPath, reportContent);
                System.Diagnostics.Process.Start(reportPath);
            }
        }
    }
}
