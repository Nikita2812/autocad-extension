using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[assembly: CommandClass(typeof(HelloWorldNET.MyComm))]
namespace HelloWorldNET
{
    public class MyComm
    {
        [CommandMethod("HelloWorld")]
        public void HelloWorld()
        {
            Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog("Hello, World!");
        }

        [CommandMethod("AddTwoNumbers")]
        public void HelloWorld2()
        {
            double num1 = 5.0;
            double num2 = 10.0;
            double sum = num1 + num2;
            Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog($"The sum of {num1} and {num2} is {sum}.");
        }

        [CommandMethod("hileo")]
        public void Heyo()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            ed.WriteMessage("hellow worlds");
        }

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

                // Convert to JSON (using simple string building since Json.NET may not be available)
                string json = ConvertToJson(entities);

                // Save to file
                string outputPath = Path.Combine(Path.GetDirectoryName(doc.Name), "drawing_data.json");
                File.WriteAllText(outputPath, json);

                ed.WriteMessage($"\nDrawing data extracted to: {outputPath}");
                Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog($"JSON extracted successfully!\nFile: {outputPath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError: {ex.Message}");
                Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog($"Error extracting JSON: {ex.Message}");
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
