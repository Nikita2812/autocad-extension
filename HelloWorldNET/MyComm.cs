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
                        ed.WriteMessage($"\n\nAPI Response:\n{response}");
                        Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog($"API Response:\n\n{response}");
                    }
                    catch (System.Exception apiEx)
                    {
                        ed.WriteMessage($"\nAPI Error: {apiEx.Message}");
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

                    // Send POST request
                    HttpResponseMessage response = await client.PostAsync(
                        "https://sesphase2.backend.testing.env.thelinkai.com/drawings/review",
                        form);

                    // Read response content
                    string responseContent = await response.Content.ReadAsStringAsync();

                    // Return formatted response
                    if (response.IsSuccessStatusCode)
                    {
                        return $"Success (Status: {response.StatusCode})\n\n{responseContent}";
                    }
                    else
                    {
                        return $"Error (Status: {response.StatusCode})\n\n{responseContent}";
                    }
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
    }
}
