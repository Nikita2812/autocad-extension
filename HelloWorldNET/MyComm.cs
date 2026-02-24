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
                PromptStringOptions psoProjectId = new PromptStringOptions("\nEnter Project ID (press Enter to skip): ");
                psoProjectId.AllowSpaces = true;
                PromptResult prProjectId = ed.GetString(psoProjectId);

                if (prProjectId.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nOperation cancelled.");
                    return;
                }

                string projectId = prProjectId.StringResult;

                // Prompt for participant_id
                PromptStringOptions psoParticipantId = new PromptStringOptions("\nEnter Participant ID (press Enter for default): ");
                psoParticipantId.AllowSpaces = true;
                PromptResult prParticipantId = ed.GetString(psoParticipantId);

                if (prParticipantId.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nOperation cancelled.");
                    return;
                }

                string participantId = string.IsNullOrWhiteSpace(prParticipantId.StringResult) 
                    ? "852821f6-1214-4dae-a35f-0c5a4df09555" 
                    : prParticipantId.StringResult;

                string drawingName = Path.GetFileNameWithoutExtension(doc.Name);

                // Call the API endpoint
                ed.WriteMessage("\nSending to API endpoint...");
                Task.Run(async () =>
                {
                    try
                    {
                        string response = await CallReviewEndpoint(outputPath, drawingName, projectId, participantId);
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

        private async Task<string> CallReviewEndpoint(string jsonFilePath, string drawingName, string projectId, string participantId)
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
                    form.Add(new StringContent(participantId), "participant_id");

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
            StringBuilder html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine("    <title>Drawing Analysis Report</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        * { margin: 0; padding: 0; box-sizing: border-box; }");
            html.AppendLine("        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: #333; padding: 20px; }");
            html.AppendLine("        .container { max-width: 1200px; margin: 0 auto; background: white; border-radius: 10px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); overflow: hidden; }");
            html.AppendLine("        .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 40px; text-align: center; }");
            html.AppendLine("        .header h1 { font-size: 2.5em; margin-bottom: 10px; }");
            html.AppendLine("        .content { padding: 40px; }");
            html.AppendLine("        .section { margin-bottom: 40px; }");
            html.AppendLine("        .section-title { font-size: 1.8em; color: #667eea; margin-bottom: 20px; padding-bottom: 10px; border-bottom: 3px solid #667eea; display: flex; align-items: center; gap: 10px; }");
            html.AppendLine("        .section-content { margin-left: 20px; }");
            html.AppendLine("        .status-box { background: #f8f9ff; border-left: 4px solid #667eea; padding: 15px 20px; margin-bottom: 15px; border-radius: 5px; }");
            html.AppendLine("        .status-box strong { color: #667eea; }");
            html.AppendLine("        .success-badge { display: inline-block; background: #4caf50; color: white; padding: 5px 12px; border-radius: 20px; font-size: 0.9em; }");
            html.AppendLine("        .error-badge { display: inline-block; background: #f44336; color: white; padding: 5px 12px; border-radius: 20px; font-size: 0.9em; }");
            html.AppendLine("        .item-list { list-style: none; }");
            html.AppendLine("        .item-list li { padding: 10px; background: #f5f5f5; margin-bottom: 8px; border-left: 3px solid #667eea; border-radius: 3px; }");
            html.AppendLine("        .item-list li strong { color: #667eea; }");
            html.AppendLine("        .no-conflicts { background: #e8f5e9; color: #2e7d32; padding: 15px; border-radius: 5px; text-align: center; font-weight: bold; }");
            html.AppendLine("        .conflict-warning { background: #fff3cd; color: #856404; padding: 15px; border-radius: 5px; margin-bottom: 15px; }");
            html.AppendLine("        table { width: 100%; border-collapse: collapse; margin: 15px 0; }");
            html.AppendLine("        th { background: #667eea; color: white; padding: 12px; text-align: left; }");
            html.AppendLine("        td { border-bottom: 1px solid #ddd; padding: 12px; }");
            html.AppendLine("        tr:hover { background: #f5f5f5; }");
            html.AppendLine("        .text-content { background: #f9f9f9; border: 1px solid #ddd; padding: 15px; border-radius: 5px; white-space: pre-wrap; word-wrap: break-word; line-height: 1.6; }");
            html.AppendLine("        .metadata { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }");
            html.AppendLine("        .metadata-item { background: #f5f5f5; padding: 15px; border-radius: 5px; }");
            html.AppendLine("        .metadata-item strong { color: #667eea; display: block; margin-bottom: 5px; }");
            html.AppendLine("        .footer { background: #f5f5f5; padding: 20px; text-align: center; color: #999; font-size: 0.9em; border-top: 1px solid #ddd; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("    <div class=\"container\">");
            html.AppendLine("        <div class=\"header\">");
            html.AppendLine("            <h1>📊 Drawing Analysis Report</h1>");
            html.AppendLine("            <p>Automated Structural Design Review</p>");
            html.AppendLine("        </div>");
            html.AppendLine("        <div class=\"content\">");

            try
            {
                // Parse JSON manually for .NET 4.8 compatibility
                Dictionary<string, object> responseData = ParseJson(jsonResponse);

                if (responseData.Count == 0)
                {
                    html.AppendLine("            <div class=\"error-badge\">Error: Could not parse API response</div>");
                    html.AppendLine("        </div>");
                    html.AppendLine("    </div>");
                    html.AppendLine("</body>");
                    html.AppendLine("</html>");
                    return html.ToString();
                }

                // Status Section
                html.AppendLine("            <div class=\"section\">");
                html.AppendLine("                <div class=\"section-title\">📋 Status Information</div>");
                html.AppendLine("                <div class=\"section-content\">");

                string successValue = GetValue(responseData, "success");
                bool isSuccess = successValue.ToLower() == "true";
                html.AppendLine($"                    <div class=\"status-box\"><strong>Status:</strong> <span class=\"{(isSuccess ? "success-badge" : "error-badge")}\">{(isSuccess ? "SUCCESS" : "FAILED")}</span></div>");
                html.AppendLine($"                    <div class=\"status-box\"><strong>Message:</strong> {GetValue(responseData, "message")}</div>");
                html.AppendLine($"                    <div class=\"status-box\"><strong>Drawing Name:</strong> {GetValue(responseData, "drawing_name")}</div>");

                if (responseData.ContainsKey("error") && responseData["error"] != null && !string.IsNullOrEmpty(responseData["error"].ToString()))
                {
                    html.AppendLine($"                    <div class=\"status-box\" style=\"border-left-color: #f44336;\"><strong style=\"color: #f44336;\">Error:</strong> {GetValue(responseData, "error")}</div>");
                }

                html.AppendLine("                </div>");
                html.AppendLine("            </div>");

                // Geometric Analysis Section
                if (responseData.ContainsKey("geometric_analysis") && responseData["geometric_analysis"] is Dictionary<string, object> geoAnalysis && geoAnalysis != null)
                {
                    html.AppendLine("            <div class=\"section\">");
                    html.AppendLine("                <div class=\"section-title\">📐 Geometric Analysis</div>");
                    html.AppendLine("                <div class=\"section-content\">");
                    html.AppendLine($"                    <div class=\"status-box\"><strong>Total Elements:</strong> {GetValue(geoAnalysis, "total_elements")}</div>");

                    // Element Types
                    if (geoAnalysis.ContainsKey("element_types") && geoAnalysis["element_types"] is Dictionary<string, object> elementTypes && elementTypes != null && elementTypes.Count > 0)
                    {
                        html.AppendLine("                    <h3 style=\"color: #667eea; margin-top: 20px; margin-bottom: 10px;\">Element Types</h3>");
                        html.AppendLine("                    <ul class=\"item-list\">");
                        foreach (var kvp in elementTypes)
                        {
                            html.AppendLine($"                        <li><strong>{kvp.Key}:</strong> {kvp.Value}</li>");
                        }
                        html.AppendLine("                    </ul>");
                    }

                    // Structural Layers
                    if (geoAnalysis.ContainsKey("structural_layers") && geoAnalysis["structural_layers"] is Dictionary<string, object> layers && layers != null && layers.Count > 0)
                    {
                        html.AppendLine("                    <h3 style=\"color: #667eea; margin-top: 20px; margin-bottom: 10px;\">Structural Layers</h3>");
                        html.AppendLine("                    <ul class=\"item-list\">");
                        foreach (var kvp in layers)
                        {
                            html.AppendLine($"                        <li><strong>{kvp.Key}:</strong> {kvp.Value}</li>");
                        }
                        html.AppendLine("                    </ul>");
                    }

                    // Beam Types
                    if (geoAnalysis.ContainsKey("beam_types") && geoAnalysis["beam_types"] is Dictionary<string, object> beamTypes && beamTypes != null)
                    {
                        html.AppendLine("                    <h3 style=\"color: #667eea; margin-top: 20px; margin-bottom: 10px;\">Beam Types</h3>");
                        if (beamTypes.Count > 0)
                        {
                            html.AppendLine("                    <ul class=\"item-list\">");
                            foreach (var kvp in beamTypes)
                            {
                                html.AppendLine($"                        <li>{kvp.Key}</li>");
                            }
                            html.AppendLine("                    </ul>");
                        }
                        else
                        {
                            html.AppendLine("                    <p style=\"color: #999; font-style: italic;\">(No beam types detected)</p>");
                        }
                    }

                    // Bolt Holes
                    if (geoAnalysis.ContainsKey("bolt_holes") && geoAnalysis["bolt_holes"] is Dictionary<string, object> boltHoles && boltHoles != null)
                    {
                        html.AppendLine("                    <h3 style=\"color: #667eea; margin-top: 20px; margin-bottom: 10px;\">Bolt Holes</h3>");
                        if (boltHoles.Count > 0)
                        {
                            html.AppendLine("                    <ul class=\"item-list\">");
                            foreach (var kvp in boltHoles)
                            {
                                html.AppendLine($"                        <li>{kvp.Key}</li>");
                            }
                            html.AppendLine("                    </ul>");
                        }
                        else
                        {
                            html.AppendLine("                    <p style=\"color: #999; font-style: italic;\">(No bolt holes detected)</p>");
                        }
                    }

                    // Connection Angles
                    if (geoAnalysis.ContainsKey("connection_angles") && geoAnalysis["connection_angles"] is List<object> angles && angles != null && angles.Count > 0)
                    {
                        html.AppendLine("                    <h3 style=\"color: #667eea; margin-top: 20px; margin-bottom: 10px;\">Connection Angles</h3>");
                        html.AppendLine("                    <ul class=\"item-list\">");
                        for (int i = 0; i < angles.Count; i++)
                        {
                            html.AppendLine($"                        <li>Connection {i + 1}</li>");
                        }
                        html.AppendLine("                    </ul>");
                    }

                    // Spatial Conflicts
                    if (geoAnalysis.ContainsKey("spatial_conflicts") && geoAnalysis["spatial_conflicts"] is List<object> conflicts && conflicts != null)
                    {
                        html.AppendLine("                    <h3 style=\"color: #f44336; margin-top: 20px; margin-bottom: 10px;\">⚠️ Spatial Conflicts</h3>");
                        if (conflicts.Count > 0)
                        {
                            html.AppendLine("                    <div class=\"conflict-warning\">");
                            html.AppendLine($"                        <strong>{conflicts.Count} conflict(s) detected</strong>");
                            html.AppendLine("                    </div>");
                            html.AppendLine("                    <ul class=\"item-list\">");
                            for (int i = 0; i < conflicts.Count; i++)
                            {
                                html.AppendLine($"                        <li>Conflict {i + 1}</li>");
                            }
                            html.AppendLine("                    </ul>");
                        }
                        else
                        {
                            html.AppendLine("                    <div class=\"no-conflicts\">✓ No spatial conflicts detected</div>");
                        }
                    }

                    html.AppendLine("                </div>");
                    html.AppendLine("            </div>");
                }

                // Token Usage Section
                if (responseData.ContainsKey("token_usage") && responseData["token_usage"] is Dictionary<string, object> tokens && tokens != null)
                {
                    html.AppendLine("            <div class=\"section\">");
                    html.AppendLine("                <div class=\"section-title\">🔧 Token Usage</div>");
                    html.AppendLine("                <div class=\"section-content\">");
                    html.AppendLine("                    <table>");
                    html.AppendLine("                        <tr>");
                    html.AppendLine("                            <th>Metric</th>");
                    html.AppendLine("                            <th>Value</th>");
                    html.AppendLine("                        </tr>");
                    html.AppendLine($"                        <tr><td>Prompt Tokens</td><td>{GetValue(tokens, "prompt_tokens")}</td></tr>");
                    html.AppendLine($"                        <tr><td>Completion Tokens</td><td>{GetValue(tokens, "completion_tokens")}</td></tr>");
                    html.AppendLine($"                        <tr><td>Total Tokens</td><td>{GetValue(tokens, "total_tokens")}</td></tr>");
                    html.AppendLine("                    </table>");
                    html.AppendLine("                </div>");
                    html.AppendLine("            </div>");
                }

                // LLM Review Section
                if (responseData.ContainsKey("llm_review") && responseData["llm_review"] != null)
                {
                    string llmReview = responseData["llm_review"].ToString();
                    if (!string.IsNullOrEmpty(llmReview))
                    {
                        html.AppendLine("            <div class=\"section\">");
                        html.AppendLine("                <div class=\"section-title\">🤖 LLM Review</div>");
                        html.AppendLine("                <div class=\"section-content\">");
                        string htmlContent = ConvertMarkdownToHtml(llmReview);
                        html.AppendLine($"                    <div style=\"background: #f9f9f9; border: 1px solid #ddd; padding: 15px; border-radius: 5px; line-height: 1.6;\">{htmlContent}</div>");
                        html.AppendLine("                </div>");
                        html.AppendLine("            </div>");
                    }
                }

                // Full Report Section
                if (responseData.ContainsKey("full_report") && responseData["full_report"] != null)
                {
                    string fullReport = responseData["full_report"].ToString();
                    if (!string.IsNullOrEmpty(fullReport))
                    {
                        html.AppendLine("            <div class=\"section\">");
                        html.AppendLine("                <div class=\"section-title\">📄 Full Report</div>");
                        html.AppendLine("                <div class=\"section-content\">");
                        string htmlContent = ConvertMarkdownToHtml(fullReport);
                        html.AppendLine($"                    <div style=\"background: #f9f9f9; border: 1px solid #ddd; padding: 15px; border-radius: 5px; line-height: 1.6;\">{htmlContent}</div>");
                        html.AppendLine("                </div>");
                        html.AppendLine("            </div>");
                    }
                }

                // Metadata Section
                html.AppendLine("            <div class=\"section\">");
                html.AppendLine("                <div class=\"section-title\">📅 Metadata</div>");
                html.AppendLine("                <div class=\"section-content\">");
                html.AppendLine("                    <div class=\"metadata\">");
                html.AppendLine($"                        <div class=\"metadata-item\"><strong>Analysis Timestamp</strong>{GetValue(responseData, "analysis_timestamp")}</div>");
                html.AppendLine($"                        <div class=\"metadata-item\"><strong>Report Path</strong>{GetValue(responseData, "report_path")}</div>");
                html.AppendLine("                    </div>");
                html.AppendLine("                </div>");
                html.AppendLine("            </div>");
            }
            catch (System.Exception ex)
            {
                html.AppendLine($"            <div class=\"conflict-warning\"><strong>Error formatting response:</strong> {ex.Message}</div>");
            }

            html.AppendLine("        </div>");
            html.AppendLine("        <div class=\"footer\">");
            html.AppendLine("            <p>Generated by AutoCAD Drawing Analysis Extension</p>");
            html.AppendLine("        </div>");
            html.AppendLine("    </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
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

        private string ConvertMarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return "";

            // Handle escaped newlines in the markdown string
            markdown = markdown.Replace("\\n", "\n").Replace("\\r", "\r");

            StringBuilder html = new StringBuilder();
            string[] lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool inCodeBlock = false;
            bool inList = false;
            bool inTable = false;

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                string processedLine = line;

                // Check for code blocks
                if (processedLine.Trim().StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;
                    if (inCodeBlock)
                        html.Append("<pre style=\"background: #f5f5f5; border: 1px solid #ddd; padding: 10px; border-radius: 3px; overflow-x: auto;\"><code>");
                    else
                        html.Append("</code></pre>");
                    continue;
                }

                if (inCodeBlock)
                {
                    html.AppendLine(System.Net.WebUtility.HtmlEncode(processedLine) + "<br>");
                    continue;
                }

                // Check for markdown table
                if (IsTableRow(processedLine))
                {
                    if (!inTable)
                    {
                        if (inList)
                        {
                            html.Append("</ul>");
                            inList = false;
                        }
                        html.AppendLine("<table style=\"width:100%; border-collapse: collapse; margin: 15px 0;\">");
                        inTable = true;

                        // Process header row
                        string[] headerCells = ParseTableRow(processedLine);
                        html.Append("<tr>");
                        foreach (string cell in headerCells)
                        {
                            html.Append($"<th style=\"background: #667eea; color: white; padding: 12px; text-align: left; border: 1px solid #ddd;\">{ApplyInlineFormatting(cell)}</th>");
                        }
                        html.AppendLine("</tr>");

                        // Skip separator row if next line is separator
                        if (lineIndex + 1 < lines.Length && IsSeparatorRow(lines[lineIndex + 1]))
                        {
                            lineIndex++;
                        }
                    }
                    else
                    {
                        // Process data row
                        string[] dataCells = ParseTableRow(processedLine);
                        html.Append("<tr>");
                        foreach (string cell in dataCells)
                        {
                            html.Append($"<td style=\"border-bottom: 1px solid #ddd; padding: 12px;\">{ApplyInlineFormatting(cell)}</td>");
                        }
                        html.AppendLine("</tr>");
                    }
                    continue;
                }
                else if (inTable)
                {
                    html.AppendLine("</table>");
                    inTable = false;
                }

                // Check for headers
                string headerProcessed = null;
                if (processedLine.StartsWith("######"))
                    headerProcessed = "<h6 style=\"color: #667eea; margin-top: 15px; margin-bottom: 10px;\">" + ApplyInlineFormatting(processedLine.Substring(6).Trim()) + "</h6>";
                else if (processedLine.StartsWith("#####"))
                    headerProcessed = "<h5 style=\"color: #667eea; margin-top: 15px; margin-bottom: 10px;\">" + ApplyInlineFormatting(processedLine.Substring(5).Trim()) + "</h5>";
                else if (processedLine.StartsWith("####"))
                    headerProcessed = "<h4 style=\"color: #667eea; margin-top: 15px; margin-bottom: 10px;\">" + ApplyInlineFormatting(processedLine.Substring(4).Trim()) + "</h4>";
                else if (processedLine.StartsWith("###"))
                    headerProcessed = "<h3 style=\"color: #667eea; margin-top: 15px; margin-bottom: 10px;\">" + ApplyInlineFormatting(processedLine.Substring(3).Trim()) + "</h3>";
                else if (processedLine.StartsWith("##"))
                    headerProcessed = "<h2 style=\"color: #667eea; margin-top: 15px; margin-bottom: 10px;\">" + ApplyInlineFormatting(processedLine.Substring(2).Trim()) + "</h2>";
                else if (processedLine.StartsWith("#"))
                    headerProcessed = "<h1 style=\"color: #667eea; margin-top: 15px; margin-bottom: 10px;\">" + ApplyInlineFormatting(processedLine.Substring(1).Trim()) + "</h1>";

                if (headerProcessed != null)
                {
                    if (inList)
                    {
                        html.Append("</ul>");
                        inList = false;
                    }
                    html.AppendLine(headerProcessed);
                    continue;
                }

                // Check for list items
                if (processedLine.Trim().StartsWith("-") || processedLine.Trim().StartsWith("*"))
                {
                    if (!inList)
                    {
                        html.Append("<ul style=\"margin-left: 20px;\">");
                        inList = true;
                    }
                    string listItem = ApplyInlineFormatting(processedLine.Trim().Substring(1).Trim());
                    html.AppendLine($"<li style=\"margin-bottom: 5px;\">{listItem}</li>");
                }
                else
                {
                    if (inList)
                    {
                        html.Append("</ul>");
                        inList = false;
                    }

                    // Paragraph
                    if (!string.IsNullOrWhiteSpace(processedLine))
                    {
                        string formattedLine = ApplyInlineFormatting(processedLine);
                        html.AppendLine($"<p style=\"margin-bottom: 10px;\">{formattedLine}</p>");
                    }
                    else
                    {
                        html.AppendLine("<br>");
                    }
                }
            }

            if (inList)
                html.Append("</ul>");
            if (inTable)
                html.Append("</table>");

            return html.ToString();
        }

        private bool IsTableRow(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string trimmed = line.Trim();
            return trimmed.StartsWith("|") && trimmed.EndsWith("|") && trimmed.Contains("|");
        }

        private bool IsSeparatorRow(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string trimmed = line.Trim();
            if (!trimmed.StartsWith("|") || !trimmed.EndsWith("|"))
                return false;

            string[] cells = trimmed.Split('|');
            foreach (string cell in cells)
            {
                string cellTrimmed = cell.Trim();
                if (string.IsNullOrEmpty(cellTrimmed))
                    continue;

                // Check if cell contains only dashes and colons (for alignment)
                if (!System.Text.RegularExpressions.Regex.IsMatch(cellTrimmed, @"^:?-+:?$"))
                    return false;
            }
            return true;
        }

        private string[] ParseTableRow(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return new string[] { };

            string trimmed = line.Trim();
            if (trimmed.StartsWith("|"))
                trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("|"))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            string[] cells = trimmed.Split('|');
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = cells[i].Trim();
            }
            return cells;
        }

        private string ApplyInlineFormatting(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Bold - **text**
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");

            // Italic - *text* (but avoid matching ** that was just processed)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<!\*)\*([^\*]+)\*(?!\*)", "<em>$1</em>");

            // Inline code - `text`
            text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]+)`", "<code style=\"background: #f0f0f0; padding: 2px 5px; border-radius: 3px;\">$1</code>");

            // Links - [text](url)
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[(.+?)\]\((.+?)\)", "<a href=\"$2\" style=\"color: #667eea; text-decoration: none;\">$1</a>");

            return text;
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
                // Save HTML report to file
                string reportPath = Path.Combine(Path.GetTempPath(), $"drawing_analysis_report_{DateTime.Now:yyyy_MM_dd_HH_mm_ss}.html");
                File.WriteAllText(reportPath, reportContent, Encoding.UTF8);

                // Open in default browser
                System.Diagnostics.Process.Start(reportPath);
            }
            catch
            {
                try
                {
                    // Fallback: Try to open with explicit browser call
                    string reportPath = Path.Combine(Path.GetTempPath(), $"drawing_analysis_report_{DateTime.Now:yyyy_MM_dd_HH_mm_ss}.html");
                    File.WriteAllText(reportPath, reportContent, Encoding.UTF8);
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(reportPath) { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (System.Exception ex)
                {
                    Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog($"Could not open report: {ex.Message}");
                }
            }
        }
    }
}
