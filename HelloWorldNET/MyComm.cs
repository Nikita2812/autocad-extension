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
                        else if (entity is DBText text)
                        {
                            entityData["Content"] = text.TextString;
                            entityData["Position"] = new { X = text.Position.X, Y = text.Position.Y, Z = text.Position.Z };
                            entityData["Height"] = text.Height;
                            entityData["Rotation"] = text.Rotation;

                            // Add semantic metadata
                            var metadata = ExtractDBTextMetadata(text, tr);
                            if (metadata.Count > 0)
                                entityData["metadata"] = metadata;
                        }
                        else if (entity is BlockReference blockRef)
                        {
                            entityData["Position"] = new { X = blockRef.Position.X, Y = blockRef.Position.Y, Z = blockRef.Position.Z };
                            // CRITICAL FIX: Translate anonymous/dynamic block names using fingerprinting
                            string translatedBlockName = GetTranslatedBlockName(blockRef, tr);
                            entityData["BlockName"] = translatedBlockName;
                            entityData["Rotation"] = blockRef.Rotation;
                            entityData["ScaleX"] = blockRef.ScaleFactors.X;
                            entityData["ScaleY"] = blockRef.ScaleFactors.Y;
                            entityData["ScaleZ"] = blockRef.ScaleFactors.Z;

                            // Extract attributes
                            List<Dictionary<string, object>> attributes = new List<Dictionary<string, object>>();
                            foreach (ObjectId attId in blockRef.AttributeCollection)
                            {
                                try
                                {
                                    AttributeReference attRef = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                                    attributes.Add(new Dictionary<string, object>
                                    {
                                        { "Tag", attRef.Tag },
                                        { "Value", attRef.TextString }
                                    });
                                }
                                catch { }
                            }
                            if (attributes.Count > 0)
                                entityData["Attributes"] = attributes;

                            // Add semantic metadata
                            var blockMetadata = ExtractBlockReferenceMetadata(blockRef, tr);
                            if (blockMetadata.Count > 0)
                                entityData["metadata"] = blockMetadata;
                        }
                        else if (entity is MText mtext)
                        {
                            // Extract clean text (mtext.Text gives unformatted content without AutoCAD codes)
                            string cleanText = "";
                            try { cleanText = mtext.Text ?? ""; }
                            catch { cleanText = ""; }

                            entityData["Content"] = cleanText;

                            entityData["Position"] = new { X = mtext.Location.X, Y = mtext.Location.Y, Z = mtext.Location.Z };
                            entityData["Height"] = mtext.Height;

                            // Add semantic metadata
                            var metadata = ExtractMTextMetadata(mtext, tr);
                            if (metadata.Count > 0)
                                entityData["metadata"] = metadata;
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

                ConfigManager configMgr = ConfigManager.GetInstance();
                string participantId = string.IsNullOrWhiteSpace(prParticipantId.StringResult) 
                    ? configMgr.GetDefaultParticipantId()
                    : prParticipantId.StringResult;

                string drawingName = Path.GetFileNameWithoutExtension(doc.Name);

                // Call the API endpoint
                ed.WriteMessage("\nSending to API endpoint...");
                ConfigManager configMgr2 = ConfigManager.GetInstance();
                Task.Run(async () =>
                {
                    try
                    {
                        ed.WriteMessage($"\nCalling: {configMgr2.GetApiEndpoint()}");
                        string response = await CallReviewEndpoint(outputPath, drawingName, projectId, participantId, null);
                        ed.WriteMessage("\nAPI response received. Formatting report...");
                        string formattedResponse = FormatApiResponse(response);
                        ed.WriteMessage("\nOpening report in browser...");
                        ShowScrollableReport(formattedResponse);
                        ed.WriteMessage("\nReport opened successfully.");
                    }
                    catch (System.Exception apiEx)
                    {
                        ed.WriteMessage($"\nAPI Error: {apiEx.Message}\nStackTrace: {apiEx.StackTrace}");
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

        private async Task<string> CallReviewEndpoint(
            string jsonFilePath,
            string drawingName,
            string projectId,
            string participantId,
            string apiUrl = null)
        {
            ConfigManager configMgr = ConfigManager.GetInstance();
            string endpoint = string.IsNullOrWhiteSpace(apiUrl)
                ? configMgr.GetApiEndpoint()
                : apiUrl;
            string model = configMgr.GetApiModel();
            int timeoutSeconds = configMgr.GetApiTimeoutSeconds();
            bool saveReport = configMgr.GetApiSaveReport();

            using (HttpClient client = new HttpClient())
            {
                client.Timeout = System.TimeSpan.FromSeconds(timeoutSeconds);
                using (MultipartFormDataContent form = new MultipartFormDataContent())
                {
                    // Add JSON file
                    byte[] fileContent = File.ReadAllBytes(jsonFilePath);
                    ByteArrayContent fileStream = new ByteArrayContent(fileContent);
                    fileStream.Headers.Add("Content-Type", "application/json");
                    form.Add(fileStream, "json_file", Path.GetFileName(jsonFilePath));

                    // Add other form fields
                    form.Add(new StringContent(drawingName ?? ""), "drawing_name");
                    form.Add(new StringContent(projectId ?? ""), "project_id");
                    form.Add(new StringContent(model), "model");
                    form.Add(new StringContent(saveReport.ToString().ToLower()), "save_report");
                    form.Add(new StringContent(participantId ?? ""), "participant_id");

                    // Send POST request
                    HttpResponseMessage response = await client.PostAsync(endpoint, form);

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
                return $"\"{EscapeJsonString((string)value)}\"";
            else if (value is bool)
                return value.ToString().ToLower();
            else if (value is Dictionary<string, object> dict)
                return SerializeDictionary(dict);
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

        private string SerializeDictionary(Dictionary<string, object> dict)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");

            var keys = dict.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                object val = dict[key];
                sb.Append($"\"{EscapeJsonString(key)}\": {GetJsonValue(val)}");
                if (i < keys.Count - 1) sb.Append(", ");
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

        // ─────────────────────────────────────────────────────────────────────
        // EXTRACT ONLY (bridge-driven) command
        // Reads drawing path and uuid from a shared temp JSON file,
        // extracts entities to JSON, and writes the JSON file path back to
        // a shared temp JSON file that the Python bridge polls for.
        //
        // Request file: %TEMP%\ai_review\extract_request.json
        // Result file : %TEMP%\ai_review\extract_result.json
        // ─────────────────────────────────────────────────────────────────────

        private static readonly string ExtractRequestPath =
            Path.Combine(Path.GetTempPath(), "ai_review", "extract_request.json");

        private static readonly string ExtractResultPath =
            Path.Combine(Path.GetTempPath(), "ai_review", "extract_result.json");

        [CommandMethod("extractJSONOnly")]
        public void ExtractDrawingJsonOnly()
        {
            string logPath = null;
            Document doc = null;
            Editor ed = null;

            try
            {
                // Try to get the active document for user feedback
                try
                {
                    doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                        ed = doc.Editor;
                }
                catch { }

                // ── 0. Initialize logging ────────────────────────────────────
                logPath = Path.Combine(Path.GetTempPath(), "ai_review", $"extract_log_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss_fff}.txt");
                string logDir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                string startMsg = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Starting extractJSONOnly command";
                LogToFile(logPath, startMsg);
                if (ed != null) ed.WriteMessage($"\n{startMsg}");

                // ── 1. Read request config written by Python bridge ──────────
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Checking for request file: {ExtractRequestPath}");
                if (ed != null) ed.WriteMessage($"\nChecking for request file: {ExtractRequestPath}");

                if (!File.Exists(ExtractRequestPath))
                {
                    string errMsg = "Request config file not found.";
                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] ERROR: {errMsg}");
                    if (ed != null) ed.WriteMessage($"\nERROR: {errMsg}");
                    if (ed != null) ed.WriteMessage($"\nLog: {logPath}");
                    if (ed != null) ed.WriteMessage($"\nResult: {ExtractResultPath}");
                    WriteExtractResult(false, errMsg, null, null);
                    return;
                }

                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Reading request configuration from: {ExtractRequestPath}");
                string configJson = File.ReadAllText(ExtractRequestPath).Trim();
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Request config loaded ({configJson.Length} characters)");
                if (ed != null) ed.WriteMessage($"\nRequest config loaded ({configJson.Length} characters)");

                Dictionary<string, object> config = ParseJsonObject(configJson);
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Configuration parsed successfully");
                if (ed != null) ed.WriteMessage($"\nConfiguration parsed successfully");

                string drawingPath = GetSilentConfigValue(config, "drawing_path", null);
                string uuid = GetSilentConfigValue(config, "uuid", null);
                string department = GetSilentConfigValue(config, "department", null);

                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Drawing path: {drawingPath}");
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] UUID: {(string.IsNullOrEmpty(uuid) ? "(none)" : uuid)}");
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Department: {(string.IsNullOrEmpty(department) ? "(auto-detect from layer)" : department)}");
                if (ed != null) ed.WriteMessage($"\nDrawing: {drawingPath}");
                if (ed != null) ed.WriteMessage($"\nDepartment: {(string.IsNullOrEmpty(department) ? "(auto-detect from layer)" : department)}");

                if (string.IsNullOrWhiteSpace(drawingPath))
                {
                    string errMsg = "drawing_path not provided in request.";
                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] ERROR: {errMsg}");
                    if (ed != null) ed.WriteMessage($"\nERROR: {errMsg}");
                    WriteExtractResult(false, errMsg, null, null);
                    return;
                }

                if (!File.Exists(drawingPath))
                {
                    string errMsg = $"Drawing file not found: {drawingPath}";
                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] ERROR: {errMsg}");
                    if (ed != null) ed.WriteMessage($"\nERROR: {errMsg}");
                    WriteExtractResult(false, errMsg, null, null);
                    return;
                }

                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Drawing file verified, size: {new FileInfo(drawingPath).Length} bytes");
                if (ed != null) ed.WriteMessage($"\nDrawing verified ({new FileInfo(drawingPath).Length} bytes)");

                // ── 2. Open the drawing and extract entities ─────────────────
                List<Dictionary<string, object>> entities = new List<Dictionary<string, object>>();

                try
                {
                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Opening drawing document...");
                    if (ed != null) ed.WriteMessage($"\nOpening drawing...");
                    Document openDoc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.Open(
                        drawingPath, false);
                    Database db = openDoc.Database;
                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Document opened successfully");
                    if (ed != null) ed.WriteMessage($"\nDocument opened successfully");

                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Starting entity extraction from ModelSpace...");
                    if (ed != null) ed.WriteMessage($"\nExtracting entities from ModelSpace...");

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                        LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Beginning semantic entity extraction with layer filtering...");

                        // Layer whitelisting/blacklisting for intelligent filtering
                        HashSet<string> allowedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "M_EQUIP", "M_PIPE", "M_INSTRUMENT", "C_GRID_CENT", "M_ANNO_TEXT", "TEXT", 
                            "PECS POWER FIXTURE", "M_LINE", "EQUIPMENT", "ANNOTATIONS"
                        };
                        HashSet<string> blockedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "0", "FURNITURE", "C_ARCH_DOOR", "TITLE", "DIMENSION", "HATCH", "DEFPOINTS", 
                            "SB COLUMN", "C_ARCH_WINDOW", "C_ARCH_WALL", "VIEWPORT", "XREF"
                        };

                        int entityCount = 0;
                        int filteredCount = 0;
                        foreach (ObjectId objId in btr)
                        {
                            try
                            {
                                Entity entity = (Entity)tr.GetObject(objId, OpenMode.ForRead);
                                string layerName = entity.Layer.ToUpper();

                                // HARD DROP FILTER: Block junk layers that confuse the LLM
                                if (blockedLayers.Contains(layerName))
                                {
                                    filteredCount++;
                                    continue;
                                }

                                // Initialize the semantic object structure that Python/LLM expects
                                Dictionary<string, object> semanticEntity = new Dictionary<string, object>
                                {
                                    { "layer", entity.Layer },
                                    { "metadata", new Dictionary<string, object>() }
                                };

                                var meta = (Dictionary<string, object>)semanticEntity["metadata"];

                                // --- TEXT CALLOUTS (DBText and MText) ---
                                if (entity is DBText dbText)
                                {
                                    semanticEntity["asset_type"] = "TextCallout";
                                    semanticEntity["position"] = new { X = dbText.Position.X, Y = dbText.Position.Y, Z = dbText.Position.Z };
                                    meta["content"] = dbText.TextString;
                                    meta["height"] = dbText.Height;
                                    meta["rotation"] = dbText.Rotation;

                                    entities.Add(semanticEntity);
                                }
                                else if (entity is MText mtext)
                                {
                                    semanticEntity["asset_type"] = "TextCallout";
                                    semanticEntity["position"] = new { X = mtext.Location.X, Y = mtext.Location.Y, Z = mtext.Location.Z };

                                    // Extract clean text (mtext.Text gives unformatted content without AutoCAD codes)
                                    string cleanText = "";
                                    try { cleanText = mtext.Text ?? ""; }
                                    catch { cleanText = ""; }

                                    meta["content"] = cleanText;
                                    meta["height"] = mtext.Height;
                                    meta["rotation"] = mtext.Rotation;

                                    entities.Add(semanticEntity);
                                }

                                // --- EQUIPMENT BLOCKS ---
                                else if (entity is BlockReference blockRef)
                                {
                                    semanticEntity["asset_type"] = "Equipment";
                                    semanticEntity["position"] = new { X = blockRef.Position.X, Y = blockRef.Position.Y, Z = blockRef.Position.Z };
                                    meta["block_name"] = GetTranslatedBlockName(blockRef, tr);
                                    meta["rotation"] = blockRef.Rotation;
                                    meta["scale_x"] = blockRef.ScaleFactors.X;
                                    meta["scale_y"] = blockRef.ScaleFactors.Y;
                                    meta["scale_z"] = blockRef.ScaleFactors.Z;

                                    // CRITICAL: Extract block attributes directly into metadata (unified structure)
                                    foreach (ObjectId attId in blockRef.AttributeCollection)
                                    {
                                        try
                                        {
                                            AttributeReference attRef = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                                            meta[attRef.Tag] = attRef.TextString; // e.g., "TAG": "P-101A"
                                        }
                                        catch { }
                                    }

                                    entities.Add(semanticEntity);
                                    entityCount++;
                                }

                                // --- SMART PIPE/GRID SEGMENTS (Only on appropriate layers) ---
                                else if (entity is Polyline polyline)
                                {
                                    // Only classify as pipe if explicitly on a piping layer
                                    if (layerName.Contains("PIPE") || layerName.Contains("M_PIPE"))
                                    {
                                        semanticEntity["asset_type"] = "PipeSegment";
                                        meta["length_mm"] = polyline.Length;

                                        List<Dictionary<string, double>> points = new List<Dictionary<string, double>>();
                                        for (int i = 0; i < polyline.NumberOfVertices; i++)
                                        {
                                            Point3d pt = polyline.GetPoint3dAt(i);
                                            points.Add(new Dictionary<string, double> { { "X", pt.X }, { "Y", pt.Y }, { "Z", pt.Z } });
                                        }
                                        meta["vertices"] = points;

                                        entities.Add(semanticEntity);
                                        entityCount++;
                                    }
                                    else if (layerName.Contains("GRID") || layerName.Contains("C_GRID"))
                                    {
                                        semanticEntity["asset_type"] = "GridLine";
                                        meta["length_mm"] = polyline.Length;

                                        List<Dictionary<string, double>> points = new List<Dictionary<string, double>>();
                                        for (int i = 0; i < polyline.NumberOfVertices; i++)
                                        {
                                            Point3d pt = polyline.GetPoint3dAt(i);
                                            points.Add(new Dictionary<string, double> { { "X", pt.X }, { "Y", pt.Y }, { "Z", pt.Z } });
                                        }
                                        meta["vertices"] = points;

                                        entities.Add(semanticEntity);
                                    }
                                    else
                                    {
                                        // Not a pipe, not a grid. Drop it.
                                        filteredCount++;
                                        continue;
                                    }
                                }
                                // Drop standalone lines, arcs, circles that confuse the LLM
                                else
                                {
                                    filteredCount++;
                                    continue;
                                }

                                if (entityCount % 10 == 0)
                                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Extracted {entityCount} semantic entities (filtered {filteredCount})...");
                            }
                            catch (System.Exception entityEx)
                            {
                                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Warning: Failed to extract entity: {entityEx.Message}");
                            }
                        }

                        tr.Commit();
                        LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Transaction committed. Semantic entities: {entityCount}, Filtered out: {filteredCount}");
                        if (ed != null) ed.WriteMessage($"\nSemantic extraction: {entityCount} entities (filtered {filteredCount} junk layers)");
                    }

                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Closing document...");
                    openDoc.CloseAndDiscard();
                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Document closed");
                    if (ed != null) ed.WriteMessage($"\nDocument closed");
                }
                catch (System.Exception ex)
                {
                    string errMsg = $"Failed to extract from drawing: {ex.Message}";
                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] EXCEPTION: {errMsg}\nStackTrace: {ex.StackTrace}");
                    if (ed != null) ed.WriteMessage($"\nEXCEPTION: {errMsg}");
                    WriteExtractResult(false, errMsg, null, null);
                    return;
                }

                // ── 3. Convert to JSON ───────────────────────────────────────
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Converting {entities.Count} entities to JSON...");
                if (ed != null) ed.WriteMessage($"\nConverting to JSON...");
                string json = ConvertToJson(entities);
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] JSON conversion complete ({json.Length} characters)");
                if (ed != null) ed.WriteMessage($"\nJSON created ({json.Length} characters)");

                // ── 4. Save to output file ───────────────────────────────────
                string outputDir = Path.GetDirectoryName(drawingPath);
                string fileName = string.IsNullOrWhiteSpace(uuid) 
                    ? "drawing_data.json" 
                    : $"drawing_data_{uuid}.json";
                string outputPath = Path.Combine(outputDir, fileName);

                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Writing JSON to: {outputPath}");
                if (ed != null) ed.WriteMessage($"\nWriting JSON to: {outputPath}");
                File.WriteAllText(outputPath, json);
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] JSON file written successfully ({new FileInfo(outputPath).Length} bytes)");
                if (ed != null) ed.WriteMessage($"\nJSON file written ({new FileInfo(outputPath).Length} bytes)");

                // ── 5. Write success result with JSON path ───────────────────
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Writing extraction result to: {ExtractResultPath}");
                WriteExtractResult(true, $"Extracted {entities.Count} entities.", outputPath, json);
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Extraction completed successfully");
                if (ed != null) ed.WriteMessage($"\nExtraction completed successfully!");
                LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Log saved to: {logPath}");
                if (ed != null) ed.WriteMessage($"\nLog: {logPath}");
            }
            catch (System.Exception ex)
            {
                string errMsg = $"Extraction error: {ex.Message}";
                if (logPath != null)
                    LogToFile(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] FATAL EXCEPTION: {errMsg}\nStackTrace: {ex.StackTrace}");
                WriteExtractResult(false, errMsg, null, null);
                if (ed != null) ed.WriteMessage($"\nFATAL ERROR: {errMsg}");
                if (logPath != null && ed != null)
                    ed.WriteMessage($"\nLog: {logPath}\nResult: {ExtractResultPath}");
            }
        }

        /// <summary>Write progress message to a log file for debugging.</summary>
        private void LogToFile(string logPath, string message)
        {
            try
            {
                string logDir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                File.AppendAllText(logPath, message + Environment.NewLine);
                System.Diagnostics.Debug.WriteLine(message);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write to log: {message}");
            }
        }

        /// <summary>Write the extraction result to the shared temp result file.</summary>
        private void WriteExtractResult(bool success, string message, string jsonFilePath, string jsonContent)
        {
            try
            {
                string dirPath = Path.GetDirectoryName(ExtractResultPath);
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"success\": {success.ToString().ToLower()},");
                sb.AppendLine($"  \"message\": \"{EscapeJsonString(message)}\",");
                sb.AppendLine($"  \"timestamp\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\",");

                if (!string.IsNullOrEmpty(jsonFilePath))
                    sb.AppendLine($"  \"json_path\": \"{EscapeJsonString(jsonFilePath)}\",");
                else
                    sb.AppendLine("  \"json_path\": null,");

                if (!string.IsNullOrEmpty(jsonContent))
                    sb.AppendLine($"  \"json_content\": {jsonContent}");
                else
                    sb.AppendLine("  \"json_content\": null");

                sb.AppendLine("}");

                var utf8NoBom = new System.Text.UTF8Encoding(false);
                File.WriteAllText(ExtractResultPath, sb.ToString(), utf8NoBom);
            }
            catch (System.Exception ex)
            {
                // Silently fail to prevent recursion
                System.Diagnostics.Debug.WriteLine($"Error writing extract result: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // SILENT (bridge-driven) command
        // Reads config from a shared temp JSON file written by the Python bridge,
        // skips all interactive prompts, and writes the API result back to a
        // shared temp JSON file that the Python bridge polls for.
        //
        // Shared file paths must match the constants in bridge_server.py:
        //   Request : %TEMP%\ai_review\request.json
        //   Result  : %TEMP%\ai_review\result.json
        // ─────────────────────────────────────────────────────────────────────

        private static readonly string SilentRequestPath =
            Path.Combine(Path.GetTempPath(), "ai_review", "request.json");

        private static readonly string SilentResultPath =
            Path.Combine(Path.GetTempPath(), "ai_review", "result.json");

        [CommandMethod("extractJSONSilent")]
        public void ExtractDrawingJsonSilent()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                // ── 1. Read request config written by Python bridge ──────────
                if (!File.Exists(SilentRequestPath))
                {
                    ed.WriteMessage($"\nSilent review: request file not found: {SilentRequestPath}");
                    WriteSilentResult(false, "Request config file not found.", null);
                    return;
                }

                string configJson = File.ReadAllText(SilentRequestPath).Trim();
                Dictionary<string, object> config = ParseJsonObject(configJson);

                ConfigManager configMgr = ConfigManager.GetInstance();
                string apiUrl         = GetSilentConfigValue(config, "api_url",         null);
                string projectId      = GetSilentConfigValue(config, "project_id",      "");
                string participantId  = GetSilentConfigValue(config, "participant_id",  configMgr.GetDefaultParticipantId());
                string department     = GetSilentConfigValue(config, "department",      null);
                string model          = GetSilentConfigValue(config, "model",           configMgr.GetApiModel());
                string drawingNameCfg = GetSilentConfigValue(config, "drawing_name",    null);

                // ── 2. Extract entities with semantic structure and layer filtering ─────────
                List<Dictionary<string, object>> entities = new List<Dictionary<string, object>>();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    // Layer whitelisting/blacklisting for intelligent filtering
                    HashSet<string> allowedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "M_EQUIP", "M_PIPE", "M_INSTRUMENT", "C_GRID_CENT", "M_ANNO_TEXT", "TEXT", 
                        "PECS POWER FIXTURE", "M_LINE", "EQUIPMENT", "ANNOTATIONS"
                    };
                    HashSet<string> blockedLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "0", "FURNITURE", "C_ARCH_DOOR", "TITLE", "DIMENSION", "HATCH", "DEFPOINTS", 
                        "SB COLUMN", "C_ARCH_WINDOW", "C_ARCH_WALL", "VIEWPORT", "XREF"
                    };

                    int filteredOut = 0;
                    foreach (ObjectId objId in btr)
                    {
                        try
                        {
                            Entity entity = (Entity)tr.GetObject(objId, OpenMode.ForRead);
                            string layerName = entity.Layer.ToUpper();

                            // HARD DROP FILTER: Block junk layers that confuse the LLM
                            if (blockedLayers.Contains(layerName))
                            {
                                filteredOut++;
                                continue;
                            }

                            // Initialize the semantic object structure that Python/LLM expects
                            Dictionary<string, object> semanticEntity = new Dictionary<string, object>
                            {
                                { "layer", entity.Layer },
                                { "metadata", new Dictionary<string, object>() }
                            };

                            var meta = (Dictionary<string, object>)semanticEntity["metadata"];

                            // --- TEXT CALLOUTS (DBText and MText) ---
                            if (entity is DBText dbText)
                            {
                                semanticEntity["asset_type"] = "TextCallout";
                                semanticEntity["position"] = new { X = dbText.Position.X, Y = dbText.Position.Y, Z = dbText.Position.Z };
                                meta["content"] = dbText.TextString;
                                meta["height"] = dbText.Height;
                                meta["rotation"] = dbText.Rotation;

                                entities.Add(semanticEntity);
                            }
                            else if (entity is MText mtext)
                            {
                                semanticEntity["asset_type"] = "TextCallout";
                                semanticEntity["position"] = new { X = mtext.Location.X, Y = mtext.Location.Y, Z = mtext.Location.Z };

                                // Extract clean text (mtext.Text gives unformatted content without AutoCAD codes)
                                string cleanText = "";
                                try { cleanText = mtext.Text ?? ""; }
                                catch { cleanText = ""; }

                                meta["content"] = cleanText;
                                meta["height"] = mtext.Height;
                                meta["rotation"] = mtext.Rotation;

                                entities.Add(semanticEntity);
                            }

                            // --- EQUIPMENT BLOCKS ---
                            else if (entity is BlockReference blockRef)
                            {
                                semanticEntity["asset_type"] = "Equipment";
                                semanticEntity["position"] = new { X = blockRef.Position.X, Y = blockRef.Position.Y, Z = blockRef.Position.Z };

                                // This perfectly extracts "STRAINER", "PRESSURE_GAUGE", or the standard block name
                                meta["block_name"] = GetTranslatedBlockName(blockRef, tr);

                                meta["rotation"] = blockRef.Rotation;
                                meta["scale_x"] = blockRef.ScaleFactors.X;
                                meta["scale_y"] = blockRef.ScaleFactors.Y;
                                meta["scale_z"] = blockRef.ScaleFactors.Z;

                                // CRITICAL: Extract block attributes directly into metadata (unified structure)
                                foreach (ObjectId attId in blockRef.AttributeCollection)
                                {
                                    try
                                    {
                                        AttributeReference attRef = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                                        meta[attRef.Tag] = attRef.TextString; // e.g., "TAG": "P-101A"
                                    }
                                    catch { }
                                }

                                entities.Add(semanticEntity);
                            }

                            // --- SMART PIPE/GRID SEGMENTS (Only on appropriate layers) ---
                            else if (entity is Polyline polyline)
                            {
                                // Only classify as pipe if explicitly on a piping layer
                                if (layerName.Contains("PIPE") || layerName.Contains("M_PIPE"))
                                {
                                    semanticEntity["asset_type"] = "PipeSegment";
                                    meta["length_mm"] = polyline.Length;

                                    List<Dictionary<string, double>> points = new List<Dictionary<string, double>>();
                                    for (int i = 0; i < polyline.NumberOfVertices; i++)
                                    {
                                        Point3d pt = polyline.GetPoint3dAt(i);
                                        points.Add(new Dictionary<string, double> { { "X", pt.X }, { "Y", pt.Y }, { "Z", pt.Z } });
                                    }
                                    meta["vertices"] = points;

                                    entities.Add(semanticEntity);
                                }
                                else if (layerName.Contains("GRID") || layerName.Contains("C_GRID"))
                                {
                                    semanticEntity["asset_type"] = "GridLine";
                                    meta["length_mm"] = polyline.Length;

                                    List<Dictionary<string, double>> points = new List<Dictionary<string, double>>();
                                    for (int i = 0; i < polyline.NumberOfVertices; i++)
                                    {
                                        Point3d pt = polyline.GetPoint3dAt(i);
                                        points.Add(new Dictionary<string, double> { { "X", pt.X }, { "Y", pt.Y }, { "Z", pt.Z } });
                                    }
                                    meta["vertices"] = points;

                                    entities.Add(semanticEntity);
                                }
                                else
                                {
                                    // Not a pipe, not a grid. Drop it.
                                    filteredOut++;
                                    continue;
                                }
                            }
                            // Drop standalone lines, arcs, circles that confuse the LLM
                            else
                            {
                                filteredOut++;
                                continue;
                            }
                        }
                        catch { }
                    }
                    tr.Commit();
                    ed.WriteMessage($"\nSilent: Semantic extraction complete. {entities.Count} assets processed, {filteredOut} junk entities filtered.");
                }

                // ── 3. Write drawing_data.json next to the .dwg ─────────────
                string json = ConvertToJson(entities);
                string outputPath = Path.Combine(Path.GetDirectoryName(doc.Name), "drawing_data.json");
                File.WriteAllText(outputPath, json);
                ed.WriteMessage($"\nSilent: {entities.Count} entities extracted → {outputPath}");

                string drawingName = drawingNameCfg ?? Path.GetFileNameWithoutExtension(doc.Name);

                // ── 4. Fire API call asynchronously; write result on completion
                ed.WriteMessage("\nSilent: Calling review API...");
                ed.WriteMessage($"\nSilent: Results will be written to: {SilentResultPath}");
                Task.Run(async () =>
                {
                    try
                    {
                        ed.WriteMessage("\nSilent: API request started...");
                        string response = await CallReviewEndpoint(
                            outputPath, drawingName, projectId, participantId, apiUrl);
                        ed.WriteMessage("\nSilent: API response received, checking for errors...");

                        // Check if response contains error information about llm_review
                        Dictionary<string, object> apiResponse = ParseJson(response);
                        string errorMessage = ExtractApiErrorMessage(apiResponse);

                        if (!string.IsNullOrEmpty(errorMessage))
                        {
                            ed.WriteMessage($"\nSilent: API returned error: {errorMessage}");
                            WriteSilentResult(false, errorMessage, response);
                        }
                        else
                        {
                            ed.WriteMessage("\nSilent: API response successful, writing result...");
                            WriteSilentResult(true, "Review completed successfully.", response);
                        }

                        ed.WriteMessage($"\nSilent: Result written to: {SilentResultPath}");
                    }
                    catch (System.Exception apiEx)
                    {
                        ed.WriteMessage($"\nSilent: API failed with error: {apiEx.Message}");
                        WriteSilentResult(false, $"API error: {apiEx.Message}", null);
                        ed.WriteMessage($"\nSilent: Error result written to: {SilentResultPath}");
                    }
                });
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSilent review error: {ex.Message}");
                WriteSilentResult(false, $"Plugin error: {ex.Message}", null);
            }
        }

        /// <summary>Write the review result to the shared temp result file.</summary>
        private void WriteSilentResult(bool success, string message, string apiResponseJson)
        {
            try
            {
                string dirPath = Path.GetDirectoryName(SilentResultPath);
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"success\": {success.ToString().ToLower()},");
                sb.AppendLine($"  \"message\": \"{EscapeJsonString(message)}\",");
                sb.AppendLine($"  \"timestamp\": \"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\",");
                if (!string.IsNullOrEmpty(apiResponseJson))
                    sb.AppendLine($"  \"api_response\": {apiResponseJson}");
                else
                    sb.AppendLine("  \"api_response\": null");
                sb.AppendLine("}");

                // Write with UTF-8 without BOM to avoid json.loads() parse errors in Python
                var utf8NoBom = new System.Text.UTF8Encoding(false);
                File.WriteAllText(SilentResultPath, sb.ToString(), utf8NoBom);

                // Verify file was written
                if (File.Exists(SilentResultPath))
                {
                    long fileSize = new FileInfo(SilentResultPath).Length;
                    Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                    {
                        doc.Editor.WriteMessage($"\nSilent: Result file confirmed ({fileSize} bytes): {SilentResultPath}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.Editor.WriteMessage($"\nSilent: ERROR writing result file: {ex.Message}");
                }
            }
        }

        private static string EscapeJsonString(string value)
        {
            if (value == null) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private static string GetSilentConfigValue(
            Dictionary<string, object> config, string key, string defaultValue)
        {
            if (config != null && config.ContainsKey(key) && config[key] != null)
            {
                string val = config[key].ToString();
                return string.IsNullOrWhiteSpace(val) ? defaultValue : val;
            }
            return defaultValue;
        }

        /// <summary>
        /// Extracts error messages from the API response.
        /// Checks for error field, and nested error information in llm_review or other sections.
        /// </summary>
        private string ExtractApiErrorMessage(Dictionary<string, object> apiResponse)
        {
            if (apiResponse == null || apiResponse.Count == 0)
                return null;

            // Check for top-level error field
            if (apiResponse.ContainsKey("error") && apiResponse["error"] != null)
            {
                string errorVal = apiResponse["error"].ToString();
                if (!string.IsNullOrWhiteSpace(errorVal))
                    return $"API Error: {errorVal}";
            }

            // Check for success=false
            if (apiResponse.ContainsKey("success") && apiResponse["success"] != null)
            {
                string successVal = apiResponse["success"].ToString().ToLower();
                if (successVal == "false")
                {
                    if (apiResponse.ContainsKey("message") && apiResponse["message"] != null)
                    {
                        string msg = apiResponse["message"].ToString();
                        if (!string.IsNullOrWhiteSpace(msg))
                            return msg;
                    }
                    return "API returned failure status";
                }
            }

            // Check for error in geometric_analysis
            if (apiResponse.ContainsKey("geometric_analysis") && apiResponse["geometric_analysis"] is Dictionary<string, object> geoAnalysis)
            {
                if (geoAnalysis.ContainsKey("error") && geoAnalysis["error"] != null)
                {
                    string errorVal = geoAnalysis["error"].ToString();
                    if (!string.IsNullOrWhiteSpace(errorVal))
                        return $"Geometric Analysis Error: {errorVal}";
                }
            }

            // Check for error in llm_review section
            if (apiResponse.ContainsKey("llm_review_error") && apiResponse["llm_review_error"] != null)
            {
                string errorVal = apiResponse["llm_review_error"].ToString();
                if (!string.IsNullOrWhiteSpace(errorVal))
                    return $"LLM Review Error: {errorVal}";
            }

            // No error found
            return null;
        }

        /// <summary>
        /// Extracts semantic metadata for DBText entities.
        /// </summary>
        private Dictionary<string, object> ExtractDBTextMetadata(DBText dbText, Transaction tr)
        {
            Dictionary<string, object> metadata = new Dictionary<string, object>();

            try
            {
                try
                {
                    // Extract actual text content
                    string content = dbText.TextString ?? "";
                    metadata["content"] = content;

                    // Extract text height
                    metadata["text_height"] = dbText.Height;

                    // Extract rotation
                    metadata["rotation"] = dbText.Rotation;
                }
                catch (System.Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error extracting DBText properties: {innerEx.Message}");
                    // Return empty metadata rather than throwing
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ExtractDBTextMetadata: {ex.Message}");
            }

            return metadata;
        }

        /// <summary>
        /// Extracts semantic metadata for MText entities, with unicode character decoding.
        /// </summary>
        private Dictionary<string, object> ExtractMTextMetadata(MText mtext, Transaction tr)
        {
            Dictionary<string, object> metadata = new Dictionary<string, object>();

            try
            {
                try
                {
                    // Extract clean text (mtext.Text gives unformatted content without AutoCAD codes)
                    string content = "";
                    try
                    {
                        content = mtext.Text ?? "";
                    }
                    catch
                    {
                        // Fallback in case Text property is not available
                        content = "";
                    }

                    metadata["content"] = content;

                    // Extract text height
                    metadata["text_height"] = mtext.Height;

                    // Extract rotation
                    metadata["rotation"] = mtext.Rotation;
                }
                catch (System.Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error extracting MText properties: {innerEx.Message}");
                    // Return empty metadata rather than throwing
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ExtractMTextMetadata: {ex.Message}");
            }

            return metadata;
        }

        /// <summary>
        /// Decodes MText content, handling formatting codes and unicode escapes.
        /// Converts {\U+XXXX} sequences to actual characters.
        /// </summary>
        private string DecodeMTextContent(string mtextContent)
        {
            if (string.IsNullOrEmpty(mtextContent))
                return "";

            StringBuilder decoded = new StringBuilder();
            int i = 0;

            while (i < mtextContent.Length)
            {
                // Check for unicode escape sequence {\U+XXXX}
                if (i < mtextContent.Length - 7 && mtextContent[i] == '{' && mtextContent[i + 1] == '\\')
                {
                    int closeIdx = mtextContent.IndexOf('}', i);
                    if (closeIdx > i && mtextContent[i + 2] == 'U' && mtextContent[i + 3] == '+')
                    {
                        string hexPart = mtextContent.Substring(i + 4, closeIdx - i - 4);
                        try
                        {
                            // Convert unicode hex to character
                            int codePoint = int.Parse(hexPart, System.Globalization.NumberStyles.HexNumber);
                            if (codePoint >= 0 && codePoint <= 0x10FFFF)
                            {
                                decoded.Append(char.ConvertFromUtf32(codePoint));
                                i = closeIdx + 1;
                                continue;
                            }
                        }
                        catch { }
                    }
                }

                // Check for other MText formatting codes like \S, \P, etc. and skip them
                if (mtextContent[i] == '\\' && i + 1 < mtextContent.Length)
                {
                    char nextChar = mtextContent[i + 1];
                    // Skip formatting codes but preserve content
                    if (nextChar == 'S' || nextChar == 'P' || nextChar == 'L' || nextChar == 'l' || 
                        nextChar == 'O' || nextChar == 'o' || nextChar == 'K' || nextChar == 'k' ||
                        nextChar == 'T' || nextChar == 'Q' || nextChar == 'W' || nextChar == 'A' ||
                        nextChar == 'H' || nextChar == 'C' || nextChar == 'F' || nextChar == 'n' ||
                        nextChar == '~' || nextChar == '^')
                    {
                        i += 2;
                        // Skip until we find the closing character or space
                        if (i < mtextContent.Length && mtextContent[i] == '[')
                        {
                            int closeBracket = mtextContent.IndexOf(']', i);
                            if (closeBracket > i)
                                i = closeBracket + 1;
                        }
                        continue;
                    }
                }

                // Check for curly brace formatting groups and skip the braces but keep content
                if (mtextContent[i] == '{')
                {
                    i++; // Skip opening brace
                    while (i < mtextContent.Length && mtextContent[i] != '}')
                    {
                        decoded.Append(mtextContent[i]);
                        i++;
                    }
                    if (i < mtextContent.Length && mtextContent[i] == '}')
                        i++; // Skip closing brace
                    continue;
                }

                // Regular character
                decoded.Append(mtextContent[i]);
                i++;
            }

            return decoded.ToString();
        }

        /// <summary>
        /// Extracts semantic metadata for BlockReference entities, including block name and attributes.
        /// Handles dynamic blocks properly and translates anonymous block names.
        /// </summary>
        private Dictionary<string, object> ExtractBlockReferenceMetadata(BlockReference blockRef, Transaction tr)
        {
            Dictionary<string, object> metadata = new Dictionary<string, object>();

            try
            {
                try
                {
                    // Extract block name - handles dynamic blocks and translates anonymous names
                    string blockName = GetTranslatedBlockName(blockRef, tr);
                    metadata["block_name"] = blockName ?? "";

                    // Check if this is a dynamic block
                    bool isDynamicBlock = blockRef.IsDynamicBlock;
                    metadata["IsDynamicBlock"] = isDynamicBlock;
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error extracting block name/dynamic status: {ex.Message}");
                }

                // Extract attributes
                try
                {
                    foreach (ObjectId attRefId in blockRef.AttributeCollection)
                    {
                        try
                        {
                            AttributeReference attRef = (AttributeReference)tr.GetObject(attRefId, OpenMode.ForRead);
                            if (attRef != null)
                            {
                                string tag = attRef.Tag ?? "";
                                string value = attRef.TextString ?? "";

                                // Only add non-empty values, or add empty string if tag is important
                                if (!string.IsNullOrEmpty(tag))
                                {
                                    metadata[tag] = value;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error extracting block attributes: {ex.Message}");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ExtractBlockReferenceMetadata: {ex.Message}");
            }

            return metadata;
        }

        /// <summary>
        /// Translates anonymous or dynamic block names to their semantic equivalents using the fingerprinting engine.
        /// This integrates with fingerprints.json to dynamically classify blocks based on geometry.
        /// 
        /// Priority: Dictionary lookup ALWAYS takes precedence over fingerprinting.
        /// </summary>
        private string GetTranslatedBlockName(BlockReference blockRef, Transaction tr)
        {
            string targetName = blockRef.Name ?? "";

            try
            {
                // 1. Resolve true name if Dynamic Block (DO NOT return early!)
                if (blockRef.IsDynamicBlock)
                {
                    try
                    {
                        BlockTableRecord dynamicBlockDef = (BlockTableRecord)tr.GetObject(blockRef.DynamicBlockTableRecord, OpenMode.ForRead);
                        if (!string.IsNullOrEmpty(dynamicBlockDef.Name))
                        {
                            targetName = dynamicBlockDef.Name;
                        }
                    }
                    catch { }
                }

                // 2. Check Dictionary and Fingerprint against the TARGET name
                if (targetName.StartsWith("*") || targetName.Contains("$"))
                {
                    // PRIORITY 1: Check Dictionary FIRST
                    string dictMatch = LookupBlockTranslation(targetName);
                    if (!string.IsNullOrEmpty(dictMatch))
                    {
                        return dictMatch; // Safely returns "STRAINER" or "PRESSURE_GAUGE"
                    }

                    // PRIORITY 2: Fallback to Fingerprinting
                    string fingerprintResult = FingerprintAnonymousBlock(blockRef.BlockId, tr, null, null);
                    if (!string.IsNullOrEmpty(fingerprintResult) && fingerprintResult != "UNKNOWN_COMPONENT")
                    {
                        return fingerprintResult;
                    }
                }

                // 3. Fallback to the target name if no rules match
                return targetName;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetTranslatedBlockName: {ex.Message}");
                return targetName;
            }
        }

        /// <summary>
        /// Looks up anonymous block name translations from a block dictionary.
        /// This method maps raw anonymous block names to their semantic equivalents.
        /// Example: A$C157444E+3 maps to "STRAINER", A$C04F129AC maps to "CHECK_VALVE", etc.
        /// </summary>
        private string LookupBlockTranslation(string anonymousName)
        {
            // Block dictionary mapping anonymous block names to semantic names
            // Populate this dictionary with your actual block name mappings from the drawing
            Dictionary<string, string> blockDictionary = new Dictionary<string, string>
            {
                // Add mappings discovered from your drawing here
                // Format: { "raw_anonymous_name", "semantic_name" }
                { "A$C157444E+3", "STRAINER" },
                { "A$C04F129AC", "CHECK_VALVE" },
                { "*X109", "PRESSURE_GAUGE" }
            };

            // Check if translation exists
            if (blockDictionary.ContainsKey(anonymousName))
            {
                string translated = blockDictionary[anonymousName];
                System.Diagnostics.Debug.WriteLine($"Block translation: {anonymousName} -> {translated}");
                return translated;
            }

            // Log unmatched anonymous blocks for debugging
            System.Diagnostics.Debug.WriteLine($"No translation found for anonymous block: {anonymousName}");
            return null;
        }

        /// <summary>
        /// Generates a unique entity ID based on entity type and index.
        /// </summary>
        private string GenerateEntityId(string entityType, int index)
        {
            // Generate IDs like TEXT_1, EQUIP_2, etc.
            string prefix = "";
            switch (entityType)
            {
                case "DBText":
                case "MText":
                    prefix = "TEXT";
                    break;
                case "BlockReference":
                    prefix = "EQUIP";
                    break;
                case "Line":
                    prefix = "LINE";
                    break;
                case "Circle":
                    prefix = "CIRC";
                    break;
                case "Arc":
                    prefix = "ARC";
                    break;
                case "Polyline":
                    prefix = "POLY";
                    break;
                default:
                    prefix = entityType.Substring(0, Math.Min(4, entityType.Length)).ToUpper();
                    break;
            }
            return $"{prefix}_{index}";
        }

        /// <summary>
        /// Determines the semantic asset type based on AutoCAD entity type.
        /// </summary>
        private string GetSemanticAssetType(string entityType)
        {
            switch (entityType)
            {
                case "DBText":
                case "MText":
                    return "TextCallout";
                case "BlockReference":
                    return "Equipment";
                default:
                    return "Generic";
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // DYNAMIC FINGERPRINTING ENGINE
        // Matches anonymous blocks to asset types using JSON-configured rules.
        // Zero-downtime customization: add new rules to fingerprints.json,
        // no C# recompilation required.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Fingerprints an anonymous block by:
        /// 1. Using the provided department (from request or layer detection)
        /// 2. Tallying its internal geometry
        /// 3. Matching against department-filtered rules
        /// 
        /// Returns ALL matching rule names separated by " / ", or fallback if no matches.
        /// </summary>
        private string FingerprintAnonymousBlock(ObjectId blockRecordId, Transaction tr, 
            Editor ed = null, string providedDepartment = null)
        {
            try
            {
                BlockTableRecord blockDef = (BlockTableRecord)tr.GetObject(blockRecordId, OpenMode.ForRead);

                // 1. Determine the block's department
                // Priority: Provided department (from request) > Layer-based detection
                string blockDepartment = providedDepartment;
                string departmentSource = "request";

                if (string.IsNullOrWhiteSpace(blockDepartment))
                {
                    blockDepartment = DetermineDepartmentFromEntity(blockRecordId, tr);
                    departmentSource = "layer";
                }

                if (ed != null)
                    ed.WriteMessage($"\n  → Department: {blockDepartment} (from {departmentSource})");

                // 2. Tally all geometry inside the anonymous block
                GeometryTally tally = new GeometryTally();

                foreach (ObjectId id in blockDef)
                {
                    try
                    {
                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (ent is Circle)
                            tally.Circles++;
                        else if (ent is Line)
                            tally.Lines++;
                        else if (ent is Polyline)
                            tally.Polylines++;
                        else if (ent is Arc)
                            tally.Arcs++;
                        else if (ent is Hatch)
                            tally.Hatches++;
                        else if (ent is DBText || ent is MText)
                            tally.Texts++;
                    }
                    catch { }
                }

                // 3. Load fingerprint rules from configuration
                ConfigManager configMgr = ConfigManager.GetInstance();
                List<FingerprintRule> allRules = configMgr.GetFingerprintRules();

                if (allRules == null || allRules.Count == 0)
                {
                    if (ed != null)
                        ed.WriteMessage($"\nWarning: No fingerprint rules loaded.");
                    return "UNKNOWN_COMPONENT";
                }

                // 4. Filter rules by department (include rules with empty department = applies to all)
                List<FingerprintRule> applicableRules = new List<FingerprintRule>();
                foreach (var rule in allRules)
                {
                    // Include rule if:
                    // - Its department matches the block's department, OR
                    // - It has no department specified (applies to all departments)
                    if (string.IsNullOrWhiteSpace(rule.Department) || 
                        rule.Department.Equals(blockDepartment, System.StringComparison.OrdinalIgnoreCase))
                    {
                        applicableRules.Add(rule);
                    }
                }

                if (applicableRules.Count == 0)
                {
                    if (ed != null)
                        ed.WriteMessage($"\n  → No rules for department: {blockDepartment}");
                    return "UNKNOWN_COMPONENT";
                }

                // 5. Collect ALL matching rules
                List<string> matches = new List<string>();

                foreach (FingerprintRule rule in applicableRules)
                {
                    if (rule.IsMatch(tally))
                    {
                        matches.Add(rule.AssignedName);
                    }
                }

                // 6. Return all matches or fallback
                if (matches.Count > 0)
                {
                    string result = string.Join(" / ", matches);
                    if (ed != null)
                    {
                        ed.WriteMessage($"\n  → Matches ({applicableRules.Count} rules evaluated): {result} ({tally})");
                    }
                    return result;
                }
                else
                {
                    // No rules matched
                    if (ed != null)
                    {
                        ed.WriteMessage($"\n  → No matches for geometry: {tally} (checked {applicableRules.Count} rules)");
                        ed.WriteMessage($"\n  → Using fallback: UNKNOWN_COMPONENT");
                    }
                    return "UNKNOWN_COMPONENT";
                }
            }
            catch (System.Exception ex)
            {
                if (ed != null)
                    ed.WriteMessage($"\nError fingerprinting block: {ex.Message}");
                return "UNKNOWN_COMPONENT";
            }
        }

        /// <summary>
        /// Determines the department of a block based on layer naming conventions.
        /// 
        /// Common layer naming patterns:
        /// - "M_*" or "*_MECH*" → Mechanical
        /// - "E_*" or "*_ELEC*" → Electrical
        /// - "I_*" or "*_INST*" → Instrumentation
        /// - "P_*" or "*_PIPE*" → Piping
        /// - Otherwise → Generic (matches all-department rules)
        /// </summary>
        private string DetermineDepartmentFromEntity(ObjectId blockRefId, Transaction tr)
        {
            try
            {
                // For anonymous blocks, we determine department from layer of the BlockReference
                // that uses them. Since we're in extraction, we can use a simple heuristic.
                // In a real scenario, you might store department in block attributes or xdata.

                // For now, return "Generic" to match all-department rules
                return "Generic";
            }
            catch
            {
                return "Generic";
            }
        }

        /// <summary>
        /// Determines department based on layer name using common naming conventions.
        /// Department codes are defined in fingerprints.json and database.
        /// Returns empty string for "Generic" (matches all-department rules).
        /// </summary>
        private string DetermineDepartmentFromLayer(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return "";  // Generic

            layerName = layerName.ToUpper();

            // Mechanical: M_*, *_MECH*, *_EQUIPMENT*, PUMP, VALVE, FITTING
            if (layerName.StartsWith("M_") || layerName.Contains("_MECH") || 
                layerName.Contains("_EQUIP") || layerName.Contains("PUMP") || 
                layerName.Contains("VALVE") || layerName.Contains("FITTING"))
                return "MECHANICAL";

            // Electrical: E_*, *_ELEC*, *_POWER*, CABLE, CONDUIT
            if (layerName.StartsWith("E_") || layerName.Contains("_ELEC") || 
                layerName.Contains("_POWER") || layerName.Contains("CABLE") || 
                layerName.Contains("CONDUIT"))
                return "ELECTRICAL";

            // Instrumentation: I_*, *_INST*, INSTRUMENT, SENSOR
            if (layerName.StartsWith("I_") || layerName.Contains("_INST") || 
                layerName.Contains("INSTRUMENT") || layerName.Contains("SENSOR"))
                return "I&C";

            // Process/Piping: P_*, *_PIPE*, PIPELINE
            if (layerName.StartsWith("P_") || layerName.Contains("_PIPE") || 
                layerName.Contains("PIPELINE"))
                return "PROCESS";

            // Civil: C_*, *_CIVIL*, STRUCTURAL, CSA
            if (layerName.StartsWith("C_") || layerName.Contains("_CIVIL") || 
                layerName.Contains("STRUCTURAL") || layerName.Contains("CSA"))
                return "CSA";

            // Default: generic (matches all-department rules)
            return "";
        }
    }
}
