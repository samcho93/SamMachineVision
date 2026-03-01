using System.Collections;
using System.Text;
using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

/// <summary>
/// Print node for debugging: displays any data type as formatted text
/// directly inside the node using WPF TextBlock (not image-based preview).
/// </summary>
[NodeInfo("Print", NodeCategories.Value,
    Description = "Display any value as formatted text in node")]
public class PrintNode : BaseNode
{
    private InputPort<object> _input0 = null!;
    private InputPort<object> _input1 = null!;
    private InputPort<object> _input2 = null!;
    private InputPort<object> _input3 = null!;
    private OutputPort<string> _textOutput = null!;

    private NodeProperty _maxItems = null!;
    private NodeProperty _showType = null!;
    private NodeProperty _label0 = null!;
    private NodeProperty _label1 = null!;
    private NodeProperty _label2 = null!;
    private NodeProperty _label3 = null!;

    protected override void Setup()
    {
        _input0 = AddInput<object>("In 0");
        _input1 = AddInput<object>("In 1");
        _input2 = AddInput<object>("In 2");
        _input3 = AddInput<object>("In 3");

        _textOutput = AddOutput<string>("Text");

        _maxItems = AddIntProperty("MaxItems", "Max Items", 20, 1, 200, "Maximum array items to display");
        _showType = AddBoolProperty("ShowType", "Show Type", true, "Show data type name");
        _label0 = AddStringProperty("Label0", "Label 0", "", "Custom label for Input 0");
        _label1 = AddStringProperty("Label1", "Label 1", "", "Custom label for Input 1");
        _label2 = AddStringProperty("Label2", "Label 2", "", "Custom label for Input 2");
        _label3 = AddStringProperty("Label3", "Label 3", "", "Custom label for Input 3");
    }

    public override void Process()
    {
        try
        {
            var inputs = new[]
            {
                (Value: GetInputValue(_input0), Label: _label0.GetValue<string>(), Name: "In 0"),
                (Value: GetInputValue(_input1), Label: _label1.GetValue<string>(), Name: "In 1"),
                (Value: GetInputValue(_input2), Label: _label2.GetValue<string>(), Name: "In 2"),
                (Value: GetInputValue(_input3), Label: _label3.GetValue<string>(), Name: "In 3"),
            };

            var maxItems = _maxItems.GetValue<int>();
            var showType = _showType.GetValue<bool>();

            var sb = new StringBuilder();
            bool hasAnyInput = false;

            foreach (var (value, label, name) in inputs)
            {
                if (value == null && !IsPortConnected(name)) continue;
                hasAnyInput = true;

                if (sb.Length > 0) sb.AppendLine();

                var displayLabel = string.IsNullOrEmpty(label) ? name : label;
                FormatValue(sb, displayLabel, value, showType, maxItems);
            }

            if (!hasAnyInput)
            {
                sb.Append("(no input)");
            }

            var text = sb.ToString();
            SetOutputValue(_textOutput, text);
            SetTextPreview(text);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Print error: {ex.Message}";
        }
    }

    private bool IsPortConnected(string portName)
    {
        return Inputs.Any(p => p.Name == portName && p.IsConnected);
    }

    private static void FormatValue(StringBuilder sb, string label, object? value, bool showType, int maxItems)
    {
        if (value == null)
        {
            sb.AppendLine($"[{label}] null");
            return;
        }

        var type = value.GetType();
        var typeName = GetFriendlyTypeName(type);

        // Header
        if (showType)
            sb.AppendLine($"[{label}] ({typeName})");
        else
            sb.AppendLine($"[{label}]");

        // Format based on type
        switch (value)
        {
            case Mat mat:
                FormatMat(sb, mat);
                break;

            case Point[] points:
                sb.AppendLine($"  Count: {points.Length}");
                for (int i = 0; i < Math.Min(points.Length, maxItems); i++)
                    sb.AppendLine($"  [{i}] ({points[i].X}, {points[i].Y})");
                if (points.Length > maxItems)
                    sb.AppendLine($"  ... +{points.Length - maxItems} more");
                break;

            case Point2f[] pts2f:
                sb.AppendLine($"  Count: {pts2f.Length}");
                for (int i = 0; i < Math.Min(pts2f.Length, maxItems); i++)
                    sb.AppendLine($"  [{i}] ({pts2f[i].X:F2}, {pts2f[i].Y:F2})");
                if (pts2f.Length > maxItems)
                    sb.AppendLine($"  ... +{pts2f.Length - maxItems} more");
                break;

            case Rect[] rects:
                sb.AppendLine($"  Count: {rects.Length}");
                for (int i = 0; i < Math.Min(rects.Length, maxItems); i++)
                    sb.AppendLine($"  [{i}] X={rects[i].X} Y={rects[i].Y} {rects[i].Width}x{rects[i].Height}");
                if (rects.Length > maxItems)
                    sb.AppendLine($"  ... +{rects.Length - maxItems} more");
                break;

            case double[] doubles:
                sb.AppendLine($"  Count: {doubles.Length}");
                for (int i = 0; i < Math.Min(doubles.Length, maxItems); i++)
                    sb.AppendLine($"  [{i}] {doubles[i]:F4}");
                if (doubles.Length > maxItems)
                    sb.AppendLine($"  ... +{doubles.Length - maxItems} more");
                break;

            case float[] floats:
                sb.AppendLine($"  Count: {floats.Length}");
                for (int i = 0; i < Math.Min(floats.Length, maxItems); i++)
                    sb.AppendLine($"  [{i}] {floats[i]:F4}");
                if (floats.Length > maxItems)
                    sb.AppendLine($"  ... +{floats.Length - maxItems} more");
                break;

            case int[] ints:
                sb.AppendLine($"  Count: {ints.Length}");
                for (int i = 0; i < Math.Min(ints.Length, maxItems); i++)
                    sb.AppendLine($"  [{i}] {ints[i]}");
                if (ints.Length > maxItems)
                    sb.AppendLine($"  ... +{ints.Length - maxItems} more");
                break;

            case string[] strings:
                sb.AppendLine($"  Count: {strings.Length}");
                for (int i = 0; i < Math.Min(strings.Length, maxItems); i++)
                    sb.AppendLine($"  [{i}] \"{strings[i]}\"");
                if (strings.Length > maxItems)
                    sb.AppendLine($"  ... +{strings.Length - maxItems} more");
                break;

            case byte[] bytes:
                sb.AppendLine($"  Length: {bytes.Length}");
                if (bytes.Length > 0)
                {
                    var hex = string.Join(" ", bytes.Take(Math.Min(16, bytes.Length)).Select(b => b.ToString("X2")));
                    if (bytes.Length > 16) hex += " ...";
                    sb.AppendLine($"  {hex}");
                }
                break;

            case bool boolVal:
                sb.AppendLine($"  {boolVal}");
                break;

            case int intVal:
                sb.AppendLine($"  {intVal}");
                break;

            case long longVal:
                sb.AppendLine($"  {longVal}");
                break;

            case float floatVal:
                sb.AppendLine($"  {floatVal:F4}");
                break;

            case double doubleVal:
                sb.AppendLine($"  {doubleVal:F6}");
                break;

            case string strVal:
                var strLines = strVal.Split('\n');
                foreach (var sl in strLines.Take(maxItems))
                    sb.AppendLine($"  {sl.TrimEnd('\r')}");
                if (strLines.Length > maxItems)
                    sb.AppendLine($"  ... +{strLines.Length - maxItems} more lines");
                break;

            case Point pt:
                sb.AppendLine($"  ({pt.X}, {pt.Y})");
                break;

            case Point2f pt2f:
                sb.AppendLine($"  ({pt2f.X:F2}, {pt2f.Y:F2})");
                break;

            case Rect rect:
                sb.AppendLine($"  X={rect.X} Y={rect.Y} {rect.Width}x{rect.Height}");
                break;

            case Size size:
                sb.AppendLine($"  {size.Width} x {size.Height}");
                break;

            case Scalar scalar:
                sb.AppendLine($"  ({scalar.Val0:F1}, {scalar.Val1:F1}, {scalar.Val2:F1}, {scalar.Val3:F1})");
                break;

            case IList list:
                sb.AppendLine($"  Count: {list.Count}");
                for (int i = 0; i < Math.Min(list.Count, maxItems); i++)
                {
                    try { sb.AppendLine($"  [{i}] {list[i]}"); }
                    catch { sb.AppendLine($"  [{i}] (error)"); }
                }
                if (list.Count > maxItems)
                    sb.AppendLine($"  ... +{list.Count - maxItems} more");
                break;

            default:
                var str = value.ToString() ?? "";
                var fallbackLines = str.Split('\n');
                foreach (var fl in fallbackLines.Take(maxItems))
                    sb.AppendLine($"  {fl.TrimEnd('\r')}");
                break;
        }
    }

    private static void FormatMat(StringBuilder sb, Mat mat)
    {
        if (mat.Empty())
        {
            sb.AppendLine("  (empty)");
            return;
        }

        sb.AppendLine($"  Size: {mat.Width} x {mat.Height}");
        sb.AppendLine($"  Ch: {mat.Channels()}  Type: {mat.Type()}");

        // Center pixel value
        int cx = mat.Width / 2, cy = mat.Height / 2;
        try
        {
            if (mat.Channels() == 1)
            {
                var val = mat.At<byte>(cy, cx);
                sb.AppendLine($"  Center[{cx},{cy}]: {val}");
            }
            else if (mat.Channels() == 3)
            {
                var val = mat.At<Vec3b>(cy, cx);
                sb.AppendLine($"  Center[{cx},{cy}]: B={val.Item0} G={val.Item1} R={val.Item2}");
            }
        }
        catch { /* ignore */ }
    }

    private static string GetFriendlyTypeName(Type type)
    {
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(string)) return "string";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(Mat)) return "Mat";
        if (type == typeof(Point)) return "Point";
        if (type == typeof(Point2f)) return "Point2f";
        if (type == typeof(Rect)) return "Rect";
        if (type == typeof(Size)) return "Size";
        if (type == typeof(Scalar)) return "Scalar";
        if (type == typeof(int[])) return "int[]";
        if (type == typeof(float[])) return "float[]";
        if (type == typeof(double[])) return "double[]";
        if (type == typeof(string[])) return "string[]";
        if (type == typeof(byte[])) return "byte[]";
        if (type == typeof(Point[])) return "Point[]";
        if (type == typeof(Point2f[])) return "Point2f[]";
        if (type == typeof(Rect[])) return "Rect[]";
        if (type.IsArray) return $"{type.GetElementType()?.Name}[]";
        if (type.IsGenericType)
        {
            var genArgs = string.Join(", ", type.GetGenericArguments().Select(t => t.Name));
            return $"{type.Name.Split('`')[0]}<{genArgs}>";
        }
        return type.Name;
    }
}
