# JSON SERIALIZATION FIX - DETAILED EXAMPLES

## Example 1: Equipment Block with Attributes

### BEFORE FIX (Invalid JSON)
```json
{
  "asset_type": "Equipment",
  "layer": "M_EQUIP",
  "position": { "X": 100.5, "Y": 200.75, "Z": 0 },
  "metadata": [["block_name", "*U26"], ["rotation", 0], ["scale_x", 1.0], ["scale_y", 1.0], ["scale_z", 1.0], ["TAG", "P-101A"], ["SIZE", "2x4"]]
}
```

❌ **Issues:**
- Keys like `block_name` and values like `*U26` are **not quoted**
- Array of pairs instead of object structure
- Python's `json.loads()` fails with `JSONDecodeError`

### AFTER FIX (Valid JSON)
```json
{
  "asset_type": "Equipment",
  "layer": "M_EQUIP",
  "position": { "X": 100.5, "Y": 200.75, "Z": 0 },
  "metadata": {
    "block_name": "*U26",
    "rotation": 0.0,
    "scale_x": 1.0,
    "scale_y": 1.0,
    "scale_z": 1.0,
    "TAG": "P-101A",
    "SIZE": "2x4"
  }
}
```

✅ **Benefits:**
- All strings are properly quoted
- Semantically correct as an object
- Python can parse with `json.loads(json_string)`
- LLM understands the structure

---

## Example 2: Text Callout with Metadata

### BEFORE FIX (Invalid JSON)
```json
{
  "asset_type": "TextCallout",
  "layer": "M_ANNO_TEXT",
  "position": { "X": 50.0, "Y": 75.0, "Z": 0 },
  "metadata": [["content", "PUMP DISCHARGE"], ["height", 2.5], ["rotation", 0.785]]
}
```

### AFTER FIX (Valid JSON)
```json
{
  "asset_type": "TextCallout",
  "layer": "M_ANNO_TEXT",
  "position": { "X": 50.0, "Y": 75.0, "Z": 0 },
  "metadata": {
    "content": "PUMP DISCHARGE",
    "height": 2.5,
    "rotation": 0.785
  }
}
```

---

## Example 3: Pipe Segment with Vertices

### BEFORE FIX (Invalid JSON)
```json
{
  "asset_type": "PipeSegment",
  "layer": "M_PIPE",
  "metadata": [["length_mm", 1500], ["vertices", [{"X": 0, "Y": 0, "Z": 0}, {"X": 100, "Y": 0, "Z": 0}, {"X": 100, "Y": 50, "Z": 0}]]]
}
```

### AFTER FIX (Valid JSON)
```json
{
  "asset_type": "PipeSegment",
  "layer": "M_PIPE",
  "metadata": {
    "length_mm": 1500,
    "vertices": [
      {"X": 0, "Y": 0, "Z": 0},
      {"X": 100, "Y": 0, "Z": 0},
      {"X": 100, "Y": 50, "Z": 0}
    ]
  }
}
```

---

## Code Path Comparison

### BEFORE: Dictionary → IEnumerable → KeyValuePair Array

```
dictionary { "key": "value" }
    ↓
Treated as IEnumerable (because Dictionary implements it)
    ↓
Converted to list of KeyValuePairs
    ↓
Serialized as: [["key", "value"]]  ← INVALID JSON
```

### AFTER: Dictionary → SerializeDictionary → JSON Object

```
dictionary { "key": "value" }
    ↓
Explicit Dictionary check catches it FIRST
    ↓
Calls SerializeDictionary() method
    ↓
Serialized as: {"key": "value"}  ← VALID JSON
```

---

## Technical Details

### Order of Checks in `GetJsonValue()`

```csharp
// CRITICAL: This check must come BEFORE the IEnumerable check!
if (value is Dictionary<string, object> dict)
    return SerializeDictionary(dict);  // ← Handle dict first
    
// This would incorrectly handle dict if placed first
else if (value is System.Collections.IEnumerable && !(value is string))
    return SerializeList(value);  // ← Would treat dict as list!
```

### Proper Escaping

The fix also ensures proper JSON escaping:

```csharp
string key = "user\"name";  // Contains quote
string escaped = EscapeJsonString(key);  // Results in: user\"name
// Output: "user\"name"  ← Properly escaped in JSON
```

---

## Validation

### Python Test Code

```python
import json
from pathlib import Path

# This will now work!
with open("drawing_data.json") as f:
    data = json.loads(f.read())  # ✅ No JSONDecodeError
    
for entity in data:
    if "metadata" in entity:
        metadata = entity["metadata"]
        assert isinstance(metadata, dict), f"metadata should be dict, got {type(metadata)}"
        print(f"✓ {entity['asset_type']}: metadata is valid")
```

### Sample Output

```
✓ Equipment: metadata is valid
✓ TextCallout: metadata is valid
✓ PipeSegment: metadata is valid
✓ GridLine: metadata is valid
```

---

## Migration Guide

If you're updating to this fixed version:

1. **No code changes needed** in your C# code
2. **Regenerate drawings** to create new JSON files
3. **Update Python consumers** to expect metadata as dict instead of list:

```python
# Old code (doesn't work)
for item in entity["metadata"]:
    key, value = item  # ← Would fail now

# New code (correct)
for key, value in entity["metadata"].items():
    print(f"{key}: {value}")
```

---

## Performance Impact

- ✅ **Negligible**: One additional `is Dictionary` check per value
- ✅ **No regex or reflection** overhead
- ✅ **Same time complexity**: O(n) where n = number of key-value pairs
- ✅ **Memory**: Same as before

---

## Backward Compatibility

⚠️ **Breaking Change for JSON Consumers**
- Old systems expecting `metadata` as array will fail
- Requires updating Python/JSON parsing code
- Recommended: Update all systems to use corrected format
- Benefit: Proper JSON compliance worth the migration effort
